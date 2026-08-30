using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Testing.GameWorld.Core.Animation;

[TestFixture]
internal class AnimationSamplerRuleTimeTests
{
    [Test]
    public void Sample_NormalizedPlayhead_PassesCurrentSecondsToAnimationRules()
    {
        var skeleton = CreateSkeleton();
        var clip = AnimationClip.CreateSkeletonAnimation(skeleton);
        clip.Duration = TimeSpan.FromSeconds(2);
        var rule = new RecordingWorldSpaceRule();

        AnimationSampler.Sample(0.25f, skeleton, clip, [rule]);

        Assert.That(rule.LastTimeSeconds, Is.EqualTo(0.5f).Within(0.0001f));
    }

    private static GameSkeleton CreateSkeleton()
    {
        var animation = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader { SkeletonName = "test" },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Id = 0,
                    Name = "root",
                    ParentId = -1,
                },
            ],
        };

        var frame = new AnimationFile.Frame();
        frame.Transforms.Add(new RmvVector3());
        frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));

        var part = new AnimationFile.AnimationPart();
        part.TranslationMappings.Add(new AnimationFile.AnimationBoneMapping(0));
        part.RotationMappings.Add(new AnimationFile.AnimationBoneMapping(0));
        part.DynamicFrames.Add(frame);
        animation.AnimationParts.Add(part);

        return GameSkeleton.CreateFromAnimationFile(animation, new AnimationPlayer());
    }

    private sealed class RecordingWorldSpaceRule : IWorldSpaceAnimationRule
    {
        public float LastTimeSeconds { get; private set; }

        public void TransformFrameWorldSpace(AnimationFrame frame, float time)
        {
            LastTimeSeconds = time;
        }
    }
}
