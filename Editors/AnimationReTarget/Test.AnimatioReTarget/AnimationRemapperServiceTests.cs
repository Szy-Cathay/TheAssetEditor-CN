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

    private static GameSkeleton CreateSkeleton(string name, int boneCount)
    {
        var skeletonFile = new AnimationFile
        {
            Bones = Enumerable.Range(0, boneCount)
                .Select(index => new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = $"bone_{index}",
                    ParentId = index - 1,
                })
                .ToArray(),
        };
        skeletonFile.Header.SkeletonName = name;
        return GameSkeleton.CreateFromAnimationFile(skeletonFile, new AnimationPlayer());
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
}
