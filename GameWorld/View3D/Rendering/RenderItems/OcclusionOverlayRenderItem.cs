using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.RenderItems;

// Render occluded detail once, then the visible detail at normal strength.
public sealed class OcclusionOverlayRenderItem(IRenderItem item) : IRenderItem
{
    private static readonly DepthStencilState s_occludedDepth = new()
    {
        DepthBufferEnable = true,
        DepthBufferWriteEnable = false,
        DepthBufferFunction = CompareFunction.Greater
    };

    public bool IncludeInPhotoCapture => false;
    public bool SupportsTechnique(RenderingTechnique technique) => item.SupportsTechnique(technique);

    public void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique technique)
    {
        var depth = device.DepthStencilState;
        var blend = device.BlendState;
        try
        {
            device.DepthStencilState = s_occludedDepth;
            item.Draw(device, parameters with { OverlayOpacity = 0.12f, SelectedOverlayOpacity = 0.5f }, technique);
            device.DepthStencilState = DepthStencilState.DepthRead;
            item.Draw(device, parameters, technique);
        }
        finally
        {
            device.DepthStencilState = depth;
            device.BlendState = blend;
        }
    }
}
