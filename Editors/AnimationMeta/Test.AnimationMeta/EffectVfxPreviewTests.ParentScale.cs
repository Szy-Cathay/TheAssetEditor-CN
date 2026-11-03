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
    private static IEnumerable<TestCaseData> ParentScaleCases()
    {
        foreach (var (version, parentScale) in new[] { (11, false), (12, false), (12, true) })
            foreach (var tracking in new[] { false, true })
                foreach (var actorKind in new[] { 0, 1, 2 })
                    yield return new TestCaseData(version, parentScale, tracking, actorKind);
    }

    [TestCaseSource(nameof(ParentScaleCases))]
    public void EffectParentScale_RealSceneRoutesCurrentAndHistoryWithoutScalingTheParticleOffsetTwice(
        int version, bool parentScale, bool tracking, int actorKind)
    {
        var source = ParameterEffect(version);
        var effect = (IEffectMeta)source;
        effect.Tracking = tracking; effect.NodeIndex = 0;
        effect.Position = new(1.25f, -.75f, 2.5f);
        effect.Orientation = new(.7f, 0, 0, .7f);
        if (source is Effect_v12 current) current.ScalesWithParent = parentScale;
        var originalData = source.Data.ToArray();
        var originalPosition = effect.Position;
        var originalOrientation = effect.Orientation;
        const float authoredScale = 2.5f, start = .25f, time = 1.25f;
        var player = new AnimationPlayer { IsEnabled = true };
        var skeleton = Skeleton(player);
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(2) };
        for (var index = 0; index < 3; ++index)
            clip.DynamicFrames.Add(new()
            {
                Position = [new(3 + index, 1 + index, -2 + index)],
                Rotation = [Quaternion.CreateFromYawPitchRoll(.2f + index * .3f, -.4f, .1f)],
                Scale = [new(2, 5, 3)],
            });
        player.SetAnimation(clip, skeleton); player.Pause();
        var provider = new Mock<ISkeletonProvider>(); provider.SetupGet(value => value.Skeleton).Returns(skeleton);
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var outer = scene.RootNode.AddObject(new GroupNode("outer")
        { ModelMatrix = Matrix.CreateRotationZ(.35f) * Matrix.CreateTranslation(10, -2, 6) });
        var actorMatrix = actorKind switch
        {
            0 => Matrix.CreateScale(2, 3, 4) * Matrix.CreateRotationY(.4f),
            1 => Matrix.CreateScale(-2, 3, 4) * Matrix.CreateRotationX(.25f),
            _ => new Matrix(2, 0, 0, 0, 1, 3, 0, 0, 2, 1, 4, 0, 0, 0, 0, 1),
        };
        actorMatrix.Translation = new(5, 1, -3);
        var actor = outer.AddObject(new GroupNode("actor") { ModelMatrix = actorMatrix });
        var builder = ParameterBuilder(out var factory);
        var result = builder.Build(null, new ParsedMetadataFile { Version = 10, Attributes = [source] }, null,
            actor, provider.Object, player, null);
        try
        {
            var instance = (EffectMetaInstance)result.Instances.Single();
            var node = (VfxPreviewSceneNode)actor.Children.Single().Children.Single();
            Matrix Bone(float seconds) => AnimationSampler.Sample(seconds / 2, skeleton, clip, player.AnimationRules)
                .GetSkeletonAnimatedWorld(skeleton, 0);
            Matrix Expected(float age, Matrix actorWorld) => EffectAttachmentTransform.Create(new Quaternion(effect.Orientation),
                effect.Position, authoredScale, parentScale, Bone(tracking ? age + start : start), actorWorld);
            player.SeekToTimeSeconds(time); instance.Update(time);
            var world = scene.GetWorldPosition(actor);
            Assert.That(node.CurrentAttachment, Is.Not.Null);
            Assert.That(node.WorldAttachmentAtTime, Is.Not.Null);
            AssertParameterMatrix(node.CurrentAttachment!(), Expected(time - start, world));
            AssertParameterMatrix(node.WorldAttachmentAtTime!(0), Expected(0, world));
            Assert.That(node.PrepareFrame(instance.WorldTransform), Is.True);
            var preview = factory.Created.Single();
            AssertParameterMatrix(preview.Attachment, Expected(time - start, world));
            AssertParameterMatrix(preview.History!(0), Expected(0, world));
            AssertParameterMatrix(preview.History(time - start), Expected(time - start, world));
            Assert.That(Vector3.Distance(instance.FocusPosition, Expected(time - start, world).Translation), Is.LessThan(.0001f));
            Assert.That(result.Diagnostics.Any(value => value.ReasonKey == "SuperView.Diagnostics.EffectParentScaleUnsupported"), Is.False);

            actor.ModelMatrix = actorMatrix * Matrix.CreateTranslation(7, -3, 2);
            AssertParameterMatrix(node.CurrentAttachment!(), Expected(time - start, world));
            AssertParameterMatrix(node.WorldAttachmentAtTime!(0), Expected(0, world));
            instance.Update(time);
            var movedWorld = scene.GetWorldPosition(actor);
            AssertParameterMatrix(node.CurrentAttachment!(), Expected(time - start, tracking ? movedWorld : world));
            AssertParameterMatrix(node.WorldAttachmentAtTime!(0), Expected(0, tracking ? movedWorld : world));

            Assert.Multiple(() =>
            {
                Assert.That(effect.Position, Is.EqualTo(originalPosition));
                Assert.That(effect.Orientation, Is.EqualTo(originalOrientation));
                Assert.That(effect.Tracking, Is.EqualTo(tracking));
                Assert.That(effect.NodeIndex, Is.Zero);
                Assert.That(source.Data, Is.EqualTo(originalData));
                Assert.That(source.Version, Is.EqualTo(version));
                if (source is Effect_v12 v12)
                {
                    Assert.That(v12.Scale, Is.EqualTo(authoredScale));
                    Assert.That(v12.ScalesWithParent, Is.EqualTo(parentScale));
                }
                else Assert.That(((Effect_v11)source).Scale, Is.EqualTo(authoredScale));
            });
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }
}
