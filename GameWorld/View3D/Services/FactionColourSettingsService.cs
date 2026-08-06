using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Settings;

namespace GameWorld.Core.Services
{
    public sealed record FactionColourSettings(
        bool Enabled,
        string Colour0,
        string Colour1,
        string Colour2);

    public sealed class FactionColourSettingsService
    {
        private readonly ApplicationSettingsService _settingsService;
        private readonly IGlobalEventHub _eventHub;

        public FactionColourSettingsService(
            ApplicationSettingsService settingsService,
            IGlobalEventHub eventHub)
        {
            _settingsService = settingsService;
            _eventHub = eventHub;
        }

        public FactionColourSettings Current
        {
            get
            {
                var settings = _settingsService.CurrentSettings;
                return new FactionColourSettings(
                    settings.ViewportFactionColoursEnabled,
                    settings.ViewportFactionColour0,
                    settings.ViewportFactionColour1,
                    settings.ViewportFactionColour2);
            }
        }

        public void Preview(FactionColourSettings factionSettings)
        {
            var settings = ViewportRenderSettings.From(
                _settingsService.CurrentSettings) with
            {
                FactionColoursEnabled = factionSettings.Enabled,
                FactionColour0 = factionSettings.Colour0,
                FactionColour1 = factionSettings.Colour1,
                FactionColour2 = factionSettings.Colour2
            };
            _eventHub.PublishGlobalEvent(
                new ViewportRenderSettingsChangedEvent(settings));
        }

        public void Save(FactionColourSettings factionSettings)
        {
            var settings = _settingsService.CurrentSettings;
            settings.ViewportFactionColoursEnabled =
                factionSettings.Enabled;
            settings.ViewportFactionColour0 = factionSettings.Colour0;
            settings.ViewportFactionColour1 = factionSettings.Colour1;
            settings.ViewportFactionColour2 = factionSettings.Colour2;
            _settingsService.Save();
            Preview(factionSettings);
        }

        public static string ToRgbString(Vector3 colour)
        {
            var converted = new Color(colour);
            return $"{converted.R},{converted.G},{converted.B}";
        }
    }
}
