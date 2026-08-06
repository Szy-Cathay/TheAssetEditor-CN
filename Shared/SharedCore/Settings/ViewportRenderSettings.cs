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
        float DirectLightRotationY,
        bool FactionColoursEnabled = true,
        string FactionColour0 = ApplicationSettings.DefaultFactionColour0,
        string FactionColour1 = ApplicationSettings.DefaultFactionColour1,
        string FactionColour2 = ApplicationSettings.DefaultFactionColour2)
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
                settings.ViewportDirectLightRotationY,
                settings.ViewportFactionColoursEnabled,
                settings.ViewportFactionColour0,
                settings.ViewportFactionColour1,
                settings.ViewportFactionColour2);
        }
    }
}
