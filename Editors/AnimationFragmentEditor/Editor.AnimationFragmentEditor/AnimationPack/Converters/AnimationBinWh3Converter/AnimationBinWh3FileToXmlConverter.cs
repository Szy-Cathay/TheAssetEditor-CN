using Editors.Shared.Core.Editors.TextEditor;
using GameWorld.Core.Services;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Ui.Editors.TextEditor;

namespace Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter
{
    public class AnimationBinWh3FileToXmlConverter : XmlToBinaryConverter<XmlFormat, AnimationBinWh3>
    {
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly MetaDataFileParser _metaDataTagDeSerializer;

        private string _animationPersistanceMetaFileName = "";
        private readonly Dictionary<string, uint> _animationsVersionFoundInPersistenceMeta = [];

        private readonly PackFile _animPackToValidate;
        private AnimationPackFileDatabase? _validationPack;

        public AnimationBinWh3FileToXmlConverter(ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper, MetaDataFileParser metaDataTagDeSerializer, PackFile animPackToValidate)
        {
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _metaDataTagDeSerializer = metaDataTagDeSerializer;
            _animPackToValidate = animPackToValidate;
        }

        protected override string CleanUpXml(string xmlText)
        {
            xmlText = xmlText.Replace("</BinEntry>", "</BinEntry>\n");
            xmlText = xmlText.Replace("<Bin>", "<Bin>\n");
            xmlText = xmlText.Replace("</GeneralBinData>", "</GeneralBinData>\n");
            return xmlText;
        }

        protected override XmlFormat ConvertBinaryToXml(byte[] bytes)
        {
            var binFile = new AnimationBinWh3("", bytes);
            var outputBin = new XmlFormat();

            var slotHelper = binFile.TableVersion == 4 ? AnimationSlotTypeHelperWh3.GetInstance() : AnimationSlotTypeHelper3k.GetInstance();
            outputBin.Version = binFile.TableVersion == 4 ? "Wh3" : "ThreeKingdom";

            outputBin.Data = new GeneralBinData()
            {
                TableVersion = binFile.TableVersion,
                TableSubVersion = binFile.TableSubVersion,
                Name = binFile.Name,
                MountBin = binFile.MountBin,
                UnmountBin = binFile.Unknown,
                SkeletonName = binFile.SkeletonName,
                LocomotionGraph = binFile.LocomotionGraph,
                UnknownValue1_RelatedToFlight = binFile.UnknownValue1
            };

            foreach (var animation in binFile.AnimationTableEntries)
            {
                var slotValue = slotHelper.TryGetFromId((int)animation.AnimationId);
                var slotString = Format("AnimPack.Validation.UnknownSlot", animation.AnimationId);
                if (slotValue != null)
                    slotString = slotValue.Value;

                outputBin.Animations.Add(new Animation()
                {
                    Slot = slotString,
                    SlotId = (int)animation.AnimationId,
                    ReservedWeaponFlags = animation.WeaponBools & ~63,
                    BlendId = animation.BlendIn,
                    BlendOut = animation.SelectionWeight,
                    Unk = animation.Unk,
                    WeaponBone = ValueConverterHelper.ConvertIntToBoolArray((int)animation.WeaponBools),
                });

                foreach (var animationRef in animation.AnimationRefs)
                {
                    outputBin.Animations.Last().Ref.Add(new Instance()
                    {
                        File = animationRef.AnimationFile,
                        Meta = animationRef.AnimationMetaFile,
                        Sound = animationRef.AnimationSoundMetaFile
                    });
                }
            }

            return outputBin;
        }

