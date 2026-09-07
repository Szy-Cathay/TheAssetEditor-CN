using Microsoft.Xna.Framework;

namespace GameWorld.Core.Rendering
{
    // record struct: stack-allocated, eliminates per-frame heap allocation
    public readonly record struct CommonShaderParameters(
        Matrix View,
        Matrix Projection,
        Vector3 CameraPosition,
        Vector3 CameraLookAt,
        float EnvLightRotationsRadians_Y,
        float DirLightRotationRadians_X,
        float DirLightRotationRadians_Y,
        float LightIntensityMult,
        Vector3 LightColour,
        Vector3[] FactionColours,
        float ViewportHeight = 0,
        float ViewportWidth = 0,
        bool FactionColoursEnabled = true,
        float SurfaceOpacity = 1,
        float OverlayOpacity = 1,
        float SelectedOverlayOpacity = 1,
        Components.Rendering.ViewportShadingSettings? ViewportShading = null,
        bool ViewportWireframe = false,
        Microsoft.Xna.Framework.Graphics.Texture2D? ViewportMatcap = null,
        Microsoft.Xna.Framework.Graphics.Texture2D? ViewportGeometry = null,
        Microsoft.Xna.Framework.Graphics.TextureCube? ViewportDiffuse = null,
        Microsoft.Xna.Framework.Graphics.TextureCube? ViewportSpecular = null
        );

}
