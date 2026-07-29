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
        float ViewportWidth = 0
        );

}
