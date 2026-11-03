using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    private static IEnumerable<TestCaseData> TrackingAvailabilityCases()
    {
        foreach (var hasClip in new[] { false, true })
            foreach (var hasBones in new[] { false, true })
                foreach (var enabled in new[] { false, true })
                    foreach (var bone in new[] { -1, 0 })
                        yield return new TestCaseData(hasClip, hasBones, enabled, bone);
    }

    [TestCaseSource(nameof(TrackingAvailabilityCases))]
    public void EffectTrackingAvailability_RequiresAClipAndBoneMatricesButDoesNotTreatDisabledAnimationAsAMissingResource(
        bool hasClip, bool hasBones, bool enabled, int bone)
    {
        using var control = new TrackingAvailabilityControl(hasClip, hasBones, enabled, bone);
        var original = control.Source.Data;
        var initial = control.Update(.6f);
        var first = initial.Attachment;
        var tracks = hasClip && hasBones;
        AssertParameterMatrix(first, control.Expected(tracks ? .6f : control.Source.StartTime));
        control.Root.ModelMatrix = Matrix.CreateRotationY(-.8f) * Matrix.CreateTranslation(31, -4, 8);
        var next = control.Update(1.1f);
        var expected = tracks ? control.Expected(1.1f) : first;
        AssertParameterMatrix(next.Attachment, expected);
        AssertParameterMatrix(next.History!(0), tracks ? control.Expected(control.Source.StartTime) : first);
        AssertParameterMatrix(next.History(.3f), tracks ? control.Expected(control.Source.StartTime + .3f) : first);
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(control.Effect.FocusPosition, expected.Translation), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(control.Effect.HitTargets.Single().Position, expected.Translation), Is.LessThan(.00001f));
            Assert.That(control.Source.Tracking, Is.True, "Availability changes the runtime route, never the author flag.");
            Assert.That(control.Source.NodeIndex, Is.EqualTo(bone));
            Assert.That(control.Source.Data, Is.SameAs(original));
            Assert.That(control.Player.AnimationClip, Is.SameAs(hasClip ? control.Clip : null));
            Assert.That(control.Player.IsEnabled, Is.EqualTo(enabled));
        });
    }

    [TestCase(-1)]
    [TestCase(0)]
    public void EffectTrackingAvailability_UsesTheRootLoadedClipWhenTheBoneProviderHasOnlyABindPose(int bone)
    {
        using var control = new TrackingAvailabilityControl(true, true, true, bone, separateProviderPlayer: true);
        var initial = control.Update(.6f).Attachment;
        control.Root.ModelMatrix = Matrix.CreateRotationY(-.8f) * Matrix.CreateTranslation(31, -4, 8);
        var next = control.Update(1.1f);
        AssertParameterMatrix(next.Attachment, control.Expected(1.1f));
        Assert.That(Vector3.Distance(initial.Translation, next.Attachment.Translation), Is.GreaterThan(5));
        Assert.That(control.Skeleton.AnimationPlayer.AnimationClip, Is.Null);
        Assert.That(control.Player.AnimationClip, Is.SameAs(control.Clip));
    }

    [TestCase(-1)]
    [TestCase(0)]
    public void EffectTrackingAvailability_ClipRemovalAndReplacementInvalidateTheFrozenRouteAndTheHistoricalBoneCache(int bone)
    {
        using var control = new TrackingAvailabilityControl(true, true, true, bone);
        var before = control.Update(.6f).Attachment;
        control.Player.SetAnimation(null!, control.Skeleton);
        control.Root.ModelMatrix = Matrix.CreateTranslation(20, 3, -7);
        var frozen = control.Update(.8f).Attachment;
        AssertParameterMatrix(frozen, control.Expected(control.Source.StartTime));
        Assert.That(Vector3.Distance(before.Translation, frozen.Translation), Is.GreaterThan(5));
        control.Root.ModelMatrix = Matrix.CreateRotationY(.7f) * Matrix.CreateTranslation(47, -2, 4);
        AssertParameterMatrix(control.Update(1).Attachment, frozen);

        control.Clip = TrackingAvailabilityControl.CreateClip(70);
        control.Player.SetAnimation(control.Clip, control.Skeleton);
        var restarted = control.Update(1.1f);
        AssertParameterMatrix(restarted.Attachment, control.Expected(1.1f));
        AssertParameterMatrix(restarted.History!(0), control.Expected(control.Source.StartTime));
        control.Root.ModelMatrix = Matrix.CreateRotationY(-.5f) * Matrix.CreateTranslation(-31, 6, 9);
        AssertParameterMatrix(control.Update(1.3f).Attachment, control.Expected(1.3f));
        Assert.That(control.Source.Tracking, Is.True);
    }

    [TestCase(false, false, -1)]
    [TestCase(false, false, 0)]
    [TestCase(false, true, -1)]
    [TestCase(false, true, 0)]
    [TestCase(true, false, -1)]
    [TestCase(true, false, 0)]
    [TestCase(true, true, -1)]
    [TestCase(true, true, 0)]
    public void EffectTrackingAvailability_NoBoneMatricesStillUsesTheLoadedClipRootReferenceAndFreezesTheCreationWorld(
        bool compensation, bool clone, int bone)
    {
        var player = new AnimationPlayer { IsEnabled = true };
        var skeleton = Skeleton(player);
        skeleton.BoneNames[0] = "animroot";
        skeleton.Rotation[0] = Quaternion.CreateFromAxisAngle(Vector3.Up, .3f);
        skeleton.RebuildSkeletonMatrix();
        var clip = FileBackedRootClip(skeleton);
        var sourceReference = clip.SourceRootReferenceRotation;
        if (clone) clip = clip.Clone();
        Assert.Multiple(() =>
        {
            Assert.That(clip.SourceRootReferenceRotation, Is.EqualTo(sourceReference));
            Assert.That(clip.SourceRootName, Is.EqualTo("animroot"));
        });
        player.SetAnimation(clip, skeleton);
        player.IsEnabled = false;
        player.Refresh();
        var source = new Effect_v12 { Name = "EFFECT", Version = 12, VfxName = "parameter_fixture", Tracking = true,
            NodeIndex = bone, StartTime = .25f, EndTime = .9f, Position = new(1, 2, -3) };
        var builder = ParameterBuilder(out var factory);
        builder.EffectPreviewContext = new() { ApplyRootMotionCompensation = compensation };
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("missing-bone-buffer")
        { ModelMatrix = Matrix.CreateRotationY(.4f) * Matrix.CreateTranslation(8, 3, -5) });
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [source] }, null,
            root, null!, player, null);
        try
        {
            var effect = (EffectMetaInstance)result.Instances.Single();
            var node = (VfxPreviewSceneNode)root.Children.Single().Children.Single();
            effect.Update(.6f);
            Assert.That(node.PrepareFrame(effect.WorldTransform), Is.True);
            // DIAG: native raw-source FPS=2, Start=.25 samples halfway through keys 0/1.
            var position = Vector3.Lerp(new(4, 1, -2), new(10, 1, 4), .5f);
            var heading = (int)MathF.Round(.5f * 10430.3779296875f, MidpointRounding.AwayFromZero);
            var correction = compensation
                ? Matrix.CreateTranslation(-position) * Matrix.CreateRotationY(-heading * .0000958738019107841f)
                : Matrix.Identity;
            var expected = Matrix.CreateTranslation(source.Position) * correction * root.ModelMatrix;
            var preview = factory.Created.Single();
            AssertParameterMatrix(preview.Attachment, expected);
            AssertParameterMatrix(preview.History!(.3f), expected);
            Assert.That(Vector3.Distance(effect.FocusPosition, expected.Translation), Is.LessThan(.0001f));
            root.ModelMatrix = Matrix.CreateRotationY(-1) * Matrix.CreateTranslation(80, -4, 15);
            effect.Update(.8f);
            node.PrepareFrame(effect.WorldTransform);
            AssertParameterMatrix(preview.Attachment, expected);
            AssertParameterMatrix(preview.History!(0), expected);
            Assert.That(Vector3.Distance(effect.FocusPosition, expected.Translation), Is.LessThan(.0001f));
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [Test]
    public void EffectTrackingAvailability_ManualClipWithoutAnyKnownRootReferenceDoesNotInventAnIdentityReference()
    {
        Assert.That(EffectRootMotionCompensation.TryCreate(null, TrackingAvailabilityControl.CreateClip(), .25f, true, out var correction), Is.False);
        Assert.That(correction, Is.EqualTo(Matrix.Identity));
    }

    private static AnimationClip FileBackedRootClip(GameSkeleton skeleton)
    {
        var file = new AnimationFile
        {
            Header = new() { SkeletonName = "test", FrameRate = 2, AnimationTotalPlayTimeInSec = 1 },
            Bones = [new() { Id = 0, Name = "animroot", ParentId = -1 }],
        };
        var part = new AnimationFile.AnimationPart();
        part.TranslationMappings.Add(new(0));
        part.RotationMappings.Add(new(0));
        for (var index = 0; index < 3; index++)
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, .8f);
            part.DynamicFrames.Add(new()
            {
                Transforms = [new RmvVector3(new Vector3(4 + index * 6, 1, -2 + index * 6))],
                Quaternion = [new RmvVector4(rotation.X, rotation.Y, rotation.Z, rotation.W)],
            });
        }
        file.AnimationParts.Add(part);
        return new(file, skeleton);
    }

    private sealed class TrackingAvailabilityControl : IDisposable
    {
        private readonly SceneManager _scene = new(null!, null!, Mock.Of<IEventHub>());
        private readonly bool _hasBones;
        private readonly MetaDataBuildResult _result;
        private readonly CapturingFactory _factory;
        public AnimationPlayer Player { get; } = new() { IsEnabled = true };
        public GameSkeleton Skeleton { get; }
        public AnimationClip Clip { get; set; } = CreateClip();
        public Effect_v12 Source { get; }
        public GroupNode Root { get; }
        public EffectMetaInstance Effect { get; }

        public TrackingAvailabilityControl(bool hasClip, bool hasBones, bool enabled, int bone, bool separateProviderPlayer = false)
        {
            _hasBones = hasBones;
            Skeleton = EffectVfxPreviewTests.Skeleton(separateProviderPlayer ? new AnimationPlayer() : Player);
            Skeleton.Translation[0] = new(3, 2, -1);
            Skeleton.RebuildSkeletonMatrix();
            Player.SetAnimation(hasClip ? Clip : null!, Skeleton);
            Player.IsEnabled = enabled;
            Player.Pause();
            Player.Refresh();
            Source = new Effect_v12 { Name = "EFFECT", Version = 12, VfxName = "parameter_fixture", Tracking = true,
                NodeIndex = bone, Position = new(1, 2, -3), Scale = 1.4f, StartTime = .25f, EndTime = 2, Data = [1, 2, 3] };
            Root = _scene.RootNode.AddObject(new GroupNode("availability-actor")
            { ModelMatrix = Matrix.CreateRotationY(.4f) * Matrix.CreateTranslation(8, 3, -5) });
            var provider = new Mock<ISkeletonProvider>();
            provider.SetupGet(value => value.Skeleton).Returns(hasBones ? Skeleton : null!);
            var builder = ParameterBuilder(out _factory);
            builder.EffectPreviewContext = new() { ApplyRootMotionCompensation = false };
            _result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [Source] }, null,
                Root, provider.Object, Player, null);
            Effect = (EffectMetaInstance)_result.Instances.Single();
        }

        public Matrix Expected(float seconds)
        {
            var bone = Matrix.Identity;
            if (_hasBones && Source.NodeIndex == 0)
                bone = Skeleton.AnimationPlayer.IsEnabled && Skeleton.AnimationPlayer.AnimationClip != null
                    ? AnimationSampler.Sample(seconds / 2, Skeleton, Skeleton.AnimationPlayer.AnimationClip, Skeleton.AnimationPlayer.AnimationRules).GetSkeletonAnimatedWorld(Skeleton, 0)
                    : Skeleton.GetWorldTransform(0);
            return EffectAttachmentTransform.Create(Quaternion.Identity, Source.Position, Source.Scale, false, bone, Root.ModelMatrix);
        }
        public CapturingPreview Update(float seconds)
        {
            Player.SeekToTimeSeconds(seconds);
            Effect.Update(new AnimationPlaybackTime(seconds, seconds, 1, 0, false, false));
            var node = (VfxPreviewSceneNode)Root.Children.Single().Children.Single();
            Assert.That(node.PrepareFrame(Effect.WorldTransform), Is.True);
            return _factory.Created.Last();
        }
        public static AnimationClip CreateClip(float offset = 0)
        {
            var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(2) };
            for (var index = 0; index < 3; index++)
                clip.DynamicFrames.Add(new() { Position = [new(4 + offset + index * 6, 1, -2)],
                    Rotation = [Quaternion.Identity], Scale = [Vector3.One] });
            return clip;
        }
        public void Dispose()
        {
            foreach (var instance in _result.Instances) instance.CleanUp();
            _scene.Dispose();
        }
    }
}
