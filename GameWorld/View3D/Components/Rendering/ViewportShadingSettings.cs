namespace GameWorld.Core.Components.Rendering;

public enum ViewportSolidLighting { Studio, Clay, Metal }
public enum ViewportEnvironment { Game, Studio, Overcast, Sunset }

// Per-viewport display settings; never applied to material data or photo capture.
public sealed record ViewportShadingSettings
{
    public bool XRay { get; init; }
    public float XRayOpacity { get; init; } = 0.35f;
    public float WireframeOpacity { get; init; } = 1;
    public bool WireframeObjectSelection { get; init; } = true;
    public ViewportSolidLighting SolidLighting { get; init; }
    public float CavityStrength { get; init; }
    public float ShadowStrength { get; init; }
    public bool UseLocalLighting { get; init; }
    public float LightIntensity { get; init; } = 1;
    public float EnvironmentRotation { get; init; } = 20;
    public ViewportEnvironment Environment { get; init; }
}