        protected override byte[] ConvertXmlToBinary(XmlFormat xmlBin, string fileName)
        {
            var binFile = new AnimationBinWh3("", null);

            binFile.TableVersion = xmlBin.Data.TableVersion;
            binFile.TableSubVersion = xmlBin.Data.TableSubVersion;
            binFile.Name = xmlBin.Data.Name;
            binFile.MountBin = xmlBin.Data.MountBin;
            binFile.SkeletonName = xmlBin.Data.SkeletonName;
            binFile.LocomotionGraph = xmlBin.Data.LocomotionGraph;
            binFile.UnknownValue1 = xmlBin.Data.UnknownValue1_RelatedToFlight;
            binFile.Unknown = xmlBin.Data.UnmountBin ?? string.Empty;

            var slotHelper = binFile.TableVersion == 4 ? AnimationSlotTypeHelperWh3.GetInstance() : AnimationSlotTypeHelper3k.GetInstance();

            foreach (var animationEntry in xmlBin.Animations)
            {
                binFile.AnimationTableEntries.Add(new AnimationBinEntry()
                {
                    AnimationId = (uint)(slotHelper.GetfromValue(animationEntry.Slot)?.Id ?? animationEntry.SlotId),
                    BlendIn = animationEntry.BlendId,
                    SelectionWeight = animationEntry.BlendOut,
                    WeaponBools = ValueConverterHelper.CreateWeaponFlagInt(animationEntry.WeaponBone) | (animationEntry.ReservedWeaponFlags & ~63),
                    Unk = animationEntry.Unk,
                });

                foreach (var animationInstance in animationEntry.Ref)
                {
                    binFile.AnimationTableEntries.Last().AnimationRefs.Add(new AnimationBinEntry.AnimationRef()
                    {
                        AnimationFile = animationInstance.File,
                        AnimationMetaFile = animationInstance.Meta,
                        AnimationSoundMetaFile = animationInstance.Sound
                    });
                }
            }

            return binFile.ToByteArray();
        }


        protected override ITextConverter.SaveError Validate(XmlFormat type, string s, IPackFileService pfs, string filepath)
        {
            var report = Check(type, pfs, filepath);
            return report.Errors.Any(e => e.IsError) ? new ITextConverter.SaveError { Text = string.Join(Environment.NewLine, report.Errors.Where(e => e.IsError).Select(e => e.Description)) } : null;
        }

