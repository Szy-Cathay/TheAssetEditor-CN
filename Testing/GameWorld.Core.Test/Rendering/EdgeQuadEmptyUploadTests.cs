using System.Reflection;
using System.Runtime.CompilerServices;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;

namespace GameWorld.Core.Test.Rendering;

public class EdgeQuadEmptyUploadTests
{
    [Test]
    public void Update_EmptyEdges_ClearsInstanceCountWithoutTouchingGpuBuffer()
    {
        var renderer = CreateUninitializedRendererWithInstanceCount(7);

        Assert.That(
            () => renderer.Update(Array.Empty<EdgeData>()),
            Throws.Nothing);
        Assert.That(renderer.CurrentInstanceCount, Is.Zero);
    }

    [Test]
    public void Draw_DirtyEmptyEdges_PropagatesClearBeforeReturning()
    {
        var renderer = CreateUninitializedRendererWithInstanceCount(7);
        var renderItem = new EdgeQuadRenderItem
        {
            EdgeQuadRenderer = renderer,
            Edges = Array.Empty<EdgeData>()
        };

        renderItem.Draw(null!, default, RenderingTechnique.Normal);

        Assert.That(renderer.CurrentInstanceCount, Is.Zero);
    }

    private static EdgeQuadInstanceMesh CreateUninitializedRendererWithInstanceCount(int count)
    {
        var renderer = (EdgeQuadInstanceMesh)RuntimeHelpers.GetUninitializedObject(
            typeof(EdgeQuadInstanceMesh));
        typeof(EdgeQuadInstanceMesh)
            .GetField("_currentInstanceCount", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(renderer, count);
        return renderer;
    }
}
