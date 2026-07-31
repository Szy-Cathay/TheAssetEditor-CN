using GameWorld.Core.Animation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Testing.GameWorld.Core.Animation;

[TestFixture]
internal class GameSkeletonAnimationFileTests
{
    [Test]
    public void CreateFromAnimationFile_UsesAnimationBindPose()
    {
        var animation = CreateAnimation([-1, 0]);
        var skeleton = GameSkeleton.CreateFromAnimationFile(animation, new AnimationPlayer());

        Assert.Multiple(() =>
        {
            Assert.That(skeleton.BoneCount, Is.EqualTo(2));
            Assert.That(skeleton.GetWorldTransform(0).Translation.X, Is.EqualTo(1).Within(0.001f));
            Assert.That(skeleton.GetWorldTransform(1).Translation.X, Is.EqualTo(3).Within(0.001f));
        });
    }

    [TestCase(-2)]
    [TestCase(2)]
    public void CreateFromAnimationFile_RejectsOutOfRangeParent(int parentId)
    {
        var animation = CreateAnimation([-1, parentId]);

        Assert.Throws<InvalidDataException>(
            () => GameSkeleton.CreateFromAnimationFile(animation, new AnimationPlayer()));
    }

    [Test]
    public void CreateFromAnimationFile_RejectsSelfParent()
    {
        var animation = CreateAnimation([-1, 1]);

        Assert.Throws<InvalidDataException>(
            () => GameSkeleton.CreateFromAnimationFile(animation, new AnimationPlayer()));
    }

    [Test]
    public void CreateFromAnimationFile_RejectsCycle()
    {
        var animation = CreateAnimation([1, 0]);

        Assert.Throws<InvalidDataException>(
            () => GameSkeleton.CreateFromAnimationFile(animation, new AnimationPlayer()));
    }

    [Test]
    public void CreateFromAnimationFile_AllowsParentStoredAfterChild()
    {
        var animation = CreateAnimation([1, -1]);

        var skeleton = GameSkeleton.CreateFromAnimationFile(animation, new AnimationPlayer());

        Assert.That(skeleton.GetWorldTransform(0).Translation.X, Is.EqualTo(3).Within(0.001f));
    }

    private static AnimationFile CreateAnimation(int[] parentIds)
    {
        var animation = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader { SkeletonName = "test" },
            Bones = parentIds
                .Select((parentId, index) => new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = $"bone_{index}",
                    ParentId = parentId,
                })
                .ToArray(),
        };

        var frame = new AnimationFile.Frame();
        var part = new AnimationFile.AnimationPart();
        for (var index = 0; index < parentIds.Length; index++)
        {
            frame.Transforms.Add(new RmvVector3 { X = index + 1 });
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            part.TranslationMappings.Add(new AnimationFile.AnimationBoneMapping(index));
            part.RotationMappings.Add(new AnimationFile.AnimationBoneMapping(index));
        }

        part.DynamicFrames.Add(frame);
        animation.AnimationParts.Add(part);
        return animation;
    }
}