        public ErrorList Check(XmlFormat type, IPackFileService pfs, string filepath)
        {
            var errorList = new ErrorList();
            ErrorList Fail(string key) { errorList.Error(Text("AnimPack.Validation.Format"), Text(key)); return errorList; }
            if (type.Data == null)
                return Fail("AnimPack.Validation.DataRequired");

            if (type.Animations == null)
                return Fail("AnimPack.Validation.AnimationsRequired");

            if (!(type.Data.TableVersion == 2 || type.Data.TableVersion == 4))
                return Fail("AnimPack.Validation.TableVersion");

            if (type.Data.TableVersion == 4 && type.Data.TableSubVersion != 3)
                return Fail("AnimPack.Validation.TableSubVersion");

            if (string.IsNullOrWhiteSpace(type.Data.SkeletonName))
                return Fail("AnimPack.Validation.SkeletonRequired");

            if (_skeletonAnimationLookUpHelper.GetSkeletonFileFromName(type.Data.SkeletonName) == null)
                errorList.Warning(Text("AnimPack.Table.Skeleton"), Format("AnimPack.Validation.ResourceUnavailable", type.Data.SkeletonName));

            if (type.Data.TableVersion == 4)
            {
                if (string.IsNullOrWhiteSpace(type.Data.LocomotionGraph))
                {
                    errorList.Warning(Text("AnimPack.Header.Graph"), Text("AnimPack.Validation.GraphEmpty"));
                }
                else
                {
                    if (pfs.FindFile(type.Data.LocomotionGraph) == null)
                        errorList.Warning(Text("AnimPack.Header.Graph"), Format("AnimPack.Validation.ResourceUnavailable", type.Data.LocomotionGraph));
                }
            }

            var slotHelper = type.Data.TableVersion == 4 ? AnimationSlotTypeHelperWh3.GetInstance() : AnimationSlotTypeHelper3k.GetInstance();

            if (string.IsNullOrWhiteSpace(type.Data.Name))
            {
                errorList.Error(Text("AnimPack.Table.Name"), Text("AnimPack.Validation.NameRequired"));
            }
            else
            {
                var filename = System.IO.Path.GetFileNameWithoutExtension(filepath).ToLowerInvariant();
                if (filename != type.Data.Name.ToLowerInvariant())
                    errorList.Error(Text("AnimPack.Table.Name"), Format("AnimPack.Validation.NameMismatch", type.Data.Name, filename));
            }

            _animationsVersionFoundInPersistenceMeta.Clear();
            _validationPack = null;
            foreach (var animation in type.Animations)
            {
                var slot = slotHelper.GetfromValue(animation.Slot);
                if (slot == null && animation.SlotId < 0)
                    errorList.Error(animation.Slot, Text("AnimPack.Validation.SlotInvalid"));
                if (!float.IsFinite(animation.BlendId) || animation.BlendId < 0 || !float.IsFinite(animation.BlendOut) || animation.BlendOut < 0)
                    errorList.Error(animation.Slot, Text("AnimPack.Validation.ParametersInvalid"));
                if (!ValueConverterHelper.ValidateBoolArray(animation.WeaponBone))
                    errorList.Error(animation.Slot, Text("AnimPack.Validation.WeaponFlagsInvalid"));

                if (animation.Ref == null || animation.Ref.Count == 0)
                {
                    continue;
                }

                foreach (var animationRef in animation.Ref)
                {
                    if (string.IsNullOrWhiteSpace(animationRef.File))
                    { errorList.Error(animation.Slot, Text("AnimPack.Validation.AnimationRequired")); continue; }
                    try
                    {
                    if (pfs.FindFile(animationRef.File) == null)
                        errorList.Warning(animation.Slot, Format("AnimPack.Validation.ResourceUnavailable", animationRef.File));
                    else if (!IsAnimFile(animationRef.File, pfs, errorList, animation.Slot))
                        errorList.Warning(animation.Slot, Format("AnimPack.Validation.ResourceFormatInvalid", animationRef.File));
                    else if (string.IsNullOrWhiteSpace(animationRef.File))
                        errorList.Warning(animation.Slot, Text("AnimPack.Validation.AnimationRequired"));
                    else
                        ValidateAnimationVersionAgainstPersistenceMeta(animationRef.File, animation.Slot, type.Data.SkeletonName, pfs, errorList);

                    if (!string.IsNullOrWhiteSpace(animationRef.Meta))
                    {
                    if (pfs.FindFile(animationRef.Meta) == null)
                        errorList.Warning(animation.Slot, Format("AnimPack.Validation.ResourceUnavailable", animationRef.Meta));
                    else if (!IsAnimMetaFile(animationRef.Meta, pfs, errorList, animation.Slot))
                        errorList.Warning(animation.Slot, Format("AnimPack.Validation.ResourceFormatInvalid", animationRef.Meta));
                    else
                    {
                        CheckForAnimationVersionsInMeta(animationRef.File, animationRef.Meta, animation.Slot, type.Data.SkeletonName, pfs, errorList);
                    }
                    }

                    var mountBin = type.Data.MountBin;
                    CheckForRiderAndHisMountAnimationsVersion(mountBin, _animPackToValidate, animation.Slot, animationRef.File, pfs, errorList);

                    if (!string.IsNullOrWhiteSpace(animationRef.Sound))
                    {
                    if (pfs.FindFile(animationRef.Sound) == null)
                        errorList.Warning(animation.Slot, Format("AnimPack.Validation.ResourceUnavailable", animationRef.Sound));
                    else if (!IsSndMetaFile(animationRef.Sound, pfs, errorList, animation.Slot))
                        errorList.Warning(animation.Slot, Format("AnimPack.Validation.ResourceFormatInvalid", animationRef.Sound));
                    }
                    }
                    catch (Exception e) { errorList.Warning(animation.Slot, Format("AnimPack.Validation.ResourceReadFailed", animationRef.File, e.Message)); }
                }
            }

            return errorList;
        }

