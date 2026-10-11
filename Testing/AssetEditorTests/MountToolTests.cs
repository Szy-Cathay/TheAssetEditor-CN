using AnimationEditor.MountAnimationCreator;
using AnimationEditor.MountAnimationCreator.Services;
using AnimationEditor.MountAnimationCreator.ViewModels;
using Editors.AnimationVisualEditors.MountAnimationCreator.Services;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Animation;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public class MountToolTests
{
    [ClassInitialize]
    public static void Initialize(TestContext _) => new LocalizationManager().LoadLanguage();

    [TestMethod]
    public void EmptyScene_PlayerTimeIsAvailableBeforeLoadingAModel()
    {
        var player = new AnimationPlayer();
        player.Update(new GameTime());
        Assert.AreEqual(TimeSpan.Zero, player.CurrentTime);
        Assert.AreEqual(0, player.FrameCount);
    }

    [TestMethod]
    public void Registration_EnablesToolWithCurrentHostContract()
    {
        var database = new EditorDatabase(null!, null!);
        new Editors.AnimationVisualEditors.DependencyInjectionContainer().RegisterTools(database);
        var entry = database.GetEditorInfos().Single(x => x.EditorEnum == EditorEnums.MountTool_Editor);
        Assert.IsTrue(entry.IsToolbarButtonEnabled);
        Assert.IsTrue(typeof(EditorHostBase).IsAssignableFrom(entry.ViewModel));
    }

    [TestMethod]
    public void Generate_UnfittedAnimation_SamplesMountByTimeAndAttachesEveryFrame()
    {
        var skeleton = Skeleton("test", ("animroot", -1), ("root", 0), ("weapon", 0));
        var mount = Clip(skeleton, 2, 2);
        mount.DynamicFrames[1].Position[0] = new Vector3(10, 0, 0);
        var rider = Clip(skeleton, 4, 2);
        var settings = new AnimationSettingsViewModel { FitAnimation = false, IsRootNodeAnimation = true };
        var generator = Generator(skeleton, settings);

        var result = generator.GenerateMountAnimation(mount, rider);

        var world = AnimationSampler.Sample(3, 0, skeleton, result).GetSkeletonAnimatedWorld(skeleton, 1);
        Assert.AreEqual(10f, world.Translation.X, 0.0001f);
        Assert.AreEqual(2f, world.Translation.Y, 0.0001f);
        Assert.AreEqual(Vector3.Zero, rider.DynamicFrames[3].Position[1]);
    }

    [TestMethod]
    public void Generate_FittingPreservesBothEndpointsAndNativeFrameIntervals()
    {
        var skeleton = Skeleton("test", ("animroot", -1), ("root", 0), ("arm", 1));
        var mount = Clip(skeleton, 21, 1);
        for (var index = 0; index < mount.DynamicFrames.Count; index++)
            mount.DynamicFrames[index].Position[0] = new Vector3(index * 0.1f, 0, 0);
        var rider = Clip(skeleton, 11, 0.5);
        rider.DynamicFrames[^1].Rotation[2] = Quaternion.CreateFromAxisAngle(Vector3.Right, 1);
        var generator = Generator(skeleton, new AnimationSettingsViewModel { IsRootNodeAnimation = true });
        var result = generator.GenerateMountAnimation(mount, rider);
        Assert.AreEqual(1f, Math.Abs(Quaternion.Dot(rider.DynamicFrames[^1].Rotation[2], result.DynamicFrames[^1].Rotation[2])), 0.0001f);
        var service = new BatchProcessorService(Mock.Of<IPackFileService>(), Mock.Of<ISkeletonAnimationLookUpHelper>(), generator,
            new BatchProcessOptions(), Mock.Of<IFileSaveService>(), Mock.Of<IStandardDialogs>(), 7);
        var file = AnimationFile.Create(PackFile.CreateFromBytes("mounted.anim", service.GenerateAnimationBytes(mount, rider)));
        Assert.AreEqual(20f, file.Header.FrameRate, 0.0001f);
        Assert.AreEqual(1f, file.Header.AnimationTotalPlayTimeInSec, 0.0001f);
        for (var index = 0; index < result.DynamicFrames.Count; index++)
            Assert.AreEqual(mount.DynamicFrames[index].Position[0], result.DynamicFrames[index].Position[0]);
    }

    [TestMethod]
    public void Generate_LoopingSharesTheEndSample()
    {
        var skeleton = Skeleton("test", ("animroot", -1), ("root", 0), ("arm", 1));
        var rider = Clip(skeleton, 3, 1);
        rider.DynamicFrames[1].Rotation[2] = Quaternion.CreateFromAxisAngle(Vector3.Right, 1);
        var settings = new AnimationSettingsViewModel { FitAnimation = false, IsRootNodeAnimation = true };
        settings.LoopCounter.Value = 2;
        var result = Generator(skeleton, settings).GenerateMountAnimation(Clip(skeleton, 1, 2), rider);
        Assert.AreEqual(5, result.DynamicFrames.Count);
        Assert.AreEqual(TimeSpan.FromSeconds(2), result.Duration);
        Assert.AreEqual(rider.DynamicFrames[1].Rotation[2], result.DynamicFrames[3].Rotation[2]);
        Assert.AreEqual(rider.DynamicFrames[^1].Rotation[2], result.DynamicFrames[^1].Rotation[2]);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Batch_PreservesVariantsOrWritesNothingWhenASourceIsMissing(bool missingSource)
    {
        var skeleton = Skeleton("test", ("animroot", -1), ("root", 0));
        var pfs = new Mock<IPackFileService>();
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup.Setup(x => x.GetSkeletonFileFromName("test")).Returns(Clip(skeleton, 1, 1).ConvertToFileFormat(skeleton));
        var animation = PackFile.CreateFromBytes("source.anim", AnimationFile.ConvertToBytes(Clip(skeleton, 2, 1).ConvertToFileFormat(skeleton)));
        pfs.Setup(x => x.FindFile(It.Is<string>(s => s.StartsWith("source")), It.IsAny<PackFileContainer?>())).Returns(animation);
        if (missingSource)
            pfs.Setup(x => x.FindFile("source_rider_b.anim", It.IsAny<PackFileContainer?>())).Returns((PackFile?)null);
        var saved = new Dictionary<string, byte[]>();
        var save = new Mock<IFileSaveService>();
        save.Setup(x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()))
            .Returns((string path, byte[] bytes, bool _) => { saved[path] = bytes; return PackFile.CreateFromBytes(path, bytes); });
        pfs.Setup(x => x.ApplyFileWrites(It.IsAny<PackFileContainer>(), It.IsAny<IReadOnlyCollection<PackFileWrite>>(), It.IsAny<bool>()))
            .Returns((PackFileContainer _, IReadOnlyCollection<PackFileWrite> writes, bool _) =>
            {
                foreach (var write in writes)
                    saved[write.Path] = write.Content;
                return writes.Select(x => PackFile.CreateFromBytes(x.Path, x.Content)).ToArray();
            });
        pfs.Setup(x => x.GetEditablePack()).Returns(new PackFileContainer("output"));
        var mount = new AnimationBinWh3("mount.bin") { Name = "mount", SkeletonName = "test" };
        mount.AnimationTableEntries.Add(Entry("WALK_1", "source_mount_a.anim", "source_mount_b.anim"));
        mount.AnimationTableEntries.Add(Entry("COMBAT_1", "missing_matched_mount.anim"));
        mount.AnimationTableEntries.Add(Entry("COMBAT_IDLE_1", "source_combat_mount.anim"));
        mount.AnimationTableEntries.Add(Entry("COMBAT_READY_MOVE_1", "source_combat_walk.anim"));
        mount.AnimationTableEntries.Add(Entry("STAND_NO_WEAPON_WALK", "source_mount_a.anim", "source_mount_b.anim"));
        var rider = new AnimationBinWh3("rider.bin")
        {
            Name = "rider", SkeletonName = "test", Unknown = "dismount", UnknownValue1 = 257,
            LocomotionGraph = "animations/locomotion_graphs/custom.xml"
        };
        rider.AnimationTableEntries.Add(Entry("RIDER_WALK_1", "source_rider_a.anim", "source_rider_b.anim"));
        rider.AnimationTableEntries.Add(Entry("PERSISTENT_METADATA_ALIVE", ""));
        rider.AnimationTableEntries.Add(Entry("RIDER_COMBAT_1", "original_matched_rider.anim"));
        rider.AnimationTableEntries.Add(Entry("RIDER_COMBAT_IDLE_1", "source_combat_rider.anim"));
        rider.AnimationTableEntries.Add(Entry("COMBAT_READY_MOVE_1", "source_unmounted_walk.anim"));
        rider.AnimationTableEntries.Add(Entry("STAND_NO_WEAPON_WALK", "source_unmounted_walk.anim"));
        rider.AnimationTableEntries[0].Unk = true;
        var original = rider.ToByteArray();
        var service = new BatchProcessorService(pfs.Object, lookup.Object,
            Generator(skeleton, new AnimationSettingsViewModel { IsRootNodeAnimation = true }),
            new BatchProcessOptions { AnimPackName = "output", FragmentName = "custom_rider", SavePrefix = "new_" },
            save.Object, Mock.Of<IStandardDialogs>(), 7);

        var success = service.Process(mount, rider);
        if (missingSource)
        {
            Assert.IsFalse(success);
            Assert.AreEqual(0, saved.Count);
            CollectionAssert.AreEqual(original, rider.ToByteArray());
            return;
        }
        Assert.IsTrue(success);

        var packBytes = saved.Single(x => x.Key.EndsWith(".animpack")).Value;
        var pack = AnimationPackSerializer.Load(PackFile.CreateFromBytes("output.animpack", packBytes), pfs.Object);
        var result = pack.Files.OfType<AnimationBinWh3>().Single(x => x.Name == "custom_rider");
        Assert.AreEqual("custom_rider", result.Name);
        Assert.AreEqual(rider.LocomotionGraph, result.LocomotionGraph);
        Assert.AreEqual(rider.UnknownValue1, result.UnknownValue1);
        Assert.AreEqual(rider.Unknown, result.Unknown);
        Assert.AreEqual("custom_rider_mount", result.MountBin);
        Assert.IsTrue(pack.Files.OfType<AnimationBinWh3>().Any(x => x.Name == result.MountBin));
        Assert.AreEqual(2, result.AnimationTableEntries[0].AnimationRefs.Count);
        Assert.IsTrue(result.AnimationTableEntries[0].Unk);
        Assert.AreNotEqual(result.AnimationTableEntries[0].AnimationRefs[0].AnimationFile,
            result.AnimationTableEntries[0].AnimationRefs[1].AnimationFile);
        Assert.AreEqual("meta_1", result.AnimationTableEntries[0].AnimationRefs[1].AnimationMetaFile);
        Assert.AreEqual("original_matched_rider.anim", result.AnimationTableEntries[2].AnimationRefs[0].AnimationFile);
        Assert.AreEqual("new_source_combat_rider.anim", result.AnimationTableEntries[3].AnimationRefs[0].AnimationFile);
        foreach (var name in new[] { "COMBAT_READY_MOVE_1", "STAND_NO_WEAPON_WALK" })
        {
            var slot = result.AnimationTableEntries.Single(x => x.AnimationId == AnimationSlotTypeHelperWh3.GetfromValue(name).Id);
            foreach (var reference in slot.AnimationRefs)
            {
                Assert.IsTrue(saved.TryGetValue(reference.AnimationFile, out var mountedBytes), name + " must not retain a foot animation");
                var replay = new AnimationClip(AnimationFile.Create(PackFile.CreateFromBytes(reference.AnimationFile, mountedBytes!)), skeleton);
                foreach (var frame in Enumerable.Range(0, replay.DynamicFrames.Count))
                    Assert.AreEqual(2f, AnimationSampler.Sample(frame, 0, skeleton, replay).GetSkeletonAnimatedWorld(skeleton, 1).Translation.Y, 0.0001f, name);
            }
        }
        CollectionAssert.AreEqual(original, rider.ToByteArray());
    }

    [TestMethod]
    public void Generate_UnfittedSingleFrameCarrierDoesNotDiscardRiderMotion()
    {
        var skeleton = Skeleton("test", ("animroot", -1), ("root", 0), ("arm", 1));
        var mount = Clip(skeleton, 1, 1);
        var rider = Clip(skeleton, 5, 2);
        rider.DynamicFrames[2].Rotation[2] = Quaternion.CreateFromAxisAngle(Vector3.Right, 1);
        var result = Generator(skeleton, new AnimationSettingsViewModel { IsRootNodeAnimation = true, FitAnimation = false })
            .GenerateMountAnimation(mount, rider);
        Assert.AreEqual(5, result.DynamicFrames.Count);
        Assert.AreEqual(rider.Duration, result.Duration);
        Assert.AreEqual(rider.DynamicFrames[2].Rotation[2], result.DynamicFrames[2].Rotation[2]);
        foreach (var frame in Enumerable.Range(0, result.DynamicFrames.Count))
            Assert.AreEqual(new Vector3(0, 2, 0), AnimationSampler.Sample(frame, 0, skeleton, result).GetSkeletonAnimatedWorld(skeleton, 1).Translation);
    }

    [TestMethod]
    public void Generate_RotatedAndScaledMount_KeepsAttachmentAndWeaponInParentSpace()
    {
        var skeleton = Skeleton("test", ("animroot", -1), ("root", 0), ("weapon", 0));
        var mount = Clip(skeleton, 2, 1);
        mount.DynamicFrames[1].Position[0] = new Vector3(10, 0, 0);
        mount.DynamicFrames[1].Rotation[0] = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2);
        var rider = Clip(skeleton, 2, 1);
        rider.DynamicFrames[1].Position[2] = new Vector3(3, 0, 0);
        var settings = new AnimationSettingsViewModel { IsRootNodeAnimation = true, KeepRiderRotation = false };
        settings.Scale.Value = 2;
        var result = Generator(skeleton, settings).GenerateMountAnimation(mount, rider);
        var frame = AnimationSampler.Sample(1, 0, skeleton, result);
        Assert.IsTrue(Vector3.Distance(new Vector3(20, 4, 0), frame.GetSkeletonAnimatedWorld(skeleton, 1).Translation) < 0.0001f);
        Assert.IsTrue(Vector3.Distance(new Vector3(20, 4, -3), frame.GetSkeletonAnimatedWorld(skeleton, 2).Translation) < 0.0001f);
        Assert.AreEqual(new Vector3(20, 0, 0), result.DynamicFrames[1].Position[0]);
        Assert.AreEqual(new Vector3(3, 0, 0), rider.DynamicFrames[1].Position[2]);
    }

    [TestMethod]
    public void Generate_DeathKeepsRiderFallWithoutApplyingCarrierFallTwice()
    {
        var skeleton = Skeleton("test", ("animroot", -1), ("root", 0), ("weapon", 0));
        var mount = Clip(skeleton, 2, 1);
        var rider = Clip(skeleton, 2, 1);
        var fall = Quaternion.CreateFromAxisAngle(Vector3.Forward, MathHelper.PiOver2);
        mount.DynamicFrames[1].Rotation[0] = fall;
        rider.DynamicFrames[1].Rotation[1] = fall;
        rider.DynamicFrames[1].Rotation[2] = fall;
        var result = Generator(skeleton, new AnimationSettingsViewModel { IsRootNodeAnimation = true })
            .GenerateMountAnimation(mount, rider);
        var frame = AnimationSampler.Sample(1, 0, skeleton, result);
        foreach (var bone in new[] { 1, 2 })
        {
            frame.GetSkeletonAnimatedWorld(skeleton, bone).Decompose(out _, out var actual, out _);
            Assert.AreEqual(1f, Math.Abs(Quaternion.Dot(fall, actual)), 0.0001f);
        }
    }

    [TestMethod]
    public void Generate_DeathGroundContactKeepsFallAndRootMotion()
    {
        var skeleton = Skeleton("test", ("animroot", -1), ("root", 0), ("hand", 1), ("weapon", 0));
        var mount = Clip(skeleton, 3, 1);
        var rider = Clip(skeleton, 3, 1);
        for (var frame = 0; frame < 3; frame++)
        {
            mount.DynamicFrames[frame].Position[0] = new Vector3(frame, 0, 0);
            mount.DynamicFrames[frame].Rotation[0] = Quaternion.CreateFromAxisAngle(Vector3.Forward, frame * MathHelper.PiOver4);
            rider.DynamicFrames[frame].Rotation[1] = mount.DynamicFrames[frame].Rotation[0];
            rider.DynamicFrames[frame].Position[2] = new Vector3(0.5f, 0, 0);
            rider.DynamicFrames[frame].Position[3] = new Vector3(0.5f, 0, 0);
        }
        var generator = Generator(skeleton, new AnimationSettingsViewModel());
        var ungrounded = generator.GenerateMountAnimation(mount, rider, synchronize: true);
        var result = generator.GenerateMountAnimation(mount, rider, synchronize: true, isDeath: true);
        for (var index = 0; index < result.DynamicFrames.Count; index++)
        {
            var frame = AnimationSampler.Sample(index, 0, skeleton, result);
            var source = AnimationSampler.Sample(index, 0, skeleton, ungrounded);
            Assert.AreEqual(mount.DynamicFrames[index].Position[0], result.DynamicFrames[index].Position[0]);
            foreach (var bone in new[] { 1, 2 })
            {
                var world = frame.GetSkeletonAnimatedWorld(skeleton, bone);
                Assert.IsTrue(world.Translation.Y >= -0.0001f, "Falling carrier must not pull rider through the ground");
                world.Decompose(out _, out var rotation, out _);
                source.GetSkeletonAnimatedWorld(skeleton, bone).Decompose(out _, out var sourceRotation, out _);
                Assert.AreEqual(1f, Math.Abs(Quaternion.Dot(sourceRotation, rotation)), 0.0001f);
            }
            var before = source.GetSkeletonAnimatedWorld(skeleton, 3).Translation - source.GetSkeletonAnimatedWorld(skeleton, 1).Translation;
            var after = frame.GetSkeletonAnimatedWorld(skeleton, 3).Translation - frame.GetSkeletonAnimatedWorld(skeleton, 1).Translation;
            Assert.IsTrue(Vector3.Distance(before, after) < 0.0001f);
        }
        Assert.AreEqual(Quaternion.CreateFromAxisAngle(Vector3.Forward, MathHelper.PiOver4), rider.DynamicFrames[1].Rotation[1]);
    }

    [TestMethod]
    public void Generate_DeathPreservesDepartureFromTheSeat()
    {
        var skeleton = Skeleton("test", ("animroot", -1), ("root", 0));
        var mount = Clip(skeleton, 3, 1);
        var rider = Clip(skeleton, 3, 1);
        for (var index = 0; index < 3; index++)
        {
            mount.DynamicFrames[index].Position[0] = new Vector3(index, 0, 0);
            rider.DynamicFrames[index].Position[0] = new Vector3(-index, 0, 3);
            rider.DynamicFrames[index].Position[1] = new Vector3(0, 2 - index * 0.75f, 0);
        }
        var generator = Generator(skeleton, new AnimationSettingsViewModel());
        var attached = generator.GenerateMountAnimation(mount, rider, synchronize: true);
        var death = generator.GenerateMountAnimation(mount, rider, synchronize: true, isDeath: true);
        for (var index = 0; index < 3; index++)
        {
            var seat = AnimationSampler.Sample(index, 0, skeleton, attached).GetSkeletonAnimatedWorld(skeleton, 1).Translation;
            var body = AnimationSampler.Sample(index, 0, skeleton, death).GetSkeletonAnimatedWorld(skeleton, 1).Translation;
            Assert.AreEqual(seat.X - index, body.X, 0.0001f, "Death must retain the rider's authored departure instead of pinning it to the corpse");
            Assert.AreEqual(seat.Z, body.Z, 0.0001f);
            Assert.AreEqual(mount.DynamicFrames[index].Position[0], death.DynamicFrames[index].Position[0]);
        }
    }

    [DataTestMethod]
    [DataRow("spine_tentacle_middle_right_6")]
    [DataRow("head_strap_left_1_5")]
    [DataRow("skirt_back_3")]
    public void Generate_DeathSecondaryBonesDoNotHoldTheBodyAboveGround(string secondaryBone)
    {
        var skeleton = Skeleton("tech", ("animroot", -1), ("root", 0), ("hand_right", 1), (secondaryBone, 1));
        var mount = Clip(skeleton, 3, 1);
        var rider = Clip(skeleton, 3, 1);
        rider.DynamicFrames[0].Position[1] = new Vector3(0, 2, 0);
        rider.DynamicFrames[1].Position[1] = new Vector3(0, 1, 0);
        rider.DynamicFrames[2].Position[1] = Vector3.Zero;
        foreach (var frame in rider.DynamicFrames)
        {
            frame.Position[2] = new Vector3(0, -0.2f, 0);
            frame.Position[3] = new Vector3(0, -1.5f, 0);
        }
        var result = Generator(skeleton, new AnimationSettingsViewModel()).GenerateMountAnimation(mount, rider, synchronize: true, isDeath: true);
        var finalPose = AnimationSampler.Sample(2, 0, skeleton, result);
        Assert.AreEqual(0f, finalPose.GetSkeletonAnimatedWorld(skeleton, 2).Translation.Y, 0.0001f,
            "Secondary cloth and tentacles must not leave the rider's body floating after death");
        Assert.AreEqual(rider.DynamicFrames[2].Rotation[1], result.DynamicFrames[2].Rotation[1]);
    }

    [TestMethod]
    public void Generate_TurnUsesCarrierHeadingAndKeepsTheRidersLean()
    {
        var skeleton = Skeleton("test", ("animroot", -1), ("root", 0));
        var mount = Clip(skeleton, 2, 1);
        var rider = Clip(skeleton, 2, 1);
        mount.DynamicFrames[1].Rotation[0] = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2);
        rider.DynamicFrames[1].Rotation[0] = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.Pi);
        rider.DynamicFrames[1].Rotation[1] = Quaternion.CreateFromAxisAngle(Vector3.Forward, 0.2f);
        var result = Generator(skeleton, new AnimationSettingsViewModel { IsRootNodeAnimation = true })
            .GenerateMountAnimation(mount, rider);
        var actual = AnimationSampler.Sample(1, 0, skeleton, result).GetSkeletonAnimatedWorld(skeleton, 1);
        var expected = Matrix.CreateFromQuaternion(rider.DynamicFrames[1].Rotation[1]) * Matrix.CreateFromQuaternion(mount.DynamicFrames[1].Rotation[0]);
        actual.Decompose(out _, out var rotation, out _);
        expected.Decompose(out _, out var expectedRotation, out _);
        Assert.AreEqual(1f, Math.Abs(Quaternion.Dot(rotation, expectedRotation)), 0.0001f);
    }

    [DataTestMethod]
    [DataRow("RIDER_ATTACK_1")]
    [DataRow("RIDER_DEATH_STAND_1")]
    public void Batch_MissingActionCannotUseStand(string missing)
    {
        var skeleton = Skeleton("person", ("animroot", -1), ("root", 0));
        var pfs = new Mock<IPackFileService>();
        pfs.Setup(x => x.GetEditablePack()).Returns(new PackFileContainer("output"));
        var mount = new AnimationBinWh3("mount.bin") { Name = "mount", SkeletonName = "person" };
        mount.AnimationTableEntries.Add(Entry(missing[6..], "mount.anim"));
        var rider = new AnimationBinWh3("rider.bin") { Name = "rider", SkeletonName = "person" };
        rider.AnimationTableEntries.Add(Entry("RIDER_STAND", "stand.anim"));
        var service = new BatchProcessorService(pfs.Object, Mock.Of<ISkeletonAnimationLookUpHelper>(),
            Generator(skeleton, new AnimationSettingsViewModel()),
            new BatchProcessOptions { AnimPackName = "output", FragmentName = "rider", SavePrefix = "new_" },
            Mock.Of<IFileSaveService>(), Mock.Of<IStandardDialogs>(), 7);

        Assert.IsNull(BatchProcessorService.FindSameAction(rider, missing));
        Assert.IsFalse(service.Process(mount, rider));
        pfs.Verify(x => x.ApplyFileWrites(It.IsAny<PackFileContainer>(), It.IsAny<IReadOnlyCollection<PackFileWrite>>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public void SameAction_OnlyUsesVariantsOfTheRequestedAction()
    {
        var rider = new AnimationBinWh3("rider.bin");
        rider.AnimationTableEntries.Add(Entry("RIDER_STAND_IDLE_1", "idle.anim"));
        rider.AnimationTableEntries.Add(Entry("RIDER_ATTACK_2", "attack.anim"));
        rider.AnimationTableEntries.Add(Entry("RIDER_DEATH_STAND_1", "death.anim"));
        Assert.AreEqual("attack.anim", BatchProcessorService.FindSameAction(rider, "RIDER_ATTACK_10")!.AnimationRefs[0].AnimationFile);
        Assert.AreEqual("death.anim", BatchProcessorService.FindSameAction(rider, "RIDER_DEATH_STAND_15")!.AnimationRefs[0].AnimationFile);
        Assert.IsNull(BatchProcessorService.FindSameAction(rider, "RIDER_DEATH_RUN_1"));
    }

    [TestMethod]
    public void Batch_WritesLinkedCarrierAndRiderTablesWithDynamicSynchronizedPairs()
    {
        var skeleton = Skeleton("person", ("animroot", -1), ("root", 0), ("arm", 1));
        var carrierClip = Clip(skeleton, 21, 1);
        for (var frame = 0; frame < carrierClip.DynamicFrames.Count; frame++)
            carrierClip.DynamicFrames[frame].Position[0] = new Vector3(frame * 0.1f, 0, 0);
        var riderClip = Clip(skeleton, 11, 0.5);
        riderClip.DynamicFrames[^1].Rotation[2] = Quaternion.CreateFromAxisAngle(Vector3.Right, 1);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var carrierSource = carrierClip.ConvertToFileFormat(skeleton);
        carrierSource.Header.FrameRate = 20;
        foreach (var path in new[] { "carrier_attack.anim", "carrier_run.anim", "carrier_death.anim" })
            files[path] = AnimationFile.ConvertToBytes(carrierSource);
        files["rider.anim"] = AnimationFile.ConvertToBytes(riderClip.ConvertToFileFormat(skeleton));
        var originalFiles = files.Keys.ToHashSet();
        var pfs = new Mock<IPackFileService>();
        pfs.Setup(x => x.GetEditablePack()).Returns(new PackFileContainer("output"));
        pfs.Setup(x => x.FindFile(It.IsAny<string>(), It.IsAny<PackFileContainer?>()))
            .Returns((string path, PackFileContainer? _) => files.TryGetValue(path, out var data) ? PackFile.CreateFromBytes(path, data) : null);
        pfs.Setup(x => x.ApplyFileWrites(It.IsAny<PackFileContainer>(), It.IsAny<IReadOnlyCollection<PackFileWrite>>(), It.IsAny<bool>()))
            .Returns((PackFileContainer _, IReadOnlyCollection<PackFileWrite> writes, bool _) =>
            {
                foreach (var write in writes)
                    files.Add(write.Path, write.Content);
                return writes.Select(x => PackFile.CreateFromBytes(x.Path, x.Content)).ToArray();
            });
        AnimationBinEntry Action(string name, params string[] paths)
        {
            var slot = Entry(name, paths);
            foreach (var reference in slot.AnimationRefs)
                reference.AnimationMetaFile = reference.AnimationSoundMetaFile = "";
            return slot;
        }
        var mount = new AnimationBinWh3("mount.bin") { Name = "mount", SkeletonName = "person" };
        mount.AnimationTableEntries.AddRange([
            Action("ATTACK_1", "carrier_attack.anim"), Action("RUN_2", "carrier_run.anim"),
            Action("DEATH_STAND_1", "carrier_death.anim")]);
        var rider = new AnimationBinWh3("rider.bin") { Name = "rider", SkeletonName = "person" };
        rider.AnimationTableEntries.AddRange([
            Action("RIDER_ATTACK_1", "rider.anim", "rider.anim"), Action("RIDER_RUN_2", "rider.anim"),
            Action("RIDER_MOVING_ATTACK_1", "rider.anim"), Action("RIDER_DEATH_STAND_1", "rider.anim"),
            Action("RIDER_DEATH_RUN_1", "rider.anim")]);
        var sourceMount = mount.ToByteArray();
        var sourceRider = rider.ToByteArray();
        var service = new BatchProcessorService(pfs.Object, Mock.Of<ISkeletonAnimationLookUpHelper>(),
            Generator(skeleton, new AnimationSettingsViewModel { FitAnimation = false, IsRootNodeAnimation = false }),
            new BatchProcessOptions { AnimPackName = "pairs", FragmentName = "paired_rider", SavePrefix = "new_" },
            Mock.Of<IFileSaveService>(), Mock.Of<IStandardDialogs>(), 7);

        Assert.IsTrue(service.Process(mount, rider));
        var savedPack = files.Single(x => x.Key.EndsWith(".animpack"));
        var tables = AnimationPackSerializer.Load(PackFile.CreateFromBytes(savedPack.Key, savedPack.Value), pfs.Object)
            .Files.OfType<AnimationBinWh3>().ToArray();
        Assert.AreEqual(2, tables.Length);
        var outputRider = tables.Single(x => x.Name == "paired_rider");
        var outputCarrier = tables.Single(x => x.Name == outputRider.MountBin);
        Assert.AreNotEqual(mount.Name, outputCarrier.Name);
        foreach (var name in new[] { "ATTACK_1", "RUN_2", "MOVING_ATTACK_1", "DEATH_STAND_1", "DEATH_RUN_1" })
        {
            var mountSlot = outputCarrier.AnimationTableEntries.Single(x => x.AnimationId == AnimationSlotTypeHelperWh3.GetfromValue(name).Id);
            var riderSlot = outputRider.AnimationTableEntries.Single(x => x.AnimationId == AnimationSlotTypeHelperWh3.GetfromValue("RIDER_" + name).Id);
            Assert.AreEqual(mountSlot.AnimationRefs.Count, riderSlot.AnimationRefs.Count, name);
            Assert.AreEqual(mountSlot.BlendIn, riderSlot.BlendIn, name);
            for (var variant = 0; variant < mountSlot.AnimationRefs.Count; variant++)
            {
                var carrierFile = AnimationFile.Create(pfs.Object.FindFile(mountSlot.AnimationRefs[variant].AnimationFile)!);
                var riderFile = AnimationFile.Create(pfs.Object.FindFile(riderSlot.AnimationRefs[variant].AnimationFile)!);
                var carrier = new AnimationClip(carrierFile, skeleton);
                var result = new AnimationClip(riderFile, skeleton);
                Assert.AreEqual(carrier.Duration, result.Duration, name);
                Assert.AreEqual(carrier.DynamicFrames.Count, result.DynamicFrames.Count, name);
                Assert.AreEqual(carrierFile.Header.FrameRate, riderFile.Header.FrameRate, 0.0001f, name);
                Assert.IsTrue(Vector3.Distance(result.DynamicFrames[0].Position[0], result.DynamicFrames[^1].Position[0]) > 1, name);
                for (var frame = 0; frame < result.DynamicFrames.Count; frame++)
                    Assert.AreEqual(carrier.DynamicFrames[frame].Position[0], result.DynamicFrames[frame].Position[0], name);
            }
        }
        CollectionAssert.AreEqual(sourceMount, mount.ToByteArray());
        CollectionAssert.AreEqual(sourceRider, rider.ToByteArray());
        Assert.IsTrue(files.Keys.Except(originalFiles).Any());
    }

    [TestMethod]
    public void RiderAnimation_TechBindingCannotBeUsedAsAnAction()
    {
        var service = new RiderAnimationService(Mock.Of<IPackFileService>(), Mock.Of<ISkeletonAnimationLookUpHelper>());
        Assert.ThrowsException<InvalidOperationException>(() => service.Load("models/tech/person.anim", Skeleton("person", ("root", -1))));
    }

    [TestMethod]
    public void RiderAnimation_RetargetsRealMotionToTheTargetRig()
    {
        var source = Skeleton("source", ("animroot", -1), ("root", 0), ("arm", 1));
        var target = Skeleton("target", ("animroot", -1), ("root", 0), ("arm", 1), ("extra", 2));
        source.Translation[2] = new Vector3(0, 1, 0);
        target.Translation[2] = new Vector3(0, 2, 0);
        target.Translation[3] = new Vector3(1, 0, 0);
        source.RebuildSkeletonMatrix();
        target.RebuildSkeletonMatrix();
        var clip = Clip(source, 2, 1);
        clip.DynamicFrames[1].Rotation[2] = Quaternion.CreateFromAxisAngle(Vector3.Right, 1);
        var file = PackFile.CreateFromBytes("attack.anim", AnimationFile.ConvertToBytes(clip.ConvertToFileFormat(source)));
        var pfs = new Mock<IPackFileService>();
        pfs.Setup(x => x.FindFile("attack.anim", It.IsAny<PackFileContainer?>())).Returns(file);
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(AnimationClip.CreateSkeletonAnimation(source).ConvertToFileFormat(source));

        var result = new RiderAnimationService(pfs.Object, lookup.Object).Load("attack.anim", target);

        Assert.AreEqual(4, result.AnimationBoneCount);
        Assert.IsTrue(Math.Abs(Quaternion.Dot(result.DynamicFrames[0].Rotation[2], result.DynamicFrames[1].Rotation[2])) < 0.99f);
        Assert.AreEqual(target.Translation[3], result.DynamicFrames[1].Position[3]);
        Assert.AreEqual("target", result.ConvertToFileFormat(target).Header.SkeletonName);
    }

    [TestMethod]
    public void RiderAnimation_WeaponFollowsTheLongerArmInsteadOfTheOldRootTrack()
    {
        var source = Skeleton("source", ("animroot", -1), ("root", 0), ("hand_right", 1), ("be_prop_1", 0));
        var target = Skeleton("target", ("animroot", -1), ("root", 0), ("hand_right", 1), ("be_prop_1", 0));
        source.Translation[2] = new Vector3(1, 0, 0);
        target.Translation[2] = new Vector3(3, 0, 0);
        source.RebuildSkeletonMatrix();
        target.RebuildSkeletonMatrix();
        var clip = Clip(source, 2, 1);
        foreach (var frame in clip.DynamicFrames)
            frame.Position[3] = new Vector3(1, 0.1f, 0);
        var pfs = new Mock<IPackFileService>();
        pfs.Setup(x => x.FindFile("attack.anim", It.IsAny<PackFileContainer?>())).Returns(
            PackFile.CreateFromBytes("attack.anim", AnimationFile.ConvertToBytes(clip.ConvertToFileFormat(source))));
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(AnimationClip.CreateSkeletonAnimation(source).ConvertToFileFormat(source));
        var converted = new RiderAnimationService(pfs.Object, lookup.Object).Load("attack.anim", target);
        var pose = AnimationSampler.Sample(0, 0, target, converted);
        Assert.AreEqual(0.1f, Vector3.Distance(pose.GetSkeletonAnimatedWorld(target, 2).Translation,
            pose.GetSkeletonAnimatedWorld(target, 3).Translation), 0.0001f);
    }

    [TestMethod]
    public void RiderMetadata_RemapsBonesAndSplicesAndKeepsImpactTimingInSync()
    {
        var source = Skeleton("source", ("animroot", -1), ("root", 0), ("hand_right", 1), ("cape", 1));
        var target = Skeleton("target", ("animroot", -1), ("root", 0), ("extra", 1), ("hand_right", 1));
        var metadata = new ParsedMetadataFile { Version = 2, Attributes =
        [
            new Transform_v10 { Name = "TRANSFORM", Version = 10, TargetNode = 2, SourceNode = 2, StartTime = 1, EndTime = 2 },
            new Splice_v12 { Name = "SPLICE", Version = 12, GenericBoneIndex = 2, Animation = "grip.anim" },
            new Splice_v12 { Name = "SPLICE", Version = 12, GenericBoneIndex = 3, Animation = "cape.anim" },
            new SoundTrigger_v10 { Name = "SOUND_TRIGGER", Version = 10, BoneIndex = 2, StartTime = 1.5f, EndTime = 1.5f },
            new DockEquipmentRHand_v11 { Name = "DOCK_EQPT_RHAND", Version = 11, PropBoneId = 2 }
        ] };
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var pfs = new Mock<IPackFileService>();
        pfs.Setup(x => x.FindFile("attack.meta", It.IsAny<PackFileContainer?>())).Returns(
            PackFile.CreateFromBytes("attack.meta", parser.GenerateBytes(2, metadata)));
        var writes = new List<PackFileWrite>();
        var report = new ErrorList();
        var converter = new RiderMetadataService(pfs.Object, source, target, path => "new_" + path,
            path => "new_" + path, writes, report);
        Assert.AreEqual("new_attack.meta", converter.Convert("attack.meta", 0.5f));
        var result = parser.ParseFile(writes.Single().Content);
        Assert.AreEqual(4, result.Attributes.Count);
        Assert.AreEqual(3, result.GetItemsOfType<Transform_v10>().Single().TargetNode);
        Assert.AreEqual(0.5f, result.GetItemsOfType<Transform_v10>().Single().StartTime);
        Assert.AreEqual("new_grip.anim", result.GetItemsOfType<Splice_v12>().Single().Animation);
        Assert.AreEqual(3, result.GetItemsOfType<Splice_v12>().Single().GenericBoneIndex);
        Assert.AreEqual(0.75f, result.GetItemsOfType<SoundTrigger_v10>().Single().StartTime);
        Assert.AreEqual(3, result.GetItemsOfType<SoundTrigger_v10>().Single().BoneIndex);
        Assert.AreEqual(2, result.GetItemsOfType<DockEquipmentRHand_v11>().Single().PropBoneId);
        Assert.IsTrue(report.Errors.Any());
    }

    internal static AnimationBinEntry Entry(string slot, params string[] animations) => new()
    {
        AnimationId = (uint)AnimationSlotTypeHelperWh3.GetfromValue(slot).Id,
        BlendIn = 0.25f,
        SelectionWeight = 2,
        WeaponBools = 3,
        AnimationRefs = animations.Select((path, index) => new AnimationBinEntry.AnimationRef
        {
            AnimationFile = path, AnimationMetaFile = $"meta_{index}", AnimationSoundMetaFile = $"sound_{index}"
        }).ToList()
    };

    internal static GameSkeleton Skeleton(string name, params (string Name, int Parent)[] bones)
    {
        var file = new AnimationFile();
        file.Header.SkeletonName = name;
        file.Bones = bones.Select((bone, index) => new AnimationFile.BoneInfo { Id = index, Name = bone.Name, ParentId = bone.Parent }).ToArray();
        return GameSkeleton.CreateFromAnimationFile(file, new AnimationPlayer());
    }

    internal static AnimationClip Clip(GameSkeleton skeleton, int frames, double seconds) => new()
    {
        Duration = TimeSpan.FromSeconds(seconds),
        DynamicFrames = Enumerable.Range(0, frames).Select(_ => new AnimationClip.KeyFrame
        {
            Position = new(skeleton.Translation), Rotation = new(skeleton.Rotation),
            Scale = Enumerable.Repeat(Vector3.One, skeleton.BoneCount).ToList()
        }).ToList()
    };

    internal static MountAnimationGeneratorService Generator(GameSkeleton skeleton, AnimationSettingsViewModel settings)
    {
        var geometry = new MeshObject(Mock.Of<IGraphicsCardGeometry>(), skeleton.SkeletonName)
        {
            VertexArray = [new VertexPositionNormalTextureCustom
            {
                Position = new Vector4(0, 2, 0, 1), BlendIndices = Vector4.Zero, BlendWeights = new Vector4(1, 0, 0, 0)
            }],
            IndexArray = []
        };
        geometry.ChangeVertexType(UiVertexFormat.Weighted, false);
        var mesh = new Rmv2MeshNode(geometry, new WeightedMaterial(), null!, null!);
        return new MountAnimationGeneratorService(settings, mesh, 0, 1,
            new SceneObject("rider") { Skeleton = skeleton }, new SceneObject("mount") { Skeleton = skeleton });
    }
}
