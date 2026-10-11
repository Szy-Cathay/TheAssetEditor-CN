using System.Text.Json;
using AnimationEditor.MountAnimationCreator;
using AnimationEditor.MountAnimationCreator.Services;
using AnimationEditor.MountAnimationCreator.ViewModels;
using Editors.AnimationVisualEditors.MountAnimationCreator.Services;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.GameFormats.RigidModel;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public class MountToolInstalledGameTests
{
    [DataTestMethod]
    [DataRow("cavalry-horse")]
    [DataRow("foot-horse")]
    [DataRow("foot-person")]
    [DataRow("tech-person")]
    public void InstalledWh3_BatchGeneratesAndReopensRealAnimations(string scenario)
    {
        var gameData = Environment.GetEnvironmentVariable("ASSETEDITOR_MOUNT_GAME_DATA");
        var outputRoot = Environment.GetEnvironmentVariable("ASSETEDITOR_MOUNT_OUTPUT");
        if (string.IsNullOrWhiteSpace(gameData) || string.IsNullOrWhiteSpace(outputRoot))
            Assert.Inconclusive("Set ASSETEDITOR_MOUNT_GAME_DATA and ASSETEDITOR_MOUNT_OUTPUT for installed-game verification.");
        outputRoot = Path.Combine(outputRoot!, scenario);
        new LocalizationManager().LoadLanguage();
        Directory.CreateDirectory(outputRoot!);
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
        settings.CurrentSettings.GameDirectories.Add(new() { Game = GameTypeEnum.Warhammer3, Path = gameData! });
        var ca = new PackFileContainerLoader(settings).LoadAllCaFiles(GameTypeEnum.Warhammer3)!;
        var pfs = new PackFileService(null) { EnforceGameFilesMustBeLoaded = false };
        pfs.AddContainer(ca);
        var tables = PackFileServiceUtility.GetAllAnimPacks(pfs)
            .SelectMany(file => AnimationPackSerializer.Load(file, pfs).Files.OfType<AnimationBinWh3>())
            .Where(x => x.TableVersion == 4).ToArray();
        var candidates = tables.Where(x => !string.IsNullOrEmpty(x.MountBin)).ToArray();
        File.WriteAllText(Path.Combine(outputRoot!, "tables.json"), JsonSerializer.Serialize(
            candidates.Select(x => new { x.Name, x.MountBin, x.SkeletonName }), new JsonSerializerOptions { WriteIndented = true }));
        var requested = Environment.GetEnvironmentVariable("ASSETEDITOR_MOUNT_RIDER_TABLE");
        var rider = string.IsNullOrWhiteSpace(requested)
            ? candidates.First(x => x.Name == "hu1_empire_hr1_warhorse_sword_and_shield")
            : tables.First(x => x.Name == requested);
        var mount = tables.First(x => x.Name == (scenario.EndsWith("person") ? "hu1_empire_sword_and_shield" : "hr1_warhorse"));
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup.Setup(x => x.GetSkeletonFileFromName(It.IsAny<string>())).Returns((string name) =>
            AnimationFile.Create(pfs.FindFile($"animations/skeletons/{name}.anim")));
        const string techPath = "variantmeshes/wh_variantmodels/hu1p/vmp/vmp_nagash/tech/vmp_nagash_01.anim";
        var tech = scenario == "tech-person" ? AnimationFile.Create(pfs.FindFile(techPath)) : null;
        var riderSkeleton = new GameSkeleton(lookup.Object.GetSkeletonFileFromName(tech?.Header.SkeletonName ?? rider.SkeletonName), new AnimationPlayer());
        var mountSkeleton = new GameSkeleton(lookup.Object.GetSkeletonFileFromName(mount.SkeletonName), new AnimationPlayer());
        var meshFile = ca.FileList.Where(x => x.Key.EndsWith(".rigid_model_v2") &&
                x.Key.Contains(scenario.EndsWith("person") ? "emp_karl_franz" : "horse", StringComparison.OrdinalIgnoreCase))
            .First(x => ModelFactory.Create().LoadOnlyHeaders(x.Value.DataSource.ReadData()).Header.SkeletonName == mount.SkeletonName);
        var model = ModelFactory.Create().Load(meshFile.Value.DataSource.ReadData());
        var graphics = new Mock<IGeometryGraphicsContextFactory>();
        graphics.Setup(x => x.Create()).Returns(Mock.Of<IGraphicsCardGeometry>());
        using var geometry = new MeshBuilderService(graphics.Object).BuildMeshFromRmvModel(model.ModelList[0][0], model.Header.SkeletonName);
        var mesh = new Rmv2MeshNode(geometry, model.ModelList[0][0].Material, null!, null!);
        var vertex = Enumerable.Range(0, geometry.VertexCount()).OrderBy(index =>
            Math.Abs(geometry.VertexArray[index].Position.X) + Math.Abs(geometry.VertexArray[index].Position.Z) +
            Math.Abs(geometry.VertexArray[index].Position.Y - 2)).First();
        var bone = riderSkeleton.GetBoneIndexByName("root");
        Assert.IsTrue(bone >= 0);
        var generator = new MountAnimationGeneratorService(new AnimationSettingsViewModel { IsRootNodeAnimation = true }, mesh, vertex, bone,
            new SceneObject("rider") { Skeleton = riderSkeleton }, new SceneObject("mount") { Skeleton = mountSkeleton });
        var projectRoot = Path.Combine(outputRoot!, "project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);
        LibGit2Sharp.Repository.Init(projectRoot);
        using var project = FolderProjectContainer.Create(projectRoot, new FolderProjectSettings { Name = "坐骑工具适配验证", GameVersion = GameTypeEnum.Warhammer3 });
        pfs.AddEditableFolderProject(project);
        var dialogs = new Mock<IStandardDialogs>();
        ErrorList? report = null;
        dialogs.Setup(x => x.ShowErrorViewDialog(It.IsAny<string>(), It.IsAny<ErrorList>(), It.IsAny<bool>()))
            .Callback((string _, ErrorList value, bool _) => report = value);
        var processor = new BatchProcessorService(pfs, lookup.Object, generator,
            new BatchProcessOptions { AnimPackName = "mount_verification", FragmentName = "mount_verification_rider", SavePrefix = "ae_mount_" + scenario.Replace('-', '_') + "_" },
            new FileSaveService(pfs, dialogs.Object), dialogs.Object, 7);
        var success = processor.Process(mount, rider);
        File.WriteAllText(Path.Combine(outputRoot!, "result.json"), JsonSerializer.Serialize(new
        {
            success, Rider = rider.Name, TechBinding = tech == null ? null : techPath, Mount = mount.Name, Mesh = meshFile.Key, Vertex = vertex, Project = projectRoot,
            Report = report?.Errors, Files = project.FileList.Keys
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(success, JsonSerializer.Serialize(report?.Errors));
        var outputPack = pfs.FindFile("animations/database/battle/bin/mount_verification.animpack", project)!;
        var outputTables = AnimationPackSerializer.Load(outputPack, pfs).Files.OfType<AnimationBinWh3>().ToArray();
        var output = outputTables.Single(x => x.Name == "mount_verification_rider");
        var outputCarrier = outputTables.Single(x => x.Name == output.MountBin);
        Assert.AreNotEqual(mount.Name, output.MountBin);
        Assert.AreEqual(riderSkeleton.SkeletonName, output.SkeletonName);
        foreach (var slot in output.AnimationTableEntries)
        {
            var name = AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)slot.AnimationId)?.Value;
            if (name?.StartsWith("RIDER_", StringComparison.Ordinal) != true || MountAnimationGeneratorService.IsMatchedCombatAnimation(name))
                continue;
            foreach (var reference in slot.AnimationRefs.Where(x => !string.IsNullOrWhiteSpace(x.AnimationFile)))
                Assert.IsNotNull(pfs.FindFile(reference.AnimationFile, project), name + " must use a generated mounted action");
            var baseType = AnimationSlotTypeHelperWh3.GetfromValue(name[6..]);
            if (baseType != null && slot.AnimationRefs.Any(x => !string.IsNullOrWhiteSpace(x.AnimationFile)))
                CollectionAssert.AreEqual(slot.AnimationRefs.Select(x => x.AnimationFile).ToArray(),
                    output.AnimationTableEntries.Single(x => x.AnimationId == baseType.Id).AnimationRefs.Select(x => x.AnimationFile).ToArray(), name);
        }
        if (scenario == "cavalry-horse")
        {
            Assert.AreEqual(rider.UnknownValue1, output.UnknownValue1);
            Assert.AreEqual(rider.LocomotionGraph, output.LocomotionGraph);
        }
        else
        {
            foreach (var source in mount.AnimationTableEntries.Where(x => x.AnimationRefs.Any(a => !string.IsNullOrWhiteSpace(a.AnimationFile))))
            {
                var name = AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)source.AnimationId)?.Value;
                var riderSlot = name == null ? null : AnimationSlotTypeHelperWh3.GetMatchingRiderAnimation(name);
                if (riderSlot != null && !MountAnimationGeneratorService.IsMatchedCombatAnimation(riderSlot.Value))
                    Assert.IsTrue(output.AnimationTableEntries.Any(x => x.AnimationId == riderSlot.Id && x.AnimationRefs.Count == source.AnimationRefs.Count), name);
            }
        }
        foreach (var entry in project.FileList.Where(x => x.Key.EndsWith(".anim")))
        {
            var animation = AnimationFile.Create(entry.Value);
            Assert.AreEqual(7u, animation.Header.Version);
            var clip = new AnimationClip(animation, riderSkeleton);
            Assert.IsTrue(clip.DynamicFrames.Count > 0);
            Assert.IsTrue(clip.DynamicFrames.All(frame => frame.Position.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z))));
        }
        Assert.IsTrue(project.FileList.Count > 10);
        var replaySkeleton = new GameSkeleton(lookup.Object.GetSkeletonFileFromName(output.SkeletonName)!, new AnimationPlayer());
        var resolver = new MeshAnimationHelper(mesh, Microsoft.Xna.Framework.Matrix.Identity);
        var verifiedPairs = 0;
        foreach (var outputSlot in output.AnimationTableEntries)
        {
            var riderSlotName = AnimationSlotTypeHelperWh3.GetFromId((int)outputSlot.AnimationId).Value;
            var riderType = riderSlotName.StartsWith("RIDER_", StringComparison.Ordinal)
                ? AnimationSlotTypeHelperWh3.GetfromValue(riderSlotName) : AnimationSlotTypeHelperWh3.GetMatchingRiderAnimation(riderSlotName);
            if ((riderType == null && BatchProcessorService.GetRiderMovementFallback(riderSlotName) == null) ||
                MountAnimationGeneratorService.IsMatchedCombatAnimation(riderType?.Value ?? riderSlotName))
                continue;
            var slotName = riderSlotName == "RIDER_SHIELD_UP" ? "STAND_SHIELD_UP" :
                riderSlotName.StartsWith("RIDER_", StringComparison.Ordinal) ? riderSlotName[6..] : riderSlotName;
            var carrierType = AnimationSlotTypeHelperWh3.GetfromValue(slotName);
            var mountSlot = outputCarrier.AnimationTableEntries.FirstOrDefault(x => x.AnimationId == carrierType?.Id);
            if (mountSlot == null || !outputSlot.AnimationRefs.Any(x => !string.IsNullOrWhiteSpace(x.AnimationFile)) ||
                outputSlot.AnimationRefs.Any(x => pfs.FindFile(x.AnimationFile, project) == null))
                continue;
            Assert.AreEqual(mountSlot.AnimationRefs.Count, outputSlot.AnimationRefs.Count, slotName);
            Assert.AreEqual(mountSlot.BlendIn, outputSlot.BlendIn, slotName);
            for (var variant = 0; variant < outputSlot.AnimationRefs.Count; variant++)
            {
                var mountFile = AnimationFile.Create(pfs.FindFile(mountSlot.AnimationRefs[variant].AnimationFile)!);
                var outputFile = AnimationFile.Create(pfs.FindFile(outputSlot.AnimationRefs[variant].AnimationFile, project)!);
                Assert.AreEqual(mountFile.Header.FrameRate, outputFile.Header.FrameRate, 0.0001f, slotName + " rider and carrier must have the same native sample rate");
                var mountClip = new AnimationClip(mountFile, mountSkeleton);
                var replay = new AnimationClip(outputFile, replaySkeleton);
                Assert.AreEqual(mountClip.Duration.TotalSeconds, replay.Duration.TotalSeconds, 0.00001, slotName);
                Assert.AreEqual(mountClip.DynamicFrames.Count, replay.DynamicFrames.Count, slotName);
                for (var frame = 0; frame < replay.DynamicFrames.Count; frame++)
                {
                    var fraction = replay.Timebase!.GetSampleTime(frame).TotalSeconds / mountClip.Duration.TotalSeconds;
                    var carrierFrame = AnimationSampler.Sample((float)Math.Min(fraction, 1), mountSkeleton, mountClip);
                    var expected = resolver.GetVertexTransformWorld(carrierFrame, vertex).Translation;
                    var pose = AnimationSampler.Sample(frame, 0, replaySkeleton, replay);
                    var rootBone = replaySkeleton.GetBoneIndexByName("root");
                    var actual = pose.GetSkeletonAnimatedWorld(replaySkeleton, rootBone).Translation;
                    if (slotName.StartsWith("DEATH_", StringComparison.Ordinal))
                    {
                        if (frame == 0)
                        {
                            Assert.AreEqual(expected.X, actual.X, 0.002f, slotName);
                            Assert.AreEqual(expected.Z, actual.Z, 0.002f, slotName);
                        }
                        var ground = pose.GetSkeletonAnimatedWorld(replaySkeleton, replaySkeleton.GetBoneIndexByName("animroot")).Translation.Y;
                        var contactBones = new[] { "root", "spine_1", "spine_2", "head", "hand_left", "hand_right", "foot_left", "foot_right", "toe_left_0", "toe_right_0" }
                            .Select(replaySkeleton.GetBoneIndexByName).Where(bone => bone >= 0).ToArray();
                        foreach (var child in contactBones)
                            Assert.IsTrue(pose.GetSkeletonAnimatedWorld(replaySkeleton, child).Translation.Y >= ground - 0.002f,
                                $"{slotName} frame {frame}: {replaySkeleton.BoneNames[child]} penetrates ground");
                    }
                    else
                        Assert.IsTrue(Microsoft.Xna.Framework.Vector3.Distance(expected, actual) < 0.002f, $"{slotName} frame {frame}: {expected} -> {actual}");
                }
                verifiedPairs++;
            }
        }
        Assert.IsTrue(verifiedPairs > 30);
        File.WriteAllText(Path.Combine(outputRoot!, "synchronization.json"), JsonSerializer.Serialize(new { scenario, Rider = output.Name, Carrier = outputCarrier.Name, VerifiedPairs = verifiedPairs }));
        PackFileServiceUtility.ExportSnapshot(Path.Combine(outputRoot!, "mount_verification.pack"),
            project.FileList.ToDictionary(x => x.Key, x => x.Value.DataSource.ReadData()),
            GameInformationDatabase.GetGameById(GameTypeEnum.Warhammer3), PackFileCAType.MOD);
    }
}