        private bool IsAnimFile(string file, IPackFileService pfs, ErrorList errorList, string animationSlot)
        {
            var endsWithAnim = file.EndsWith(".anim");

            var theFile = pfs.FindFile(file);
            if (theFile == null)
            {
                errorList.Warning(animationSlot, Format("AnimPack.Validation.ResourceUnavailable", file));
                return false;
            }

            var data = theFile.DataSource.ReadData();
            var headerIsReallyAnimFile = data.Length >= 4 && BitConverter.ToUInt32(data) is 5 or 6 or 7 or 8;
            return endsWithAnim && headerIsReallyAnimFile;
        }

        private bool IsAnimMetaFile(string file, IPackFileService pfs, ErrorList errorList, string animationSlot)
        {
            var endsWithDotMeta = file.EndsWith(".anm.meta") || file.EndsWith(".meta");

            var theFile = pfs.FindFile(file);
            if (theFile == null)
            {
                errorList.Warning(animationSlot, Format("AnimPack.Validation.ResourceUnavailable", file));
                return false;
            }

            var data = theFile.DataSource.ReadData();
            var headerIsReallyAnimMetaFile = data.Length >= 4 && data[0] == 0x02;
            return endsWithDotMeta && headerIsReallyAnimMetaFile;
        }

        private bool IsSndMetaFile(string file, IPackFileService pfs, ErrorList errorList, string animationSlot)
        {
            var endsWithDotMeta = file.EndsWith(".snd.meta");

            var theFile = pfs.FindFile(file);
            if (theFile == null)
            {
                errorList.Warning(animationSlot, Format("AnimPack.Validation.ResourceUnavailable", file));
                return false;
            }

            var data = theFile.DataSource.ReadData();
            var headerIsReallyAnimMetaFile = data.Length >= 4 && data[0] == 0x02;
            return endsWithDotMeta && headerIsReallyAnimMetaFile;
        }

        private bool CheckForAnimationVersionsInMeta(string mainAnimationFile, string metaFile, string animationSlot, string skeleton, IPackFileService pfs, ErrorList errorList)
        {
            var result = true;

            var theFile = pfs.FindFile(metaFile);
            if (theFile == null)
            {
                errorList.Warning(animationSlot, Format("AnimPack.Validation.ResourceUnavailable", metaFile));
                return false;
            }
            var data = theFile.DataSource.ReadData();
            var parsed = _metaDataTagDeSerializer.ParseFile(data);

            var mainAnimationHeader = GetAnimationHeader(mainAnimationFile, pfs);
            if (mainAnimationHeader == null)
            {
                errorList.Warning(animationSlot, Format("AnimPack.Validation.ResourceUnavailable", mainAnimationFile));
                return false;
            }

            var mainAnimationVersion = mainAnimationHeader.Version;

            var metaItems = parsed.Attributes;

            foreach (var item in metaItems)
            {
                if (item.DisplayName.Contains("SPLICE") && item is Splice_v11 splice)
                {
                    var animPath = splice.Animation;
                    if (animPath == null || animPath == "")
                    {
                        errorList.Warning(animationSlot, Format("AnimPack.Validation.SpliceEmpty", metaFile));
                        result = false;
                        continue;
                    }

                    var parsedHeader = GetAnimationHeader(animPath, pfs);
                    if (parsedHeader == null)
                    {
                        errorList.Warning(animationSlot, Format("AnimPack.Validation.ResourceFormatInvalid", animPath));
                        result = false;
                        continue;
                    }

                    var animationVersion = parsedHeader.Version;
                    if (animationSlot == "PERSISTENT_METADATA_ALIVE") //check for this too, cus this plays throughtout char animation (all of them)
                    {
                        _animationsVersionFoundInPersistenceMeta[animPath] = animationVersion;
                        _animationPersistanceMetaFileName = metaFile;
                    }
                    else
                    {
                        if (mainAnimationVersion == 5 && animationVersion == 8) continue; //no idea why this isn't problem in vanilla wh3
                        if (mainAnimationVersion == 8 && animationVersion == 5) continue; //no idea why this isn't problem in vanilla wh3

                        var isTheVersionMatch = animationVersion == mainAnimationVersion;
                        var isMatchedCombat = animationSlot.StartsWith("COMBAT_");
                        var isMountedAnim = animationSlot.StartsWith("RIDER_") && skeleton.Contains("humanoid");

                        if (isMatchedCombat) continue;
                        if (!isMountedAnim) continue;
                        if (isTheVersionMatch) continue;

                        {
                            errorList.Warning(animationSlot, Format("AnimPack.Validation.SpliceVersion", metaFile, animPath, animationVersion, mainAnimationFile, mainAnimationVersion));
                            result = false;
                        }
                    }
                }
                else if (item.DisplayName.Contains("DISABLE_PER") && animationSlot.StartsWith("RIDER_"))
                {
                    errorList.Warning(animationSlot, Format("AnimPack.Validation.DisablePersistence", metaFile));
                }


            }

            return result;
        }

