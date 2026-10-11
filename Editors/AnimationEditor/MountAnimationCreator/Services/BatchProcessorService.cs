using System.IO;
using AnimationEditor.MountAnimationCreator;
using AnimationEditor.MountAnimationCreator.Services;
using GameWorld.Core.Animation;
using GameWorld.Core.Services;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.AnimationVisualEditors.MountAnimationCreator.Services
{
    class BatchProcessorService
    {
        private readonly IPackFileService _pfs;
        private readonly ISkeletonAnimationLookUpHelper _lookup;
        private readonly MountAnimationGeneratorService _generator;
        private readonly BatchProcessOptions _options;
        private readonly IFileSaveService _saveService;
        private readonly IStandardDialogs _dialogs;
        private readonly uint _outputFormat;
        private readonly bool _ensureUnique;
        private readonly HashSet<string> _reservedPaths = new(StringComparer.OrdinalIgnoreCase);

        public BatchProcessorService(IPackFileService pfs, ISkeletonAnimationLookUpHelper lookup,
            MountAnimationGeneratorService generator, BatchProcessOptions options,
            IFileSaveService saveService, IStandardDialogs dialogs, uint outputFormat, bool ensureUnique = true)
        {
            _pfs = pfs;
            _lookup = lookup;
            _generator = generator;
            _options = options;
            _saveService = saveService;
            _dialogs = dialogs;
            _outputFormat = outputFormat;
            _ensureUnique = ensureUnique;
        }

        public bool Process(IAnimationBinGenericFormat mountFragment, IAnimationBinGenericFormat riderFragment)
        {
            if (mountFragment is not AnimationBinWh3 mount || mount.TableVersion != 4 ||
                riderFragment is not AnimationBinWh3 rider || rider.TableVersion != 4)
                throw new InvalidOperationException(Text("MountAnim.Wh3TablesRequired"));
            var project = _pfs.GetEditablePack() ?? throw new InvalidOperationException(Text("MountAnim.ProjectRequired"));
            ValidateName(_options.AnimPackName);
            ValidateName(_options.FragmentName);
            ValidateName(_options.SavePrefix, allowEmpty: true);
            _reservedPaths.Clear();

            var outputName = Path.GetFileNameWithoutExtension(_options.FragmentName);
            var carrierOutput = new AnimationBinWh3("animations/database/battle/bin/" + outputName + "_mount.bin", mount.ToByteArray())
            {
                Name = outputName + "_mount"
            };
            var output = new AnimationBinWh3("animations/database/battle/bin/" + outputName + ".bin", rider.ToByteArray())
            {
                Name = outputName,
                MountBin = carrierOutput.Name,
                SkeletonName = _generator.GetRiderSkeleton().SkeletonName,
                LocomotionGraph = rider.LocomotionGraph
            };
            var mountSkeleton = _generator.GetMountSkeleton();
            var riderSkeleton = _generator.GetRiderSkeleton();
            if (!string.Equals(mount.SkeletonName, mountSkeleton.SkeletonName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Text("Msg.FragmentSkeletonMismatch"));

            var results = new ErrorList();
            var writes = new List<PackFileWrite>();
            var generated = new Dictionary<(string Mount, string Rider, bool IsDeath), (string Path, float TimeScale)>();
            var riderLoader = new RiderAnimationService(_pfs, _lookup);
            var loadedRiders = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
            var auxiliary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string ConvertAuxiliary(string path)
            {
                if (auxiliary.TryGetValue(path, out var existing))
                    return existing;
                var outputPath = GetAnimationPath(path);
                var clip = riderLoader.Load(path, riderSkeleton);
                writes.Add(new PackFileWrite(outputPath, GetAnimationBytes(clip)));
                auxiliary.Add(path, outputPath);
                return outputPath;
            }
            var sourceSkeleton = string.Equals(rider.SkeletonName, riderSkeleton.SkeletonName, StringComparison.OrdinalIgnoreCase)
                ? riderSkeleton : new GameSkeleton(_lookup.GetSkeletonFileFromName(rider.SkeletonName), new AnimationPlayer());
            var metadata = new RiderMetadataService(_pfs, sourceSkeleton, riderSkeleton, ConvertAuxiliary, GetAnimationPath, writes, results);
            AnimationBinEntry.AnimationRef ConvertReference(string mountPath, AnimationBinEntry.AnimationRef riderRef, bool isDeath)
            {
                var key = (mountPath, riderRef.AnimationFile, isDeath);
                if (!generated.TryGetValue(key, out var conversion))
                {
                    var mountClip = LoadAnimation(mountPath, mountSkeleton);
                    if (!loadedRiders.TryGetValue(riderRef.AnimationFile, out var riderClip))
                    {
                        riderClip = riderLoader.Load(riderRef.AnimationFile, riderSkeleton);
                        loadedRiders.Add(riderRef.AnimationFile, riderClip);
                    }
                    var path = GetAnimationPath(riderRef.AnimationFile);
                    var clip = _generator.GenerateMountAnimation(mountClip, riderClip, synchronize: true, isDeath: isDeath);
                    writes.Add(new PackFileWrite(path, GetAnimationBytes(clip)));
                    conversion = (path, (float)(clip.Duration.TotalSeconds / riderClip.Duration.TotalSeconds));
                    generated.Add(key, conversion);
                }
                return new AnimationBinEntry.AnimationRef
                {
                    AnimationFile = conversion.Path,
                    AnimationMetaFile = metadata.Convert(riderRef.AnimationMetaFile, conversion.TimeScale),
                    AnimationSoundMetaFile = metadata.Convert(riderRef.AnimationSoundMetaFile, conversion.TimeScale)
                };
            }
            void PairSlot(AnimationBinEntry carrier, AnimationBinEntry riderSlot, uint carrierId)
            {
                var mountCount = carrier.AnimationRefs.Count;
                var riderCount = riderSlot.AnimationRefs.Count;
                if (mountCount == 0 || riderCount == 0 || (mountCount != riderCount && mountCount != 1 && riderCount != 1))
                    throw new InvalidOperationException(LocalizationManager.Instance.GetFormat("MountAnim.VariantMismatch", mountCount, riderCount));
                var carrierRefs = new List<AnimationBinEntry.AnimationRef>();
                var riderRefs = new List<AnimationBinEntry.AnimationRef>();
                for (var variant = 0; variant < Math.Max(mountCount, riderCount); variant++)
                {
                    var mountRef = carrier.AnimationRefs[mountCount == 1 ? 0 : variant];
                    var riderRef = riderSlot.AnimationRefs[riderCount == 1 ? 0 : variant];
                    var isDeath = AnimationSlotTypeHelperWh3.GetFromId((int)riderSlot.AnimationId).Value.StartsWith("RIDER_DEATH_", StringComparison.Ordinal);
                    riderRefs.Add(ConvertReference(mountRef.AnimationFile, riderRef, isDeath));
                    carrierRefs.Add(new AnimationBinEntry.AnimationRef
                    {
                        AnimationFile = mountRef.AnimationFile, AnimationMetaFile = mountRef.AnimationMetaFile,
                        AnimationSoundMetaFile = mountRef.AnimationSoundMetaFile
                    });
                }
                riderSlot.AnimationRefs = riderRefs;
                riderSlot.BlendIn = carrier.BlendIn;
                riderSlot.SelectionWeight = carrier.SelectionWeight;
                carrierOutput.AnimationTableEntries.RemoveAll(x => x.AnimationId == carrierId);
                carrierOutput.AnimationTableEntries.Add(new AnimationBinEntry
                {
                    AnimationId = carrierId, BlendIn = carrier.BlendIn, SelectionWeight = carrier.SelectionWeight,
                    WeaponBools = carrier.WeaponBools, Unk = carrier.Unk, AnimationRefs = carrierRefs
                });
            }
            var convertedSlots = new HashSet<uint>();
            foreach (var mountSlot in mount.AnimationTableEntries)
            {
                var mountName = AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)mountSlot.AnimationId)?.Value;
                var riderType = mountName == null ? null : AnimationSlotTypeHelperWh3.GetMatchingRiderAnimation(mountName);
                var movementFallback = riderType == null && mountName != null ? GetRiderMovementFallback(mountName) : null;
                riderType ??= movementFallback == null ? null : AnimationSlotTypeHelperWh3.GetfromValue(movementFallback);
                if (riderType == null ||
                    mountSlot.AnimationRefs.All(x => string.IsNullOrWhiteSpace(x.AnimationFile)))
                    continue;
                if (MountAnimationGeneratorService.IsMatchedCombatAnimation(riderType.Value))
                {
                    results.Warning(riderType.Value, Text("MountAnim.MatchedCombatNeedsPairing"));
                    continue;
                }

                var outputSlotId = movementFallback == null ? (uint)riderType.Id : mountSlot.AnimationId;
                var riderSlot = movementFallback == null
                    ? output.AnimationTableEntries.FirstOrDefault(x => x.AnimationId == outputSlotId) : null;
                var missing = riderSlot == null || riderSlot.AnimationRefs.Count == 0 ||
                    riderSlot.AnimationRefs.All(x => string.IsNullOrWhiteSpace(x.AnimationFile));
                if (missing)
                {
                    var equivalent = FindSameAction(rider, riderType.Value);
                    if (equivalent == null && !riderType.Value.StartsWith("RIDER_ATTACK_", StringComparison.Ordinal) &&
                        !riderType.Value.StartsWith("RIDER_DEATH_", StringComparison.Ordinal))
                    {
                        foreach (var other in mount.AnimationTableEntries.Where(x => x.AnimationRefs.Select(a => a.AnimationFile)
                            .SequenceEqual(mountSlot.AnimationRefs.Select(a => a.AnimationFile))))
                        {
                            var otherName = AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)other.AnimationId)?.Value;
                            var otherType = otherName == null ? null : AnimationSlotTypeHelperWh3.GetMatchingRiderAnimation(otherName);
                            equivalent = otherType == null ? null : rider.AnimationTableEntries.FirstOrDefault(x => x.AnimationId == otherType.Id);
                            if (equivalent?.AnimationRefs.Any(x => !string.IsNullOrWhiteSpace(x.AnimationFile)) == true)
                                break;
                            equivalent = null;
                        }
                    }
                    if (equivalent == null)
                    {
                        results.Error(riderType.Value, Text("MountAnim.MissingRiderSlot"));
                        continue;
                    }
                    output.AnimationTableEntries.RemoveAll(x => x.AnimationId == outputSlotId);
                    riderSlot = new AnimationBinEntry
                    {
                        AnimationId = outputSlotId, BlendIn = equivalent.BlendIn,
                        SelectionWeight = equivalent.SelectionWeight, WeaponBools = equivalent.WeaponBools,
                        Unk = equivalent.Unk, AnimationRefs = equivalent.AnimationRefs
                    };
                    output.AnimationTableEntries.Add(riderSlot);
                    results.Warning(riderType.Value, LocalizationManager.Instance.GetFormat("MountAnim.UsedSameAction",
                        AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)equivalent.AnimationId)!.Value));
                }
                try
                {
                    PairSlot(mountSlot, riderSlot, mountSlot.AnimationId);
                    convertedSlots.Add(riderSlot.AnimationId);
                }
                catch (Exception exception) { results.Error(riderType.Value, exception.Message); }
            }

            foreach (var slot in output.AnimationTableEntries.Where(x => !convertedSlots.Contains(x.AnimationId)))
            {
                var name = AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)slot.AnimationId)?.Value;
                if (name == null || !name.StartsWith("RIDER_", StringComparison.Ordinal) ||
                    MountAnimationGeneratorService.IsMatchedCombatAnimation(name) ||
                    slot.AnimationRefs.All(x => string.IsNullOrWhiteSpace(x.AnimationFile)))
                    continue;
                var carrier = FindCarrierAction(mount, name[6..]);
                var carrierType = AnimationSlotTypeHelperWh3.GetfromValue(name == "RIDER_SHIELD_UP" ? "STAND_SHIELD_UP" : name[6..]);
                if (carrier == null || carrierType == null)
                {
                    results.Error(name, Text("MountAnim.MissingRiderSlot"));
                    continue;
                }
                try
                {
                    PairSlot(carrier, slot, (uint)carrierType.Id);
                    convertedSlots.Add(slot.AnimationId);
                }
                catch (Exception exception) { results.Error(name, exception.Message); }
            }

            // Auxiliary slots also need the target rig; leaving source-rig clips breaks runtime transitions.
            if (!string.Equals(rider.SkeletonName, riderSkeleton.SkeletonName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var slot in output.AnimationTableEntries.Where(x => !convertedSlots.Contains(x.AnimationId)))
                foreach (var reference in slot.AnimationRefs)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(reference.AnimationFile))
                            reference.AnimationFile = ConvertAuxiliary(reference.AnimationFile);
                        reference.AnimationMetaFile = metadata.Convert(reference.AnimationMetaFile);
                        reference.AnimationSoundMetaFile = metadata.Convert(reference.AnimationSoundMetaFile);
                    }
                    catch (Exception exception) { results.Error(reference.AnimationFile, exception.Message); }
                }
            }

            if (results.Errors.Any(x => x.IsError))
            {
                _dialogs.ShowErrorViewDialog(Text("MountAnim.BatchFailed"), results, false);
                return false;
            }
            if (writes.Count == 0)
                throw new InvalidOperationException(Text("MountAnim.NoMatchedAnimations"));

            // Runtime fallbacks must use the same mounted action, including movement and death.
            foreach (var slot in output.AnimationTableEntries.Where(x => convertedSlots.Contains(x.AnimationId)).ToArray())
            {
                var name = AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)slot.AnimationId)!.Value;
                var baseType = name.StartsWith("RIDER_", StringComparison.Ordinal) ? AnimationSlotTypeHelperWh3.GetfromValue(name[6..]) : null;
                if (baseType == null)
                    continue;
                output.AnimationTableEntries.RemoveAll(x => x.AnimationId == baseType.Id);
                output.AnimationTableEntries.Add(new AnimationBinEntry
                {
                    AnimationId = (uint)baseType.Id, BlendIn = slot.BlendIn, SelectionWeight = slot.SelectionWeight,
                    WeaponBools = slot.WeaponBools, Unk = slot.Unk, AnimationRefs = slot.AnimationRefs
                });
            }

            var packPath = ReservePath("animations/database/battle/bin/" + Path.GetFileNameWithoutExtension(_options.AnimPackName) + ".animpack");
            var database = new AnimationPackFileDatabase(packPath);
            database.AddFile(output);
            database.AddFile(carrierOutput);
            writes.Add(new PackFileWrite(packPath, AnimationPackSerializer.ConvertToBytes(database)));
            if (!_ensureUnique && writes.Any(x => _pfs.FindFile(x.Path, project) != null) &&
                _dialogs.ShowYesNoBox(Text("MountAnim.OverwriteGenerated"), Text("DisplayName.MountTool")) != ShowMessageBoxResult.OK)
                return false;

            // Commit the complete output together; a failed generation never leaves a partial animation set.
            _pfs.ApplyFileWrites(project, writes, !_ensureUnique);
            results.Ok(output.Name, LocalizationManager.Instance.GetFormat("MountAnim.BatchSucceeded", writes.Count - 1));
            _dialogs.ShowErrorViewDialog(Text("MountAnim.BatchResult"), results, false);
            return true;
        }

        internal static AnimationBinEntry? FindSameAction(AnimationBinWh3 rider, string name)
        {
            var exactType = AnimationSlotTypeHelperWh3.GetfromValue(name);
            var exact = exactType == null ? null : rider.AnimationTableEntries.FirstOrDefault(x => x.AnimationId == exactType.Id &&
                x.AnimationRefs.Any(a => !string.IsNullOrWhiteSpace(a.AnimationFile)));
            if (exact != null)
                return exact;
            if (name.StartsWith("RIDER_WALK_IDLE_", StringComparison.Ordinal) || name.StartsWith("RIDER_RUN_IDLE_", StringComparison.Ordinal))
                name = name.Replace("_IDLE_", "_", StringComparison.Ordinal);
            else if (name.StartsWith("RIDER_CELEBRATE_", StringComparison.Ordinal))
                name = name[6..];
            var suffix = name.LastIndexOf('_');
            if (suffix < 0 || !int.TryParse(name[(suffix + 1)..], out _))
                return null;
            var prefix = name[..(suffix + 1)];
            return rider.AnimationTableEntries.FirstOrDefault(x =>
            {
                var candidate = AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)x.AnimationId)?.Value;
                return candidate != null && candidate.StartsWith(prefix, StringComparison.Ordinal) &&
                    int.TryParse(candidate[prefix.Length..], out _) &&
                    x.AnimationRefs.Any(a => !string.IsNullOrWhiteSpace(a.AnimationFile));
            });
        }

        private static AnimationBinEntry? FindCarrierAction(AnimationBinWh3 mount, string name)
        {
            var exact = FindSameAction(mount, name);
            if (exact != null)
                return exact;
            // These are carrier actions, not substitutes for the rider's attack or death.
            var fallback = name switch
            {
                _ when name.StartsWith("DEATH_", StringComparison.Ordinal) => "DEATH_STAND_1",
                _ when name.StartsWith("MOVING_ATTACK_", StringComparison.Ordinal) => "RUN_2",
                _ when name.StartsWith("CAST_SPELL_", StringComparison.Ordinal) => "STAND",
                _ when name.StartsWith("REFUSE_", StringComparison.Ordinal) => "DEFEND_1",
                "SHIELD_UP" => "STAND_SHIELD_UP",
                _ => null
            };
            return fallback == null ? null : FindSameAction(mount, fallback) ??
                (name == "SHIELD_UP" ? FindSameAction(mount, "COMBAT_IDLE_1") : null);
        }

        internal static string? GetRiderMovementFallback(string name)
        {
            if (name.StartsWith("COMBAT_READY_", StringComparison.Ordinal))
            {
                var action = name[13..];
                if (action.StartsWith("MOVE_", StringComparison.Ordinal) && int.TryParse(action[5..], out var speed))
                    action = speed <= 2 ? "WALK_" + speed : "RUN_" + (speed - 2);
                return "RIDER_" + (action == "STEP_BACK" ? "STEP_BACKWARD" : action);
            }
            if (name.StartsWith("STAND_NO_WEAPON", StringComparison.Ordinal))
            {
                var action = name[15..].TrimStart('_');
                return "RIDER_" + (action switch
                {
                    "" => "STAND",
                    "WALK" => "WALK_1",
                    "RUN" => "RUN_2",
                    _ when action.StartsWith("IDLE_", StringComparison.Ordinal) => "STAND_" + action,
                    _ => action
                });
            }
            if (name.StartsWith("STAND_SHIELD_UP", StringComparison.Ordinal))
                return "RIDER_STAND" + name[15..];
            if (name.Contains("_SHIELD_UP", StringComparison.Ordinal))
                return "RIDER_" + name.Replace("_SHIELD_UP", "", StringComparison.Ordinal);
            return null;
        }

        public string? SaveSingleAnim(AnimationClip mountAnim, AnimationClip riderAnim, string originalAnimationName, bool isDeath = false)
        {
            var path = GetAnimationPath(originalAnimationName);
            var saved = _saveService.Save(path, GenerateAnimationBytes(mountAnim, riderAnim, isDeath), !_ensureUnique);
            return saved == null ? null : _pfs.GetFullPath(saved);
        }

        internal byte[] GenerateAnimationBytes(AnimationClip mountAnim, AnimationClip riderAnim, bool isDeath = false)
        {
            var generated = _generator.GenerateMountAnimation(mountAnim, riderAnim, isDeath: isDeath);
            return GetAnimationBytes(generated);
        }

        private byte[] GetAnimationBytes(AnimationClip generated)
        {
            var file = generated.ConvertToFileFormat(_generator.GetRiderSkeleton());
            // WH3 stores both endpoints: N samples span N - 1 frame intervals.
            if (generated.DynamicFrames.Count > 1)
                file.Header.FrameRate = (float)((generated.DynamicFrames.Count - 1) / generated.Duration.TotalSeconds);
            if (_outputFormat != 7)
                file.ConvertToVersion(_outputFormat, _lookup.GetSkeletonFileFromName(file.Header.SkeletonName), _pfs);
            return AnimationFile.ConvertToBytes(file);
        }

        internal string GetAnimationPath(string originalPath)
        {
            ValidateName(_options.SavePrefix, allowEmpty: true);
            var normalized = originalPath.Replace('/', Path.DirectorySeparatorChar);
            return ReservePath(Path.Combine(Path.GetDirectoryName(normalized) ?? "", _options.SavePrefix + Path.GetFileName(normalized)));
        }

        private string ReservePath(string path)
        {
            path = path.Replace('/', '\\');
            var candidate = path;
            var index = 1;
            while (_reservedPaths.Contains(candidate) || (_ensureUnique && _pfs.FindFile(candidate) != null))
                candidate = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "_" + index++ + Path.GetExtension(path));
            _reservedPaths.Add(candidate);
            return candidate;
        }

        private AnimationClip LoadAnimation(string path, GameSkeleton skeleton)
        {
            var file = string.IsNullOrWhiteSpace(path) ? null : _pfs.FindFile(path);
            if (file == null)
                throw new InvalidOperationException(LocalizationManager.Instance.GetFormat("MountAnim.MissingAnimation", path));
            return new AnimationClip(AnimationFile.Create(file), skeleton);
        }

        private static void ValidateName(string name, bool allowEmpty = false)
        {
            if (allowEmpty && string.IsNullOrEmpty(name))
                return;
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
                throw new InvalidOperationException(Text("MountAnim.InvalidName"));
        }

        private static string Text(string key) => LocalizationManager.Instance.Get(key);
    }
}
