using System.Reflection;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;
using Shared.Core.Settings;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void FrozenEffectMarker_ActorMotionAndSingularScaleKeepFocusHitAndRenderedGeometryAtTheParticleBirth(bool singular)
    {
        using var control = new NativeTrackingControl(12, scalesWithParent: true);
        control.SetPose(.8f);
        control.Update(new(.8f, .8, 1, 0, false, false));
        var particles = control.Factory.Created.Single();
        var frozen = particles.Attachment;
        var marker = (SimpleDrawableNode)control.Root.Children.Single();
        var local = marker.ModelMatrix;
        var originalActor = control.Root.ModelMatrix;
        var rawReference = control.Effect.ReferenceWorldTransform;
        var expectedPose = Matrix.CreateScale(1 / 3.75f) * frozen;
        var renderer = MarkerRenderer();
        marker.Render(renderer, originalActor);
        var before = CaptureMarker(renderer);
        AssertParameterMatrix(control.Effect.WorldTransform, expectedPose);
        Assert.That(control.Effect.WorldTransform.Up.Length(), Is.EqualTo(1).Within(.0001f),
            "Authored Scale and ScalesWithParent change particle geometry, not the marker size.");

        control.Root.ModelMatrix = Matrix.CreateScale(singular ? 0 : -2, 4, 3)
            * Matrix.CreateFromYawPitchRoll(-1.1f, .6f, -.5f) * Matrix.CreateTranslation(71, -13, 19);
        control.SetPose(1.3f);
        control.Update(new(1.3f, 1.3, 1, 0, false, false));
        renderer.Update(new GameTime());
        marker.Render(renderer, control.Root.ModelMatrix);
        var after = CaptureMarker(renderer);
        Assert.Multiple(() =>
        {
            AssertParameterMatrix(particles.Attachment, frozen);
            AssertParameterMatrix(control.Effect.WorldTransform, expectedPose);
            Assert.That(control.Effect.FocusPosition, Is.EqualTo(frozen.Translation));
            Assert.That(control.Effect.HitTargets.Single().Position, Is.EqualTo(frozen.Translation));
            Assert.That(marker.ModelMatrix, Is.EqualTo(local), "The scene graph keeps the raw local transform for child compatibility.");
            AssertParameterMatrix(control.Effect.ReferenceWorldTransform,
                rawReference * Matrix.Invert(originalActor) * control.Root.ModelMatrix);
            Assert.That(after.Lines, Is.EqualTo(before.Lines));
            Assert.That(after.Triangles, Is.EqualTo(before.Triangles));
            Assert.That(after.Edges, Is.EqualTo(before.Edges));
            Assert.That(after.TextWorlds, Is.EqualTo(before.TextWorlds));
            Assert.That(after.Lines, Has.Length.GreaterThan(0));
            Assert.That(after.Triangles, Has.Length.GreaterThan(0));
            Assert.That(after.Edges, Has.Length.GreaterThan(0));
            Assert.That(after.TextWorlds, Has.Length.GreaterThan(0));
        });

        control.Update(new(1.3f, 1.3, 2, 0, false, false));
        var restarted = control.Factory.Created.Last().Attachment;
        Assert.That(Vector3.Distance(restarted.Translation, frozen.Translation), Is.GreaterThan(5));
        Assert.That(control.Effect.FocusPosition, Is.EqualTo(restarted.Translation), "A new playback revision establishes a new marker birth.");
    }

    [Test]
    public void OrdinaryDrawableWithoutWorldDisplayProvider_RetainsItsLocalTimesParentRenderContract()
    {
        var node = new SimpleDrawableNode("ordinary")
        { ModelMatrix = Matrix.CreateRotationZ(.3f) * Matrix.CreateTranslation(2, -1, 4) };
        var points = new[] { new Vector3(1, 2, 3), new Vector3(-3, 4, 1) };
        node.AddItem(points.Select(point => new VertexPositionColor(point, Color.White)).ToArray());
        var parent = Matrix.CreateScale(2, 3, 4) * Matrix.CreateRotationY(.6f) * Matrix.CreateTranslation(8, -2, 5);
        var renderer = MarkerRenderer();
        node.Render(renderer, parent);
        Assert.Multiple(() =>
        {
            Assert.That(node.DisplayWorldTransform, Is.Null);
            Assert.That(CaptureMarker(renderer).Lines, Is.EqualTo(points.Select(point => Vector3.Transform(point, node.ModelMatrix * parent))));
            Assert.That(((SimpleDrawableNode)node.CreateCopyInstance()).DisplayWorldTransform, Is.Null);
        });
    }

    private static RenderEngineComponent MarkerRenderer() => new(null!, null!, null!, null!,
        new ApplicationSettingsService(), new(), Mock.Of<IEventHub>(), new GridComponent(null!, null!, null!));

    private static (Vector3[] Lines, Vector3[] Triangles, (Vector3, Vector3)[] Edges, Matrix[] TextWorlds) CaptureMarker(RenderEngineComponent renderer)
    {
        object Field(string name) => typeof(RenderEngineComponent).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        return (((List<VertexPositionColor>)Field("_renderLines")).Select(vertex => vertex.Position).ToArray(),
            ((List<VertexPositionColor>)Field("_translucentPreviewTriangles")).Select(vertex => vertex.Position).ToArray(),
            ((List<EdgeData>)Field("_previewEdges")).Select(edge => (edge.P0, edge.P1)).ToArray(),
            ((Dictionary<RenderBuckedId, List<IRenderItem>>)Field("_renderItems"))[RenderBuckedId.Font]
                .Cast<WorldTextRenderItem>().Select(item => item.ModelMatrix).ToArray());
    }
}