        private bool ValidateAnimationVersionAgainstPersistenceMeta(string mainAnimationFile, string animationSlot, string skeleton, IPackFileService pfs, ErrorList errorList)
        {
            var versions = _animationsVersionFoundInPersistenceMeta;
            var result = true;

            var mainAnimation = pfs.FindFile(mainAnimationFile);
            if (mainAnimation == null)
            {
                errorList.Warning(animationSlot, Format("AnimPack.Validation.ResourceUnavailable", mainAnimationFile));
                return false;
            }
            var mainAnimationParsed = AnimationFile.Create(mainAnimation);
            var mainAnimationVersion = mainAnimationParsed.Header.Version;


            foreach (var (animPath, animationVersion) in versions)
            {
                if (mainAnimationVersion == 5 && animationVersion == 8) continue; //no idea why this isn't problem in vanilla wh3
                if (mainAnimationVersion == 8 && animationVersion == 5) continue; //no idea why this isn't problem in vanilla wh3

                var isTheVersionMatch = animationVersion == mainAnimationVersion;
                var isMatchedCombat = animationSlot.StartsWith("COMBAT_");
                var isMountedAnim = animationSlot.StartsWith("RIDER_") && skeleton.Contains("humanoid");

                if (isMatchedCombat) continue;
                if (!isMountedAnim) continue;
                if (isTheVersionMatch) continue;
                {
                    errorList.Warning(animationSlot, Format("AnimPack.Validation.SpliceVersion", _animationPersistanceMetaFileName, animPath, animationVersion, mainAnimationFile, mainAnimationVersion));
                    result = false;
                }
            }

            return result;
        }

