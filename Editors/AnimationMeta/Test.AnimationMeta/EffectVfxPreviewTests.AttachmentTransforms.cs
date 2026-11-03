using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(7)]
    [TestCase(11)]
    [TestCase(12)]
    public void EffectAttachment_UntrackedEffectsUseTheBirthBoneInsteadOfTheActorOrigin(int version)
    {
        using var control = new AttachmentControl(version, false);
        foreach (var time in new[] { .6f, 1.4f, .8f })
        {
            var preview = control.Update(time);
            var expected = control.ExpectedAt(control.Start);
            AssertParameterMatrix(preview.Attachment, expected);
            AssertParameterMatrix(preview.History!(0), expected);
            AssertParameterMatrix(preview.History(time - control.Start), expected);
            Assert.That(Vector3.Distance(control.Effect.FocusPosition, expected.Translation), Is.LessThan(.0001f));
        }
    }

    [TestCase(7)]
    [TestCase(11)]
    [TestCase(12)]
    public void EffectAttachment_TrackedRootSentinelIsValidAndHasNoMissingBoneWarning(int version)
    {
        using var control = new AttachmentControl(version, true, -1);
        var preview = control.Update(.6f);
        AssertParameterMatrix(preview.Attachment, control.ExpectedAt(0, bindPose: true));
        Assert.That(control.Result.Diagnostics.Any(value => value.ReasonKey == "SuperView.Diagnostics.MissingBone"), Is.False);
    }

    [Test]
    public void EffectAttachment_HistoryInvalidatesWhenTheSameClipChangesAnimationRules()
    {
        using var control = new AttachmentControl(12, true);
        var before = control.Update(.6f).History!(0);
        control.Player.AnimationRules.Add(new TransformBoneRule(new Transform_v10
        { TargetNode = 0, Position = new(7, -2, 3), Orientation = Quaternion.CreateFromYawPitchRoll(.2f, -.4f, .3f).ToVector4() }));
        var preview = control.Update(.6f);
        var expected = control.ExpectedAt(control.Start);
        Assert.That(Vector3.Distance(before.Translation, expected.Translation), Is.GreaterThan(1));
        AssertParameterMatrix(preview.History!(0), expected);
    }

    [TestCase(false, -2)]
    [TestCase(true, -2)]
    [TestCase(false, 99)]
    [TestCase(true, 99)]
    public void EffectAttachment_InvalidBoneStillReportsItsSourceAndKeepsTheRootFallback(bool tracking, int bone)
    {
        using var control = new AttachmentControl(12, tracking, bone);
        Assert.That(control.Result.Diagnostics.Single(value => value.ReasonKey == "SuperView.Diagnostics.MissingBone").Source,
            Is.SameAs(control.Effect.Source));
        AssertParameterMatrix(control.Update(.6f).Attachment, control.ExpectedAt(0, bindPose: true));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EffectAttachment_TogglingAnimationDoesNotReuseThePreviousPoseHistory(bool tracking)
    {
        using var control = new AttachmentControl(12, tracking);
        var animated = control.Update(.6f).History!(0);
        control.Player.IsEnabled = false;
        var preview = control.Update(.6f);
        AssertParameterMatrix(preview.History!(0), control.ExpectedAt(0, bindPose: true));
        control.Player.IsEnabled = true;
        AssertParameterMatrix(control.Update(.6f).History!(0), animated);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EffectAttachment_LoopTailKeepsItsOwnFinalBoneAndBirthHistory(bool shareResources)
    {
        var player = new AnimationPlayer();
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(1) };
        for (var index = 0; index < 2; ++index)
            clip.DynamicFrames.Add(new() { Position = [Vector3.Zero], Rotation = [Quaternion.Identity], Scale = [Vector3.One] });
        player.SetAnimation(clip, Skeleton(player));
        using var control = new TailPlaybackControl(0, 0, player: player);
        control.Factory.ShareResources = shareResources;
        control.Node.LocalAttachmentAtTime = seconds => Matrix.CreateRotationY(Math.Min(seconds, 1))
            * Matrix.CreateTranslation(Math.Min(seconds, 1) * 10, 0, 0);
        var end = control.Node.LocalAttachmentAtTime(1);
        control.Update(new(.9f, .9, 1, 0, true, false), control.Node.LocalAttachmentAtTime(.9f));
        var first = control.Factory.Created.Single();
        control.Update(new(0, 1, 1, 1, true, false), Matrix.Identity);
        control.Update(new(.2f, 1.2, 1, 1, true, false), control.Node.LocalAttachmentAtTime(.2f));
        AssertParameterMatrix(first.Attachment, end);
        AssertParameterMatrix(first.History!(.4f), control.Node.LocalAttachmentAtTime(.4f));
        Assert.That(first.Time, Is.EqualTo(1.2f).Within(.00001f));
    }

    private sealed class AttachmentControl : IDisposable
    {
        public AnimationPlayer Player { get; } = new() { IsEnabled = true };
        public GroupNode Root { get; }
        public MetaDataBuildResult Result { get; }
        public EffectMetaInstance Effect { get; }
        public Matrix Local { get; }
        public float Start { get; }
        private readonly GameSkeleton _skeleton;
        private readonly AnimationClip _clip;
        private readonly CapturingFactory _factory;
        private readonly SceneManager _scene = new(null!, null!, Mock.Of<IEventHub>());
        public AttachmentControl(int version, bool tracking, int bone = 0)
        {
            var source = ParameterEffect(version);
            var effect = (IEffectMeta)source;
            effect.Tracking = tracking; effect.NodeIndex = bone;
            effect.Position = new(1.5f, -2, 3);
            effect.Orientation = Quaternion.CreateFromYawPitchRoll(.3f, -.5f, .7f).ToVector4();
            Start = MetaDataTimeRange.TryCreate(source, out var range) ? range.StartTime : 0;
            var scale = version is 3 or 5 or 7 or 11 or 12 ? 2.5f : 1;
            Local = Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(new Quaternion(effect.Orientation))
                * Matrix.CreateTranslation(effect.Position);
            _skeleton = Skeleton(Player);
            _clip = new AnimationClip { Duration = TimeSpan.FromSeconds(2) };
            for (var index = 0; index < 3; ++index)
                _clip.DynamicFrames.Add(new()
                {
                    Position = [new(4 + index * 5, 2 + index, -3 + index)],
                    Rotation = [Quaternion.CreateFromYawPitchRoll(.2f + index * .4f, -.3f + index * .2f, .1f)],
                    Scale = [Vector3.One],
                });
            Player.SetAnimation(_clip, _skeleton); Player.Pause();
            var provider = new Mock<ISkeletonProvider>(); provider.SetupGet(value => value.Skeleton).Returns(_skeleton);
            Root = _scene.RootNode.AddObject(new GroupNode("actor")
            { ModelMatrix = Matrix.CreateScale(1.4f) * Matrix.CreateFromYawPitchRoll(-.4f, .2f, .3f) * Matrix.CreateTranslation(9, 2, -7) });
            var builder = ParameterBuilder(out _factory);
            Result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [source] }, null,
                Root, provider.Object, Player, null);
            Effect = (EffectMetaInstance)Result.Instances.Single();
        }
        public Matrix Bone(float seconds) => AnimationSampler.Sample(seconds / 2, _skeleton, _clip, Player.AnimationRules)
            .GetSkeletonAnimatedWorld(_skeleton, 0);
        public Matrix ExpectedAt(float seconds, bool bindPose = false)
        {
            // This fixture has unit bone rotations and a fixed 1.4 actor scale; geometry does not inherit that scale.
            var expected = Local * (bindPose ? Matrix.Identity : Bone(seconds)) * Root.ModelMatrix;
            expected.Right /= 1.4f;
            expected.Up /= 1.4f;
            expected.Backward /= 1.4f;
            return expected;
        }
        public CapturingPreview Update(float seconds)
        {
            Player.SeekToTimeSeconds(seconds); Effect.Update(seconds);
            var marker = Root.Children.Single();
            Assert.That(((VfxPreviewSceneNode)marker.Children.Single()).PrepareFrame(marker.ModelMatrix), Is.True);
            return _factory.Created.Last();
        }
        public void Dispose()
        {
            foreach (var instance in Result.Instances) instance.CleanUp();
            _scene.Dispose();
        }
    }
}
