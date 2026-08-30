using Editors.AnimatioReTarget.Editor;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.AnimatioReTarget.Editor.BoneHandling.Presentation;
using Editors.AnimatioReTarget.Editor.Settings;
using GameWorld.Core.Animation;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.GameFormats.Animation;

namespace Test.AnimatioReTarget;

public class AnimationRemapperServiceTests
{
    [Test]
    public void ReMapAnimation_SourceAtBindPose_PreservesTargetBindPose()
    {
        var sourceSkeleton = CreateSkeleton("source", 1);
        sourceSkeleton.Rotation[0] = Quaternion.CreateFromAxisAngle(
            Vector3.UnitX,
            MathHelper.ToRadians(90));
        sourceSkeleton.RebuildSkeletonMatrix();

        var targetSkeleton = CreateSkeleton("target", 1);
        var targetBindRotation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            MathHelper.ToRadians(-90));
        targetSkeleton.Rotation[0] = targetBindRotation;
        targetSkeleton.RebuildSkeletonMatrix();

        var animation = CreateAnimation(2, 1, 1.0f);
        foreach (var frame in animation.DynamicFrames)
            frame.Rotation[0] = sourceSkeleton.Rotation[0];

        var targetRoot = new SkeletonBoneNode_new("target_root", 0, -1)
        {
            HasMapping = true,
            MappedIndex = 0,
        };
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            [targetRoot]);

        var result = service.ReMapAnimation(
            sourceSkeleton,
            targetSkeleton,
            animation);

        var actual = Quaternion.Normalize(result.DynamicFrames[0].Rotation[0]);
        var expected = Quaternion.Normalize(targetBindRotation);
        Assert.That(MathF.Abs(Quaternion.Dot(actual, expected)), Is.GreaterThan(0.9999f));
    }

    [Test]
    public void ReMapAnimation_RelativeScale_DoesNotScaleTargetBindTranslationTwice()
    {
        var sourceSkeleton = CreateSkeleton("source", 2);
        sourceSkeleton.Translation[1] = new Vector3(1, 0, 0);
        sourceSkeleton.RebuildSkeletonMatrix();
        var targetSkeleton = CreateSkeleton("target", 2);
        targetSkeleton.Translation[1] = new Vector3(2, 0, 0);
        targetSkeleton.RebuildSkeletonMatrix();
        var animation = CreateAnimation(2, 2, 1.0f);
        foreach (var frame in animation.DynamicFrames)
        {
            frame.Position[0] = sourceSkeleton.Translation[0];
            frame.Position[1] = sourceSkeleton.Translation[1];
        }

        var targetRoot = new SkeletonBoneNode_new("target_root", 0, -1)
        {
            HasMapping = true,
            MappedIndex = 0,
        };
        targetRoot.Children.Add(new SkeletonBoneNode_new("target_child", 1, 0)
        {
            HasMapping = true,
            MappedIndex = 1,
        });
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = true },
            [targetRoot]);

        var result = service.ReMapAnimation(
            sourceSkeleton,
            targetSkeleton,
            animation);

        Assert.That(
            Vector3.Distance(
                result.DynamicFrames[0].Position[1],
                new Vector3(2, 0, 0)),
            Is.LessThan(0.0001f));
    }

    [Test]
    public void ReMapAnimation_SnapWorldspace_PreservesTargetBasisAtSourceBindPose()
    {
        var sourceSkeleton = CreateSkeleton("source", 2);
        sourceSkeleton.Translation[1] = new Vector3(1, 0, 0);
        sourceSkeleton.Rotation[1] = Quaternion.CreateFromAxisAngle(
            Vector3.UnitX,
            MathHelper.ToRadians(90));
        sourceSkeleton.RebuildSkeletonMatrix();

        var targetSkeleton = CreateSkeleton("target", 2);
        var targetBindTranslation = new Vector3(2, 0, 0);
        var targetBindRotation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            MathHelper.ToRadians(-90));
        targetSkeleton.Translation[1] = targetBindTranslation;
        targetSkeleton.Rotation[1] = targetBindRotation;
        targetSkeleton.RebuildSkeletonMatrix();

        var animation = CreateAnimation(2, 2, 1.0f);
        foreach (var frame in animation.DynamicFrames)
        {
            frame.Position[0] = sourceSkeleton.Translation[0];
            frame.Rotation[0] = sourceSkeleton.Rotation[0];
            frame.Position[1] = sourceSkeleton.Translation[1];
            frame.Rotation[1] = sourceSkeleton.Rotation[1];
        }

        var targetRoot = new SkeletonBoneNode_new("target_root", 0, -1)
        {
            HasMapping = true,
            MappedIndex = 0,
        };
        targetRoot.Children.Add(new SkeletonBoneNode_new("target_child", 1, 0)
        {
            HasMapping = true,
            MappedIndex = 1,
            ForceSnapToWorld = true,
        });
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            [targetRoot]);

        var result = service.ReMapAnimation(
            sourceSkeleton,
            targetSkeleton,
            animation);

        var actualRotation = Quaternion.Normalize(result.DynamicFrames[0].Rotation[1]);
        Assert.Multiple(() =>
        {
            Assert.That(
                MathF.Abs(Quaternion.Dot(actualRotation, targetBindRotation)),
                Is.GreaterThan(0.9999f));
            Assert.That(
                Vector3.Distance(
                    result.DynamicFrames[0].Position[1],
                    targetBindTranslation),
                Is.LessThan(0.0001f));
        });
    }

    [Test]
    public void BoneManager_TargetHasMoreBones_BuildsSettingsForEveryTargetBone()
    {
        var sourceFile = CreateSkeletonFile("source", 2, "source_bone");
        var targetFile = CreateSkeletonFile("target", 4, "target_bone");
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
        lookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(targetFile);
        var manager = new BoneManager(
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
            lookup.Object);

        manager.UpdateSourceSkeleton("source");
        manager.UpdateTargetSkeleton("target");

        Assert.Multiple(() =>
        {
            Assert.That(manager.FlatBoneList, Has.Count.EqualTo(4));
            Assert.That(
                manager.FlatBoneList.Select(x => x.BoneName),
                Is.EqualTo(new[]
                {
                    "target_bone_0",
                    "target_bone_1",
                    "target_bone_2",
                    "target_bone_3",
                }));
        });
    }

    [Test]
    public void BoneManager_ApplyHumanoidMapping_MapsTargetBonesBeyondSourceCount()
    {
        var sourceFile = CreateSkeletonFile(
            "source",
            ("animroot", -1),
            ("root", 0),
            ("upperleg_left", 1));
        var targetFile = CreateSkeletonFile(
            "target",
            ("root", -1),
            ("pelvis", 0),
            ("Bip", 1),
            ("unused_3", 2),
            ("unused_4", 2),
            ("thigh_l", 2));
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
        lookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(targetFile);
        var manager = new BoneManager(
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
            lookup.Object);

        manager.UpdateSourceSkeleton("source");
        manager.UpdateTargetSkeleton("target");
        manager.ApplyHumanoidMapping();

        Assert.Multiple(() =>
        {
            Assert.That(BoneHelper_new.GetMappedIndex(manager.Bones, 0), Is.EqualTo(0));
            Assert.That(BoneHelper_new.GetMappedIndex(manager.Bones, 2), Is.EqualTo(1));
            Assert.That(BoneHelper_new.GetMappedIndex(manager.Bones, 5), Is.EqualTo(2));
            Assert.That(manager.MappingSummary, Is.EqualTo("3 / 6"));
        });
    }

    [Test]
    public void BoneManager_ApplyHumanoidMapping_PreservesUnrecognizedWeaponConfiguration()
    {
        var sourceFile = CreateSkeletonFile(
            "source",
            ("animroot", -1),
            ("root", 0),
            ("spine_0", 1),
            ("clav_left", 2),
            ("upperarm_left", 3),
            ("lowerarm_left", 4),
            ("hand_left", 5),
            ("finger_index_left_0", 6),
            ("upperleg_left", 1),
            ("upperleg_right", 1),
            ("weapon_1", 0));
        var targetFile = CreateSkeletonFile(
            "target",
            ("root", -1),
            ("pelvis", 0),
            ("Bip", 1),
            ("spine_01", 2),
            ("clavicle_l", 3),
            ("upperarm_l", 4),
            ("lowerarm_l", 5),
            ("hand_l", 6),
            ("index_01_l", 7),
            ("weapon_r", 7),
            ("weapon_root_ji", 9),
            ("weapon_01", 10),
            ("thigh_l", 2),
            ("thigh_r", 2));
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
        lookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(targetFile);
        var manager = new BoneManager(
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
            lookup.Object);

        manager.UpdateSourceSkeleton("source");
        manager.UpdateTargetSkeleton("target");
        var hand = BoneHelper_new.GetBoneFromId(manager.Bones, 7)!;
        hand.RotationOffset.X.Value = 45;
        hand.TranslationOffset.Y.Value = 3;
        hand.ForceSnapToWorld = true;
        var weaponRoot = BoneHelper_new.GetBoneFromId(manager.Bones, 10)!;
        weaponRoot.HasMapping = true;
        weaponRoot.MappedIndex = 10;
        weaponRoot.SelectedRelativeBone = hand;
        weaponRoot.FreezeRotation = true;
        manager.ApplyHumanoidMapping();

        Assert.Multiple(() =>
        {
            Assert.That(BoneHelper_new.GetBoneFromId(manager.Bones, 0)!.ApplyTranslation, Is.True);
            Assert.That(BoneHelper_new.GetBoneFromId(manager.Bones, 2)!.ApplyTranslation, Is.True);
            Assert.That(BoneHelper_new.GetBoneFromId(manager.Bones, 3)!.ApplyTranslation, Is.False);
            Assert.That(BoneHelper_new.GetBoneFromId(manager.Bones, 7)!.ApplyTranslation, Is.False);
            Assert.That(BoneHelper_new.GetBoneFromId(manager.Bones, 8)!.ApplyTranslation, Is.False);
            Assert.That(BoneHelper_new.GetMappedIndex(manager.Bones, 9), Is.Null);
            Assert.That(BoneHelper_new.GetMappedIndex(manager.Bones, 10), Is.EqualTo(10));
            Assert.That(BoneHelper_new.GetMappedIndex(manager.Bones, 11), Is.Null);
            Assert.That(hand.RotationOffset.X.Value, Is.Zero);
            Assert.That(hand.TranslationOffset.Y.Value, Is.Zero);
            Assert.That(hand.ForceSnapToWorld, Is.False);
            Assert.That(weaponRoot.SelectedRelativeBone, Is.SameAs(hand));
            Assert.That(weaponRoot.FreezeRotation, Is.True);
            Assert.That(weaponRoot.ApplyTranslation, Is.True);
        });
    }

    [Test]
    public void BoneManager_ApplyHumanoidMapping_ClearsExistingRigSpecificTwistMappings()
    {
        var sourceFile = CreateSkeletonFile(
            "source",
            ("animroot", -1),
            ("root", 0),
            ("upperleg_left", 1),
            ("upperleg_right", 1),
            ("lowerarm_roll_left_0", 1),
            ("weapon_1", 0));
        var targetFile = CreateSkeletonFile(
            "target",
            ("root", -1),
            ("pelvis", 0),
            ("Bip", 1),
            ("thigh_l", 2),
            ("thigh_r", 2),
            ("lowerarm_twist_01_l", 2),
            ("weapon_r", 2));
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
        lookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(targetFile);
        var manager = new BoneManager(
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
            lookup.Object);

        manager.UpdateSourceSkeleton("source");
        manager.UpdateTargetSkeleton("target");
        var twist = BoneHelper_new.GetBoneFromId(manager.Bones, 5)!;
        twist.HasMapping = true;
        twist.MappedIndex = 4;
        var weapon = BoneHelper_new.GetBoneFromId(manager.Bones, 6)!;
        weapon.HasMapping = true;
        weapon.MappedIndex = 5;

        manager.ApplyHumanoidMapping();

        Assert.Multiple(() =>
        {
            Assert.That(BoneHelper_new.GetMappedIndex(manager.Bones, 5), Is.Null);
            Assert.That(BoneHelper_new.GetMappedIndex(manager.Bones, 6), Is.EqualTo(5));
            Assert.That(manager.MappingSummary, Is.EqualTo("5 / 7"));
        });
    }

    [Test]
    public void ReMapAnimation_ThreeSourceFingerChains_DriveFiveTargetFingerChains()
    {
        var sourceFile = CreateSkeletonFile(
            "source",
            ("animroot", -1),
            ("root", 0),
            ("upperleg_left", 1),
            ("upperleg_right", 1),
            ("hand_left", 1),
            ("finger_index_left_0", 4),
            ("finger_index_left_1", 5),
            ("finger_index_left_2", 6),
            ("finger_ring_left_0", 4),
            ("finger_ring_left_1", 8),
            ("finger_ring_left_2", 9),
            ("thumb_left_0", 4),
            ("thumb_left_1", 11),
            ("thumb_left_2", 12));
        var targetFile = CreateSkeletonFile(
            "target",
            ("root", -1),
            ("pelvis", 0),
            ("thigh_l", 1),
            ("thigh_r", 1),
            ("hand_l", 1),
            ("index_01_l", 4),
            ("index_02_l", 5),
            ("index_03_l", 6),
            ("middle_01_l", 4),
            ("middle_02_l", 8),
            ("middle_03_l", 9),
            ("ring_01_l", 4),
            ("ring_02_l", 11),
            ("ring_03_l", 12),
            ("pinky_01_l", 4),
            ("pinky_02_l", 14),
            ("pinky_03_l", 15),
            ("thumb_01_l", 4),
            ("thumb_02_l", 17),
            ("thumb_03_l", 18));
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
        lookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(targetFile);
        var manager = new BoneManager(
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
            lookup.Object);
        manager.UpdateSourceSkeleton("source");
        manager.UpdateTargetSkeleton("target");
        manager.ApplyHumanoidMapping();

        var sourceSkeleton = GameSkeleton.CreateFromAnimationFile(
            sourceFile,
            new AnimationPlayer());
        var targetSkeleton = GameSkeleton.CreateFromAnimationFile(
            targetFile,
            new AnimationPlayer());
        var sourceAnimation = CreateAnimation(2, sourceFile.Bones.Length, 1.0f);
        var ringRotations = new[]
        {
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.ToRadians(15)),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.ToRadians(25)),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.ToRadians(35)),
        };
        for (var segment = 0; segment < ringRotations.Length; segment++)
            sourceAnimation.DynamicFrames[1].Rotation[8 + segment] = ringRotations[segment];
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            manager.Bones);

        var result = service.ReMapAnimation(
            sourceSkeleton,
            targetSkeleton,
            sourceAnimation);

        Assert.Multiple(() =>
        {
            for (var segment = 0; segment < ringRotations.Length; segment++)
            {
                AssertQuaternionEquivalent(
                    result.DynamicFrames[1].Rotation[8 + segment],
                    ringRotations[segment]);
                AssertQuaternionEquivalent(
                    result.DynamicFrames[1].Rotation[11 + segment],
                    ringRotations[segment]);
                AssertQuaternionEquivalent(
                    result.DynamicFrames[1].Rotation[14 + segment],
                    ringRotations[segment]);
            }
        });
    }

    [Test]
    public void ReMapAnimation_QuickMappingPreservesAndTransfersManualWeaponMotion()
    {
        var sourceFile = CreateSkeletonFile(
            "source",
            ("animroot", -1),
            ("root", 0),
            ("spine_0", 1),
            ("clav_left", 2),
            ("upperarm_left", 3),
            ("lowerarm_left", 4),
            ("hand_left", 5),
            ("upperleg_left", 1),
            ("upperleg_right", 1),
            ("unused", 1),
            ("weapon_1", 0));
        var targetFile = CreateSkeletonFile(
            "target",
            ("root", -1),
            ("pelvis", 0),
            ("Bip", 1),
            ("spine_01", 2),
            ("clavicle_l", 3),
            ("upperarm_l", 4),
            ("lowerarm_l", 5),
            ("hand_l", 6),
            ("weapon_r", 7),
            ("weapon_helper", 8),
            ("weapon_root_ji", 9));
        var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
        lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
        lookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(targetFile);
        var manager = new BoneManager(
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
            lookup.Object);
        manager.UpdateSourceSkeleton("source");
        manager.UpdateTargetSkeleton("target");
        var weaponRoot = BoneHelper_new.GetBoneFromId(manager.Bones, 10)!;
        weaponRoot.HasMapping = true;
        weaponRoot.MappedIndex = 10;
        manager.ApplyHumanoidMapping();

        var sourceSkeleton = GameSkeleton.CreateFromAnimationFile(
            sourceFile,
            new AnimationPlayer());
        var targetSkeleton = GameSkeleton.CreateFromAnimationFile(
            targetFile,
            new AnimationPlayer());
        var sourceAnimation = CreateAnimation(2, sourceFile.Bones.Length, 1.0f);
        var weaponRotation = Quaternion.CreateFromYawPitchRoll(
            MathHelper.ToRadians(20),
            MathHelper.ToRadians(35),
            MathHelper.ToRadians(-15));
        sourceAnimation.DynamicFrames[1].Rotation[10] = weaponRotation;
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            manager.Bones);

        var result = service.ReMapAnimation(
            sourceSkeleton,
            targetSkeleton,
            sourceAnimation);

        Assert.Multiple(() =>
        {
            Assert.That(BoneHelper_new.GetMappedIndex(manager.Bones, 10), Is.EqualTo(10));
            AssertQuaternionEquivalent(
                result.DynamicFrames[1].Rotation[10],
                weaponRotation);
        });
    }

    [Test]
    public void BoneManager_SelectingKnownSkeletonPair_RestoresSavedMapping()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ae-bone-manager-profile-{Guid.NewGuid():N}");
        try
        {
            var sourceFile = CreateSkeletonFile(
                "source",
                ("animroot", -1),
                ("root", 0),
                ("upperleg_left", 1));
            var targetFile = CreateSkeletonFile(
                "target",
                ("root", -1),
                ("pelvis", 0),
                ("Bip", 1),
                ("unused_3", 2),
                ("unused_4", 2),
                ("thigh_l", 2));
            var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
            lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
            lookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(targetFile);
            var store = CharacterRetargetProfileStore.CreateForFile(
                Path.Combine(directory, "profiles.json"));

            var firstManager = CreateBoneManager(lookup.Object, store);
            firstManager.UpdateSourceSkeleton("source");
            firstManager.UpdateTargetSkeleton("target");
            firstManager.ApplyHumanoidMapping();
            var styledBone = BoneHelper_new.GetBoneFromId(firstManager.Bones, 5)!;
            styledBone.BoneLengthMult = 1.25f;
            styledBone.RotationOffset.X.Value = 10;
            styledBone.FreezeRotationZ = true;
            styledBone.SelectedRelativeBone = BoneHelper_new.GetBoneFromId(
                firstManager.Bones,
                2);
            firstManager.SaveCharacterProfile();

            var restoredManager = CreateBoneManager(lookup.Object, store);
            restoredManager.UpdateSourceSkeleton("source");
            restoredManager.UpdateTargetSkeleton("target");

            Assert.Multiple(() =>
            {
                Assert.That(BoneHelper_new.GetMappedIndex(restoredManager.Bones, 0), Is.EqualTo(0));
                Assert.That(BoneHelper_new.GetMappedIndex(restoredManager.Bones, 2), Is.EqualTo(1));
                Assert.That(BoneHelper_new.GetMappedIndex(restoredManager.Bones, 5), Is.EqualTo(2));
                Assert.That(restoredManager.MappingSummary, Is.EqualTo("3 / 6"));
                var restoredBone = BoneHelper_new.GetBoneFromId(
                    restoredManager.Bones,
                    5)!;
                Assert.That(restoredBone.BoneLengthMult, Is.EqualTo(1.25f));
                Assert.That(restoredBone.RotationOffset.X.Value, Is.EqualTo(10));
                Assert.That(restoredBone.FreezeRotationZ, Is.True);
                Assert.That(restoredBone.SelectedRelativeBone?.BoneIndex, Is.EqualTo(2));
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void BoneManager_ApplyHumanoidMapping_DoesNotSaveUntilUserConfirms()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ae-bone-manager-unsaved-profile-{Guid.NewGuid():N}");
        try
        {
            var sourceFile = CreateSkeletonFile(
                "source",
                ("animroot", -1),
                ("root", 0),
                ("upperleg_left", 1));
            var targetFile = CreateSkeletonFile(
                "target",
                ("root", -1),
                ("pelvis", 0),
                ("Bip", 1),
                ("thigh_l", 2));
            var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
            lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
            lookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(targetFile);
            var profilePath = Path.Combine(directory, "profiles.json");
            var store = CharacterRetargetProfileStore.CreateForFile(profilePath);
            var manager = CreateBoneManager(lookup.Object, store);
            manager.UpdateSourceSkeleton("source");
            manager.UpdateTargetSkeleton("target");

            manager.ApplyHumanoidMapping();

            Assert.That(
                File.Exists(profilePath),
                Is.False,
                "Quick matching must remain an unsaved preview until the user saves the character profile.");

            manager.SaveCharacterProfile();

            Assert.That(File.Exists(profilePath), Is.True);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void BoneManager_SkeletonStructureChanges_DoesNotRestoreStaleProfile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ae-bone-manager-stale-profile-{Guid.NewGuid():N}");
        try
        {
            var sourceFile = CreateSkeletonFile(
                "source",
                ("animroot", -1),
                ("root", 0),
                ("upperleg_left", 1));
            var originalTargetFile = CreateSkeletonFile(
                "target",
                ("root", -1),
                ("pelvis", 0),
                ("Bip", 1),
                ("thigh_l", 2));
            var store = CharacterRetargetProfileStore.CreateForFile(
                Path.Combine(directory, "profiles.json"));
            var originalLookup = new Mock<ISkeletonAnimationLookUpHelper>();
            originalLookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
            originalLookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(originalTargetFile);
            var originalManager = CreateBoneManager(originalLookup.Object, store);
            originalManager.UpdateSourceSkeleton("source");
            originalManager.UpdateTargetSkeleton("target");
            originalManager.ApplyHumanoidMapping();
            originalManager.SaveCharacterProfile();

            var reexportedTargetFile = CreateSkeletonFile(
                "target",
                ("root", -1),
                ("pelvis", 0),
                ("weapon_helper", 1),
                ("Bip", 1),
                ("thigh_l", 3));
            var changedLookup = new Mock<ISkeletonAnimationLookUpHelper>();
            changedLookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
            changedLookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(reexportedTargetFile);
            var changedManager = CreateBoneManager(changedLookup.Object, store);

            changedManager.UpdateSourceSkeleton("source");
            changedManager.UpdateTargetSkeleton("target");

            Assert.Multiple(() =>
            {
                Assert.That(changedManager.MappingSummary, Is.EqualTo("-"));
                Assert.That(changedManager.HasValidMapping, Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void BoneManager_SkeletonBindPoseChanges_DoesNotRestoreStaleProfile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ae-bone-manager-bind-profile-{Guid.NewGuid():N}");
        try
        {
            var sourceFile = CreateSkeletonFile(
                "source",
                ("animroot", -1),
                ("root", 0),
                ("upperleg_left", 1));
            SetBindPose(sourceFile, Quaternion.Identity);
            var originalTargetFile = CreateSkeletonFile(
                "target",
                ("root", -1),
                ("pelvis", 0),
                ("Bip", 1),
                ("thigh_l", 2));
            SetBindPose(originalTargetFile, Quaternion.Identity);
            var store = CharacterRetargetProfileStore.CreateForFile(
                Path.Combine(directory, "profiles.json"));
            var originalLookup = new Mock<ISkeletonAnimationLookUpHelper>();
            originalLookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
            originalLookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(originalTargetFile);
            var originalManager = CreateBoneManager(originalLookup.Object, store);
            originalManager.UpdateSourceSkeleton("source");
            originalManager.UpdateTargetSkeleton("target");
            originalManager.ApplyHumanoidMapping();
            originalManager.SaveCharacterProfile();

            var reexportedTargetFile = CreateSkeletonFile(
                "target",
                ("root", -1),
                ("pelvis", 0),
                ("Bip", 1),
                ("thigh_l", 2));
            SetBindPose(
                reexportedTargetFile,
                Quaternion.CreateFromAxisAngle(
                    Vector3.UnitX,
                    MathHelper.ToRadians(90)));
            var changedLookup = new Mock<ISkeletonAnimationLookUpHelper>();
            changedLookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
            changedLookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(reexportedTargetFile);
            var changedManager = CreateBoneManager(changedLookup.Object, store);

            changedManager.UpdateSourceSkeleton("source");
            changedManager.UpdateTargetSkeleton("target");

            Assert.Multiple(() =>
            {
                Assert.That(changedManager.MappingSummary, Is.EqualTo("-"));
                Assert.That(changedManager.HasValidMapping, Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void BoneManager_SaveCharacterProfileFails_ShowsError()
    {
        new LocalizationManager().LoadLanguage();
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ae-bone-manager-save-profile-{Guid.NewGuid():N}");
        var profilePath = Path.Combine(directory, "profiles.json");
        try
        {
            Directory.CreateDirectory(profilePath);
            var sourceFile = CreateSkeletonFile(
                "source",
                ("animroot", -1),
                ("root", 0),
                ("upperleg_left", 1));
            var targetFile = CreateSkeletonFile(
                "target",
                ("root", -1),
                ("pelvis", 0),
                ("Bip", 1),
                ("thigh_l", 2));
            var lookup = new Mock<ISkeletonAnimationLookUpHelper>();
            lookup.Setup(x => x.GetSkeletonFileFromName("source")).Returns(sourceFile);
            lookup.Setup(x => x.GetSkeletonFileFromName("target")).Returns(targetFile);
            var dialogs = new Mock<IStandardDialogs>(MockBehavior.Strict);
            dialogs
                .Setup(dialog => dialog.ShowDialogBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()));
            var manager = new BoneManager(
                dialogs.Object,
                Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
                lookup.Object,
                CharacterRetargetProfileStore.CreateForFile(profilePath));
            manager.UpdateSourceSkeleton("source");
            manager.UpdateTargetSkeleton("target");
            manager.ApplyHumanoidMapping();

            manager.SaveCharacterProfile();

            dialogs.Verify(dialog => dialog.ShowDialogBox(
                It.Is<string>(message => !string.IsNullOrWhiteSpace(message)),
                It.Is<string>(title => !string.IsNullOrWhiteSpace(title))),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void ReMapAnimation_RelativeBoneIsUnmapped_SkipsAttachmentAdjustment()
    {
        var sourceSkeleton = CreateSkeleton("source", 2);
        var targetSkeleton = CreateSkeleton("target", 2);
        var animation = CreateAnimation(2, 2, 1.0f);
        var root = new SkeletonBoneNode_new("root", 0, -1);
        var attachment = new SkeletonBoneNode_new("attachment", 1, 0)
        {
            HasMapping = true,
            MappedIndex = 1,
            SelectedRelativeBone = root,
        };
        root.Children.Add(attachment);
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            [root]);

        AnimationClip? result = null;
        Assert.That(
            () => result = service.ReMapAnimation(sourceSkeleton, targetSkeleton, animation),
            Throws.Nothing);
        Assert.That(result!.DynamicFrames, Has.Count.EqualTo(2));
    }

    [Test]
    public void ReMapAnimation_AttachmentOffsetUsesTargetReferenceBoneLocalSpace()
    {
        var sourceFile = CreateSkeletonFile(
            "source",
            ("root", -1),
            ("hand", 0),
            ("attachment", 1));
        SetBindPose(sourceFile, Quaternion.Identity);
        sourceFile.AnimationParts[0].DynamicFrames[0].Transforms[2] = new(1, 0, 0);
        var targetFile = CreateSkeletonFile(
            "target",
            ("root", -1),
            ("hand", 0),
            ("attachment", 1));
        SetBindPose(targetFile, Quaternion.Identity);
        var targetHandBindRotation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            MathHelper.ToRadians(90));
        targetFile.AnimationParts[0].DynamicFrames[0].Quaternion[1] = new(
            targetHandBindRotation.X,
            targetHandBindRotation.Y,
            targetHandBindRotation.Z,
            targetHandBindRotation.W);
        targetFile.AnimationParts[0].DynamicFrames[0].Transforms[2] = new(1, 0, 0);
        var sourceSkeleton = GameSkeleton.CreateFromAnimationFile(
            sourceFile,
            new AnimationPlayer());
        var targetSkeleton = GameSkeleton.CreateFromAnimationFile(
            targetFile,
            new AnimationPlayer());
        var animation = CreateAnimation(2, 3, 1.0f);
        var sourceHandRotation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            MathHelper.ToRadians(90));
        foreach (var frame in animation.DynamicFrames)
        {
            frame.Rotation[1] = sourceHandRotation;
            frame.Position[2] = Vector3.UnitX;
        }

        var root = new SkeletonBoneNode_new("root", 0, -1)
        {
            HasMapping = true,
            MappedIndex = 0,
        };
        var hand = new SkeletonBoneNode_new("hand", 1, 0)
        {
            HasMapping = true,
            MappedIndex = 1,
        };
        var attachment = new SkeletonBoneNode_new("attachment", 2, 1)
        {
            HasMapping = true,
            MappedIndex = 2,
            SelectedRelativeBone = hand,
        };
        root.Children.Add(hand);
        hand.Children.Add(attachment);
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            [root]);

        var result = service.ReMapAnimation(
            sourceSkeleton,
            targetSkeleton,
            animation);

        Assert.That(
            Vector3.Distance(result.DynamicFrames[0].Position[2], Vector3.UnitX),
            Is.LessThan(0.0001f));
    }

    [Test]
    public void ReMapAnimation_RootAttachmentWithRelativeBone_DoesNotReadParentMinusOne()
    {
        var sourceSkeleton = CreateSkeleton("source", 2);
        var targetSkeleton = CreateSkeleton("target", 2);
        var animation = CreateAnimation(2, 2, 1.0f);
        var root = new SkeletonBoneNode_new("root", 0, -1)
        {
            HasMapping = true,
            MappedIndex = 0,
        };
        var child = new SkeletonBoneNode_new("child", 1, 0)
        {
            HasMapping = true,
            MappedIndex = 1,
        };
        root.Children.Add(child);
        root.SelectedRelativeBone = child;
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            [root]);

        Assert.That(
            () => service.ReMapAnimation(sourceSkeleton, targetSkeleton, animation),
            Throws.Nothing);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ReMapAnimation_InvalidBoneLengthMultiplier_IsRejected(float multiplier)
    {
        var skeleton = CreateSkeleton("shared", 1);
        var animation = CreateAnimation(2, 1, 1.0f);
        var bone = new SkeletonBoneNode_new("root", 0, -1)
        {
            HasMapping = true,
            MappedIndex = 0,
            BoneLengthMult = multiplier,
        };
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            [bone]);

        Assert.That(
            () => service.ReMapAnimation(skeleton, skeleton, animation),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ReMapAnimation_SameNameDifferentSkeletonStructure_UsesTargetSkeleton()
    {
        var sourceSkeleton = CreateSkeleton("shared", 1);
        var targetSkeleton = CreateSkeleton("shared", 2);
        targetSkeleton.Translation[1] = Vector3.UnitY;
        targetSkeleton.RebuildSkeletonMatrix();
        var animation = CreateAnimation(2, 1, 1.0f);
        var root = new SkeletonBoneNode_new("root", 0, -1)
        {
            HasMapping = true,
            MappedIndex = 0,
        };
        root.Children.Add(new SkeletonBoneNode_new("child", 1, 0));
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            [root]);

        var result = service.ReMapAnimation(
            sourceSkeleton,
            targetSkeleton,
            animation);

        Assert.That(result.AnimationBoneCount, Is.EqualTo(2));
    }

    [Test]
    public void ReMapAnimation_FreezeRotationZ_PreservesOtherRotationAxes()
    {
        var skeleton = CreateSkeleton("shared", 1);
        var animation = CreateAnimation(2, 1, 1.0f);
        var firstFrameTwist = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            MathHelper.ToRadians(25));
        var secondFrameSwing = Quaternion.CreateFromAxisAngle(
            Vector3.UnitX,
            MathHelper.ToRadians(35));
        var secondFrameTwist = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            MathHelper.ToRadians(70));
        animation.DynamicFrames[0].Rotation[0] = firstFrameTwist;
        animation.DynamicFrames[1].Rotation[0] = Quaternion.Normalize(
            secondFrameSwing * secondFrameTwist);
        var bone = new SkeletonBoneNode_new("root", 0, -1)
        {
            HasMapping = true,
            MappedIndex = 0,
            FreezeRotationZ = true,
        };
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            [bone]);

        var result = service.ReMapAnimation(skeleton, skeleton, animation);

        var actual = Quaternion.Normalize(result.DynamicFrames[1].Rotation[0]);
        var expected = Quaternion.Normalize(secondFrameSwing * firstFrameTwist);
        Assert.That(MathF.Abs(Quaternion.Dot(actual, expected)), Is.GreaterThan(0.9999f));
    }

    [Test]
    public void ReMapAnimation_SpeedMultiplierTwo_HalvesDurationAndPreservesSamplingRate()
    {
        var skeleton = CreateSkeleton("shared", 1);
        var animation = CreateAnimation(21, 1, 1.0f);
        var bone = new SkeletonBoneNode_new("root", 0, -1)
        {
            HasMapping = true,
            MappedIndex = 0,
        };
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings
            {
                AnimationSpeedMult = 2.0f,
                ApplyRelativeScale = false,
            },
            [bone]);

        var result = service.ReMapAnimation(skeleton, skeleton, animation);

        Assert.That(result.PlayTimeInSec, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(result.DynamicFrames, Has.Count.EqualTo(11));
    }

    [Test]
    public void ReMapAnimation_SpeedMultiplierZero_IsRejected()
    {
        var skeleton = CreateSkeleton("shared", 1);
        var animation = CreateAnimation(2, 1, 1.0f);
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings
            {
                AnimationSpeedMult = 0,
                ApplyRelativeScale = false,
            },
            []);

        Assert.That(
            () => service.ReMapAnimation(skeleton, skeleton, animation),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ReMapAnimation_SkeletonScaleZero_IsRejected()
    {
        var skeleton = CreateSkeleton("shared", 1);
        var animation = CreateAnimation(2, 1, 1.0f);
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings
            {
                SkeletonScale = 0,
                ApplyRelativeScale = false,
            },
            []);

        Assert.That(
            () => service.ReMapAnimation(skeleton, skeleton, animation),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ReMapAnimation_EmptySourceAnimation_IsRejected()
    {
        var skeleton = CreateSkeleton("shared", 1);
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            []);

        Assert.That(
            () => service.ReMapAnimation(
                skeleton,
                skeleton,
                new AnimationClip()),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ReMapAnimation_SingleFrame_PreservesFiniteFrameWithoutResampling()
    {
        var skeleton = CreateSkeleton("shared", 1);
        var animation = CreateAnimation(1, 1, 1.0f);
        animation.DynamicFrames[0].Position[0] = new Vector3(1, 2, 3);
        var service = new AnimationRemapperService(
            new AnimationGenerationSettings { ApplyRelativeScale = false },
            []);

        var result = service.ReMapAnimation(skeleton, skeleton, animation);

        Assert.Multiple(() =>
        {
            Assert.That(result.DynamicFrames, Has.Count.EqualTo(1));
            Assert.That(result.DynamicFrames[0].Position[0],
                Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(float.IsFinite(result.PlayTimeInSec), Is.True);
        });
    }

    [Test]
    public void ResetSelectedBoneCommand_RestoresEditableDefaults()
    {
        var relativeBone = new SkeletonBoneNode_new("relative", 1, 0);
        var bone = new SkeletonBoneNode_new("root", 0, -1)
        {
            IsLocalOffset = true,
            BoneLengthMult = 2,
            ForceSnapToWorld = true,
            FreezeTranslation = true,
            FreezeRotation = true,
            FreezeRotationZ = true,
            ApplyTranslation = false,
            ApplyRotation = false,
            SelectedRelativeBone = relativeBone,
        };
        bone.RotationOffset.X.Value = 10;
        bone.RotationOffset.Y.Value = 20;
        bone.RotationOffset.Z.Value = 30;
        bone.TranslationOffset.X.Value = 1;
        bone.TranslationOffset.Y.Value = 2;
        bone.TranslationOffset.Z.Value = 3;
        var dialogs = new Mock<IStandardDialogs>(MockBehavior.Strict);
        var manager = new BoneManager(
            dialogs.Object,
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
            Mock.Of<ISkeletonAnimationLookUpHelper>())
        {
            SelectedBone = bone,
        };

        manager.ResetSelectedBoneCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(bone.IsLocalOffset, Is.False);
            Assert.That(bone.BoneLengthMult, Is.EqualTo(1));
            Assert.That(bone.RotationOffset.X.Value, Is.Zero);
            Assert.That(bone.RotationOffset.Y.Value, Is.Zero);
            Assert.That(bone.RotationOffset.Z.Value, Is.Zero);
            Assert.That(bone.TranslationOffset.X.Value, Is.Zero);
            Assert.That(bone.TranslationOffset.Y.Value, Is.Zero);
            Assert.That(bone.TranslationOffset.Z.Value, Is.Zero);
            Assert.That(bone.ForceSnapToWorld, Is.False);
            Assert.That(bone.FreezeTranslation, Is.False);
            Assert.That(bone.FreezeRotation, Is.False);
            Assert.That(bone.FreezeRotationZ, Is.False);
            Assert.That(bone.ApplyTranslation, Is.True);
            Assert.That(bone.ApplyRotation, Is.True);
            Assert.That(bone.SelectedRelativeBone, Is.Null);
            dialogs.VerifyNoOtherCalls();
        });
    }

    [Test]
    public void ConfirmSparseMapping_WhenOnlyOneBoneIsMapped_AsksBeforeContinuing()
    {
        new LocalizationManager().LoadLanguage();
        var dialogs = new Mock<IStandardDialogs>(MockBehavior.Strict);
        dialogs
            .Setup(dialog => dialog.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.Cancel);
        var manager = new BoneManager(
            dialogs.Object,
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
            Mock.Of<ISkeletonAnimationLookUpHelper>());
        manager.FlatBoneList =
        [
            new SkeletonBoneNode_new("root", 0, -1)
            {
                HasMapping = true,
                MappedIndex = 0,
            },
            new SkeletonBoneNode_new("spine", 1, 0),
        ];

        var result = manager.ConfirmSparseMapping();

        Assert.That(result, Is.False);
        dialogs.Verify(dialog => dialog.ShowYesNoBox(
            It.Is<string>(message => !string.IsNullOrWhiteSpace(message)),
            It.Is<string>(title => !string.IsNullOrWhiteSpace(title))),
            Times.Once);
        dialogs.VerifyNoOtherCalls();
    }

    [Test]
    public void ConfirmSparseMapping_WhenMultipleBonesAreMapped_DoesNotPrompt()
    {
        var dialogs = new Mock<IStandardDialogs>(MockBehavior.Strict);
        var manager = new BoneManager(
            dialogs.Object,
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
            Mock.Of<ISkeletonAnimationLookUpHelper>());
        manager.FlatBoneList =
        [
            new SkeletonBoneNode_new("root", 0, -1)
            {
                HasMapping = true,
                MappedIndex = 0,
            },
            new SkeletonBoneNode_new("spine", 1, 0)
            {
                HasMapping = true,
                MappedIndex = 1,
            },
        ];

        var result = manager.ConfirmSparseMapping();

        Assert.That(result, Is.True);
        dialogs.VerifyNoOtherCalls();
    }

    private static GameSkeleton CreateSkeleton(string name, int boneCount)
    {
        var skeletonFile = CreateSkeletonFile(name, boneCount, "bone");
        return GameSkeleton.CreateFromAnimationFile(skeletonFile, new AnimationPlayer());
    }

    private static AnimationFile CreateSkeletonFile(
        string name,
        int boneCount,
        string boneNamePrefix)
    {
        var skeletonFile = new AnimationFile
        {
            Bones = Enumerable.Range(0, boneCount)
                .Select(index => new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = $"{boneNamePrefix}_{index}",
                    ParentId = index - 1,
                })
                .ToArray(),
        };
        skeletonFile.Header.SkeletonName = name;
        return skeletonFile;
    }

    private static AnimationFile CreateSkeletonFile(
        string name,
        params (string Name, int ParentId)[] bones)
    {
        var skeletonFile = new AnimationFile
        {
            Bones = bones
                .Select((bone, index) => new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = bone.Name,
                    ParentId = bone.ParentId,
                })
                .ToArray(),
        };
        skeletonFile.Header.SkeletonName = name;
        return skeletonFile;
    }

    private static void SetBindPose(
        AnimationFile skeletonFile,
        Quaternion rootRotation)
    {
        var frame = new AnimationFile.Frame();
        var part = new AnimationFile.AnimationPart();
        for (var boneIndex = 0; boneIndex < skeletonFile.Bones.Length; boneIndex++)
        {
            frame.Transforms.Add(new(0, boneIndex, 0));
            var rotation = boneIndex == 0
                ? rootRotation
                : Quaternion.Identity;
            frame.Quaternion.Add(new(
                rotation.X,
                rotation.Y,
                rotation.Z,
                rotation.W));
            part.TranslationMappings.Add(
                new AnimationFile.AnimationBoneMapping(boneIndex));
            part.RotationMappings.Add(
                new AnimationFile.AnimationBoneMapping(boneIndex));
        }

        part.DynamicFrames.Add(frame);
        skeletonFile.AnimationParts = [part];
    }

    private static AnimationClip CreateAnimation(int frameCount, int boneCount, float playTime)
    {
        var animation = new AnimationClip();
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frame = new AnimationClip.KeyFrame();
            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                frame.Position.Add(Vector3.Zero);
                frame.Rotation.Add(Quaternion.Identity);
                frame.Scale.Add(Vector3.One);
            }

            animation.DynamicFrames.Add(frame);
        }

        animation.PlayTimeInSec = playTime;
        return animation;
    }

    private static BoneManager CreateBoneManager(
        ISkeletonAnimationLookUpHelper lookup,
        CharacterRetargetProfileStore store)
    {
        return new BoneManager(
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
            lookup,
            store);
    }

    private static void AssertQuaternionEquivalent(
        Quaternion actual,
        Quaternion expected)
    {
        Assert.That(
            MathF.Abs(Quaternion.Dot(
                Quaternion.Normalize(actual),
                Quaternion.Normalize(expected))),
            Is.GreaterThan(0.9999f));
    }
}
