using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Components.Rendering
{
    public enum RenderBuckedId
    {
        Outline,
        Normal,
        Wireframe,
        Selection,
        Font,
    }

    public enum RenderingTechnique
    {
        Normal,
        Solid,
        Emissive,
    }

    enum RasterizerStateEnum
    {
        Normal,
        Wireframe,
        SelectedFaces,
        Outline,
    }

    public interface IRenderItem
    {
        void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique);
        bool SupportsTechnique(RenderingTechnique technique) => false;
        bool IncludeInPhotoCapture => true;
    }
}
