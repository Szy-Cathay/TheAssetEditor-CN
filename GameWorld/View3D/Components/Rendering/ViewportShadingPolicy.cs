using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Components.Rendering;

internal readonly record struct ViewportShadingPipeline(
    RenderingTechnique SurfaceTechnique,
    FillMode FillMode,
    bool EnableBloom);

internal static class ViewportShadingPolicy
{
    public static ViewportShadingPipeline Resolve(
        ViewportShadingMode mode)
    {
        return mode switch
        {
            ViewportShadingMode.MaterialPreview => new(
                RenderingTechnique.Normal,
                FillMode.Solid,
                true),
            ViewportShadingMode.Solid => new(
                RenderingTechnique.Solid,
                FillMode.Solid,
                false),
            ViewportShadingMode.Wireframe => new(
                RenderingTechnique.Solid,
                FillMode.WireFrame,
                false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                null)
        };
    }

    public static ViewportShadingMode Next(
        ViewportShadingMode current)
    {
        return current switch
        {
            ViewportShadingMode.MaterialPreview =>
                ViewportShadingMode.Wireframe,
            ViewportShadingMode.Wireframe =>
                ViewportShadingMode.Solid,
            ViewportShadingMode.Solid =>
                ViewportShadingMode.MaterialPreview,
            _ => throw new ArgumentOutOfRangeException(
                nameof(current),
                current,
                null)
        };
    }
}
