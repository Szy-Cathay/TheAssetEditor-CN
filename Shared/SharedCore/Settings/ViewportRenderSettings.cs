namespace Shared.Core.Settings
{
    public sealed record ViewportRenderSettings(
        BackgroundColour BackgroundColour,
        string CustomBackgroundColour,
        bool SimulateGameBackfaces,
        bool ShowGrid,
        string GridColour,
        float LightIntensity,
        float EnvironmentLightRotationY,
        float DirectLightRotationX,
        float DirectLightRotationY)
    {
        public static ViewportRenderSettings From(
            ApplicationSettings settings)
        {
            return new ViewportRenderSettings(
                settings.RenderEngineBackgroundColour,
                settings.CustomBackgroundColour,
                settings.SimulateGameBackfaces,
                settings.ShowViewportGrid,
                settings.ViewportGridColour,
                settings.ViewportLightIntensity,
                settings.ViewportEnvironmentLightRotationY,
                settings.ViewportDirectLightRotationX,
                settings.ViewportDirectLightRotationY);
        }
    }
}
