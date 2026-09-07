using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.RenderItems;

public sealed class DepthOverrideRenderItem(IRenderItem item, DepthStencilState depthState) : IRenderItem
{
    public bool IncludeInPhotoCapture => false;
    public bool SupportsTechnique(RenderingTechnique technique) => item.SupportsTechnique(technique);

    public void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique)
    {
        var previous = device.DepthStencilState;
        try
        {
            device.DepthStencilState = depthState;
            item.Draw(device, parameters, renderingTechnique);
        }
        finally
        {
            device.DepthStencilState = previous;
        }
    }
}
