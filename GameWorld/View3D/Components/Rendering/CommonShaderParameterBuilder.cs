using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Components.Rendering
{
    internal static class CommonShaderParameterBuilder
    {
        // Pre-allocated array to avoid per-frame allocation
        private static readonly Vector3[] _cachedFactionColours = new Vector3[3];

        public static CommonShaderParameters Build(ArcBallCamera camera, SceneRenderParametersStore sceneLightParameters, float viewportHeight = 0, float viewportWidth = 0)
        {
            // Reuse the cached array (rendering is single-threaded)
            _cachedFactionColours[0] = sceneLightParameters.FactionColour0;
            _cachedFactionColours[1] = sceneLightParameters.FactionColour1;
            _cachedFactionColours[2] = sceneLightParameters.FactionColour2;

            var commonShaderParameters = new CommonShaderParameters(
                 camera.ViewMatrix,
                camera.ProjectionMatrix,
                camera.Position,
                camera.LookAt,

                sceneLightParameters.EnvLightRotationRadians_Y,
                sceneLightParameters.DirLightRotationRadians_X,
                sceneLightParameters.DirLightRotationRadians_Y,
                sceneLightParameters.LightIntensityMult,
                sceneLightParameters.LightColour,

                _cachedFactionColours,
                viewportHeight,
                viewportWidth,
                sceneLightParameters.FactionColoursEnabled);

            return commonShaderParameters;
        }
    }
}
