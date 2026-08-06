using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Test.Rendering;

public class ViewportShadingPolicyTests
{
    [TestCase(
        ViewportShadingMode.MaterialPreview,
        RenderingTechnique.Normal,
        FillMode.Solid,
        true)]
    [TestCase(
        ViewportShadingMode.Solid,
        RenderingTechnique.Solid,
        FillMode.Solid,
        false)]
    [TestCase(
        ViewportShadingMode.Wireframe,
        RenderingTechnique.Solid,
        FillMode.WireFrame,
        false)]
    public void Resolve_SeparatesEditingAndPreviewPipelines(
        ViewportShadingMode mode,
        RenderingTechnique expectedTechnique,
        FillMode expectedFillMode,
        bool expectedBloom)
    {
        var result = ViewportShadingPolicy.Resolve(mode);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.SurfaceTechnique,
                Is.EqualTo(expectedTechnique));
            Assert.That(
                result.FillMode,
                Is.EqualTo(expectedFillMode));
            Assert.That(
                result.EnableBloom,
                Is.EqualTo(expectedBloom));
        });
    }

    [TestCase(
        ViewportShadingMode.MaterialPreview,
        ViewportShadingMode.Wireframe)]
    [TestCase(
        ViewportShadingMode.Wireframe,
        ViewportShadingMode.Solid)]
    [TestCase(
        ViewportShadingMode.Solid,
        ViewportShadingMode.MaterialPreview)]
    public void Next_UsesStableThreeModeCycle(
        ViewportShadingMode current,
        ViewportShadingMode expected)
    {
        Assert.That(
            ViewportShadingPolicy.Next(current),
            Is.EqualTo(expected));
    }
}
