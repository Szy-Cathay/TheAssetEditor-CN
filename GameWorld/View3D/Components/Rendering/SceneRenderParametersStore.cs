using Microsoft.Xna.Framework;

using Shared.Core.Settings;

namespace GameWorld.Core.Components.Rendering
{
    public class SceneRenderParametersStore
    {
        private ViewportRenderSettings? _globalLighting;
        private int _lightingOverrideCount;

        public float EnvLightRotationDegrees_Y { get; set; } = 20;
        public float DirLightRotationDegrees_X { get; set; } = 0;
        public float DirLightRotationDegrees_Y { get; set; } = 0;
        public float LightIntensityMult { get; set; } = 1;
        public Vector3 LightColour { get; set; } = Vector3.One;

        // Pre-computed radians (updated with degree properties if needed)
        public float EnvLightRotationRadians_Y => MathHelper.ToRadians(EnvLightRotationDegrees_Y);
        public float DirLightRotationRadians_X => MathHelper.ToRadians(DirLightRotationDegrees_X);
        public float DirLightRotationRadians_Y => MathHelper.ToRadians(DirLightRotationDegrees_Y);

        public Vector3 FactionColour0 { get; set; } = Color.Red.ToVector3();
        public Vector3 FactionColour1 { get; set; } =
            new Color(100, 169, 226).ToVector3();
        public Vector3 FactionColour2 { get; set; } = Color.White.ToVector3();
        public bool FactionColoursEnabled { get; set; } = true;

        public void ApplyGlobalLighting(ViewportRenderSettings settings)
        {
            _globalLighting = settings;
            FactionColoursEnabled = settings.FactionColoursEnabled;
            FactionColour0 = ParseFactionColour(settings.FactionColour0);
            FactionColour1 = ParseFactionColour(settings.FactionColour1);
            FactionColour2 = ParseFactionColour(settings.FactionColour2);
            if (_lightingOverrideCount == 0)
                ApplyLighting(settings);
        }

        public IDisposable BeginLightingOverride()
        {
            _lightingOverrideCount++;
            return new LightingOverride(this);
        }

        private void EndLightingOverride()
        {
            if (_lightingOverrideCount == 0)
                return;

            _lightingOverrideCount--;
            if (_lightingOverrideCount == 0 && _globalLighting != null)
                ApplyLighting(_globalLighting);
        }

        private void ApplyLighting(ViewportRenderSettings settings)
        {
            LightIntensityMult = settings.LightIntensity;
            EnvLightRotationDegrees_Y = settings.EnvironmentLightRotationY;
            DirLightRotationDegrees_X = settings.DirectLightRotationX;
            DirLightRotationDegrees_Y = settings.DirectLightRotationY;
        }

        private static Vector3 ParseFactionColour(string rgb) =>
            ApplicationSettingsHelper.ParseCustomBackgroundColour(rgb)
                .ToVector3();

        private sealed class LightingOverride(
            SceneRenderParametersStore owner) : IDisposable
        {
            private SceneRenderParametersStore? _owner = owner;

            public void Dispose()
            {
                _owner?.EndLightingOverride();
                _owner = null;
            }
        }
    }
}
