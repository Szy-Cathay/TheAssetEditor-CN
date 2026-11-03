using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
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
    private static IEnumerable<TestCaseData> LegacyNativeTrackingCases()
    {
        foreach (var version in new[] { 1, 2, 3, 4, 5 })
            foreach (var literal in new[] { "true", "false", "", "TRUE", "true " })
                yield return new TestCaseData(version, literal);
    }

    [TestCaseSource(nameof(LegacyNativeTrackingCases))]
    public void EffectNativeTracking_LegacyLiteralControlsTrackingWithoutChangingThePlaceholderOrSource(int version, string literal)
    {
        var tracking = literal == "true";
        using var control = new NativeTrackingControl(version, literal, !tracking);
        var data = control.Source.Data;
        var bytes = data.ToArray();
        var position = control.Meta.Position;
        var orientation = control.Meta.Orientation;
        control.SetPose(.9f);
        control.Update(new(.9f, .9, 1, 0, false, false));
        var preview = control.Factory.Created.Single();
        AssertParameterMatrix(preview.Attachment, control.ExpectedAt(tracking ? .9f : control.Start));
        AssertParameterMatrix(preview.History!(0), control.ExpectedAt(control.Start));
        control.SetPose(1.4f);
        control.Update(new(1.4f, 1.4, 1, 0, false, false));
        AssertParameterMatrix(preview.Attachment, control.ExpectedAt(tracking ? 1.4f : control.Start));
        AssertParameterMatrix(preview.History!(.4f), control.ExpectedAt(tracking ? control.Start + .4f : control.Start));
        Assert.Multiple(() =>
        {
            Assert.That(control.Meta.Tracking, Is.EqualTo(!tracking), "The unserialized interface placeholder is never rewritten.");
            Assert.That(control.Source is Effect_v1 first ? first.BoolUnknown : ((Effect_v2)control.Source).BoolUnknown,
                Is.EqualTo(literal));
            Assert.That(control.Source.Data, Is.SameAs(data));
            Assert.That(control.Source.Data, Is.EqualTo(bytes));
            Assert.That(control.Source.Version, Is.EqualTo(version));
            Assert.That(control.Meta.Position, Is.EqualTo(position));
            Assert.That(control.Meta.Orientation, Is.EqualTo(orientation));
        });
    }

    [Test]
    public void EffectNativeTracking_FirstRunningEligibleFrameCapturesTheActualHeldBonePose()
    {
        using var control = new NativeTrackingControl(12);
        control.SetPose(.1f);
        control.Update(new(.1f, .1, 1, 0, true, false));
        Assert.That(control.Factory.Created, Is.Empty);
        control.SetPose(.6f);
        var actualBone = control.SetManualBone(new(47, 8, -3), Quaternion.CreateFromYawPitchRoll(.8f, -.4f, .3f));
        var expected = control.ExpectedFromBone(actualBone);
        Assert.That(Vector3.Distance(expected.Translation, control.ExpectedAt(control.Start).Translation), Is.GreaterThan(5));
        control.Update(new(.6f, .6, 1, 0, true, false));
        var preview = control.Factory.Created.Single();
        AssertParameterMatrix(preview.Attachment, expected);
        AssertParameterMatrix(preview.History!(0), expected);
        control.SetPose(1.2f);
        control.Update(new(1.2f, 1.2, 1, 0, true, false));
        AssertParameterMatrix(preview.Attachment, expected);
        AssertParameterMatrix(preview.History!(.7f), expected);
    }

    [TestCase(.55f)]
    [TestCase(1.4f)]
    public void EffectNativeTracking_ExplicitSeekReconstructsTheStartInsteadOfTheCurrentlyHeldBone(float seek)
    {
        using var control = new NativeTrackingControl(12);
        control.SetPose(seek);
        var manualBone = control.SetManualBone(new(53, -9, 11), Quaternion.CreateFromYawPitchRoll(-.7f, .2f, .5f));
        control.Update(new(seek, seek, 8, 0, false, false));
        var expected = control.ExpectedAt(control.Start);
        var preview = control.Factory.Created.Single();
        Assert.That(Vector3.Distance(expected.Translation, control.ExpectedFromBone(manualBone).Translation), Is.GreaterThan(5));
        AssertParameterMatrix(preview.Attachment, expected);
        AssertParameterMatrix(preview.History!(seek - control.Start), expected);
        control.SetPose(.8f);
        control.Update(new(.8f, .8, 9, 0, false, false));
        Assert.That(preview.Disposed, Is.True);
        AssertParameterMatrix(control.Factory.Created.Last().Attachment, expected);
    }

    [Test]
    public void EffectNativeTracking_UntrackedWorldPositionRotationAndMagnificationStayFrozenUntilRevisionChanges()
    {
        using var control = new NativeTrackingControl(12, scalesWithParent: true);
        control.SetPose(.8f);
        control.Update(new(.8f, .8, 1, 0, false, false));
        var first = control.Factory.Created.Single();
        var frozen = first.Attachment;
        var history = first.History!;
        Assert.That(frozen.Up.Length(), Is.EqualTo(2.5f * 1.5f).Within(.0001f));
        control.Root.ModelMatrix = Matrix.CreateScale(4) * Matrix.CreateFromYawPitchRoll(-1.1f, .6f, -.5f)
            * Matrix.CreateTranslation(71, -13, 19);
        control.SetPose(1.3f);
        control.Update(new(1.3f, 1.3, 1, 0, false, false));
        AssertParameterMatrix(first.Attachment, frozen);
        AssertParameterMatrix(first.History!(0), frozen);
        AssertParameterMatrix(first.History(1), frozen);
        AssertParameterMatrix(history(1), frozen);
        Assert.That(first.Attachment.Up.Length(), Is.EqualTo(2.5f * 1.5f).Within(.0001f));
        control.Update(new(1.3f, 1.3, 2, 0, false, false));
        var restarted = control.Factory.Created.Last();
        var expected = control.ExpectedAt(control.Start);
        Assert.That(first.Disposed, Is.True);
        AssertParameterMatrix(restarted.Attachment, expected);
        Assert.That(restarted.Attachment.Up.Length(), Is.EqualTo(2.5f * 4).Within(.0001f));
        Assert.That(Vector3.Distance(restarted.Attachment.Translation, frozen.Translation), Is.GreaterThan(5));
        AssertParameterMatrix(history(1), frozen);
    }

    [Test]
    public void EffectNativeTracking_NaturalLoopCapturesANewWorldPoseWithoutMovingThePreviousTail()
    {
        using var control = new NativeTrackingControl(12, scalesWithParent: true);
        control.SetPose(.1f);
        control.Update(new(.1f, .1, 1, 0, true, false));
        control.SetPose(.6f);
        var firstBone = control.SetManualBone(new(31, 3, -4), Quaternion.CreateFromYawPitchRoll(.2f, -.5f, .1f));
        var firstExpected = control.ExpectedFromBone(firstBone);
        control.Update(new(.6f, .6, 1, 0, true, false));
        var first = control.Factory.Created.Single();
        var firstHistory = first.History!;
        control.SetPose(1.8f);
        control.Update(new(1.8f, 1.8, 1, 0, true, false));
        control.Root.ModelMatrix = Matrix.CreateScale(3) * Matrix.CreateRotationY(.9f) * Matrix.CreateTranslation(23, 6, -8);
        control.SetPose(0);
        control.Update(new(0, 2.2, 1, 1, true, false));
        Assert.That(control.Factory.Created, Has.Count.EqualTo(1), "The next cohort waits for its own META Start.");
        AssertParameterMatrix(first.Attachment, firstExpected);
        control.SetPose(.6f);
        var nextBone = control.SetManualBone(new(-17, 9, 12), Quaternion.CreateFromYawPitchRoll(-.4f, .3f, -.2f));
        var nextExpected = control.ExpectedFromBone(nextBone);
        control.Update(new(.6f, 2.8, 1, 1, true, false));
        var second = control.Factory.Created.Last();
        Assert.That(control.Factory.Created, Has.Count.EqualTo(2));
        Assert.That(first.Disposed, Is.False);
        AssertParameterMatrix(first.Attachment, firstExpected);
        AssertParameterMatrix(first.History!(0), firstExpected);
        AssertParameterMatrix(firstHistory(1.5f), firstExpected);
        AssertParameterMatrix(second.Attachment, nextExpected);
        AssertParameterMatrix(second.History!(0), nextExpected);
        Assert.That(second.Attachment.Up.Length(), Is.EqualTo(2.5f * 3).Within(.0001f));
        control.Root.ModelMatrix = Matrix.CreateScale(5) * Matrix.CreateTranslation(100, -20, 3);
        control.SetPose(.9f);
        control.Update(new(.9f, 3.1, 1, 1, true, false));
        AssertParameterMatrix(first.Attachment, firstExpected);
        AssertParameterMatrix(first.History!(1), firstExpected);
        AssertParameterMatrix(second.Attachment, nextExpected);
        AssertParameterMatrix(second.History!(1), nextExpected);
    }

    private sealed class NativeTrackingControl : IDisposable
    {
        private readonly SceneManager _scene = new(null!, null!, Mock.Of<IEventHub>());
        private readonly GameSkeleton _skeleton;
        private readonly AnimationClip _clip = new() { Duration = TimeSpan.FromSeconds(2) };
        private readonly MetaDataBuildResult _result;
        private readonly float _scale;
        private readonly bool _scalesWithParent;
        public AnimationPlayer Player { get; } = new() { IsEnabled = true };
        public ParsedMetadataAttribute Source { get; }
        public IEffectMeta Meta => (IEffectMeta)Source;
        public GroupNode Root { get; }
        public EffectMetaInstance Effect { get; }
        public CapturingFactory Factory { get; }
        public VfxPreviewSceneNode Node => (VfxPreviewSceneNode)Root.Children.Single().Children.Single();
        public float Start { get; }

        public NativeTrackingControl(int version, string legacy = "", bool placeholder = false, bool scalesWithParent = false)
        {
            Source = ParameterEffect(version);
            Meta.Tracking = placeholder;
            Meta.NodeIndex = 0;
            Meta.Position = new(1.5f, -.5f, 2);
            Meta.Orientation = Quaternion.CreateFromYawPitchRoll(.2f, -.3f, .1f).ToVector4();
            if (Source is Effect_v1 first) first.BoolUnknown = legacy;
            if (Source is Effect_v2 older) older.BoolUnknown = legacy;
            if (Source is Effect_v12 current) current.ScalesWithParent = scalesWithParent;
            Start = MetaDataTimeRange.TryCreate(Source, out var range) ? range.StartTime : 0;
            _scale = version is 3 or 5 or 7 or 11 or 12 ? 2.5f : 1;
            _scalesWithParent = scalesWithParent;
            _skeleton = Skeleton(Player);
            for (var index = 0; index < 3; index++)
                _clip.DynamicFrames.Add(new()
                {
                    Position = [new(2 + index * 9, 1 + index * 2, -3 + index)],
                    Rotation = [Quaternion.CreateFromYawPitchRoll(.1f + index * .3f, -.2f + index * .1f, .15f)],
                    Scale = [Vector3.One],
                });
            Player.SetAnimation(_clip, _skeleton);
            Player.Pause();
            var provider = new Mock<ISkeletonProvider>();
            provider.SetupGet(value => value.Skeleton).Returns(_skeleton);
            Root = _scene.RootNode.AddObject(new GroupNode("native-tracking")
            { ModelMatrix = Matrix.CreateScale(1.5f) * Matrix.CreateFromYawPitchRoll(-.4f, .2f, .3f) * Matrix.CreateTranslation(6, 2, -5) });
            var builder = ParameterBuilder(out var factory);
            Factory = factory;
            _result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [Source] }, null,
                Root, provider.Object, Player, null);
            Effect = (EffectMetaInstance)_result.Instances.Single();
        }

        public void SetPose(float seconds) => Player.SeekToTimeSeconds(seconds);
        public Matrix SetManualBone(Vector3 translation, Quaternion rotation)
        {
            var bone = new AnimationFrame.BoneKeyFrame
            { BoneIndex = 0, ParentBoneIndex = -1, Translation = translation, Rotation = rotation, Scale = Vector3.One };
            bone.ComputeWorldMatrixFromComponents();
            var frame = new AnimationFrame();
            frame.BoneTransforms.Add(bone);
            Player.SetManualFrame(frame);
            _skeleton.Update();
            return _skeleton.GetAnimatedWorldTranform(0);
        }
        public Matrix ExpectedAt(float seconds) => ExpectedFromBone(AnimationSampler.Sample(seconds / 2, _skeleton, _clip,
            Player.AnimationRules).GetSkeletonAnimatedWorld(_skeleton, 0));
        public Matrix ExpectedFromBone(Matrix bone) => EffectAttachmentTransform.Create(new Quaternion(Meta.Orientation), Meta.Position,
            _scale, _scalesWithParent, bone, _scene.GetWorldPosition(Root));
        public void Update(AnimationPlaybackTime time)
        {
            Effect.Update(time);
            if (Root.Children.Single().IsVisible) Node.PrepareFrame(Effect.WorldTransform);
        }
        public void Dispose()
        {
            foreach (var instance in _result.Instances) instance.CleanUp();
            _scene.Dispose();
        }
    }
}