        private bool CheckForRiderAndHisMountAnimationsVersion(string mountBinReference, PackFile animpack, string animationSlot, string animationFile, IPackFileService pfs, ErrorList errorList)
        {
            if (!animationSlot.Contains("RIDER_") || string.IsNullOrWhiteSpace(mountBinReference)) return true;
            if (animpack == null) return false;

            var result = true;

            var animPack = _validationPack ??= AnimationPackSerializer.Load(animpack, pfs);
            var itemNames = animPack.Files.ToList();

            var findMountBinReference = itemNames.Find(x => string.Equals(System.IO.Path.GetFileNameWithoutExtension(x.FileName.Replace('\\', '/')), mountBinReference, StringComparison.OrdinalIgnoreCase));
            if (findMountBinReference == null)
            {
                errorList.Warning(animationSlot, Format("AnimPack.Validation.MountUnavailable", mountBinReference));
                return false;
            }

            var mountBinBytes = findMountBinReference.ToByteArray();

            var parsedBin = ConvertBinaryToXml(mountBinBytes);
            var animations = parsedBin.Animations;

            var riderAnimationSlotWithoutPrefix = animationSlot.Substring(6);
            var mainAnimationToCompareHeader = GetAnimationHeader(animationFile, pfs);
            if (mainAnimationToCompareHeader == null)
            {
                errorList.Warning(animationSlot, Format("AnimPack.Validation.MountUnavailable", mountBinReference));
                return false;
            }
            var mainAnimationToCompareVersion = mainAnimationToCompareHeader.Version;
            var mainAnimationToData = GetAnimationData(animationFile, pfs);
            if (mainAnimationToData == null)
            {
                errorList.Warning(animationSlot, Format("AnimPack.Validation.ResourceUnavailable", animationFile));
                return false;
            }

            var mainAnimationLength = mainAnimationToData.AnimationParts[0].DynamicFrames.Count;
            var mainAnimationTime = mainAnimationToCompareHeader.AnimationTotalPlayTimeInSec;


            foreach (var anim in animations)
            {
                if (anim.Slot != riderAnimationSlotWithoutPrefix) continue;

                var animationInstances = anim.Ref;
                foreach (var animationInstance in animationInstances)
                {
                    var header = GetAnimationHeader(animationInstance.File, pfs);

                    if (header == null)
                    {
                        errorList.Warning(animationSlot, Format("AnimPack.Validation.ResourceUnavailable", animationInstance.File));
                        continue;
                    }

                    var version = header.Version;
                    var isVersionMatch = version == mainAnimationToCompareVersion;
                    if (!isVersionMatch)
                    {
                        errorList.Warning(animationSlot, Format("AnimPack.Validation.RiderMismatch", Text("AnimPack.Validation.Version"), animationInstance.File, version, animationFile, mainAnimationToCompareVersion));
                        result = false;
                    }

                    var timing = header.AnimationTotalPlayTimeInSec;
                    var data = GetAnimationData(animationInstance.File, pfs);
                    if (data == null)
                    {
                        errorList.Warning(animationSlot, Format("AnimPack.Validation.ResourceUnavailable", animationInstance.File));
                        return false;
                    }
                    else
                    {
                        var length = data.AnimationParts[0].DynamicFrames.Count;
                        var isTImingMatch = mainAnimationTime == timing;
                        if (!isTImingMatch)
                        {
                            errorList.Warning(animationSlot, Format("AnimPack.Validation.RiderMismatch", Text("AnimPack.Validation.Duration"), animationInstance.File, timing, animationFile, mainAnimationTime));
                            result = false;
                        }

                        var isLenMatch = mainAnimationLength == length;
                        if (!isLenMatch)
                        {
                            errorList.Warning(animationSlot, Format("AnimPack.Validation.RiderMismatch", Text("AnimPack.Validation.Frames"), animationInstance.File, length, animationFile, mainAnimationLength));
                            result = false;
                        }
                    }
                }
            }

            return result;
        }


        private AnimationFile.AnimationHeader? GetAnimationHeader(string path, IPackFileService pfs)
        {
            var mainAnimation = pfs.FindFile(path);
            if (mainAnimation == null) return null;
            var mainAnimationParsed = AnimationFile.Create(mainAnimation);
            return mainAnimationParsed.Header;
        }

        private AnimationFile? GetAnimationData(string path, IPackFileService pfs)
        {
            var mainAnimation = pfs.FindFile(path);
            if (mainAnimation == null) return null;
            var mainAnimationParsed = AnimationFile.Create(mainAnimation);
            return mainAnimationParsed;
        }

        private static string Text(string key) => LocalizationManager.Instance?.Get(key) ?? key;
        private static string Format(string key, params object[] values) => LocalizationManager.Instance?.GetFormat(key, values) ?? key;



    }
}
