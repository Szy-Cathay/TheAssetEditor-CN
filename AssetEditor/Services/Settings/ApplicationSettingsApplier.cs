using System;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Settings;

namespace AssetEditor.Services.Settings
{
    internal sealed record ApplicationSettingsApplyResult(
        bool RequiresApplicationRestart,
        bool RequiresModelReload);

    internal sealed class ApplicationSettingsApplier
    {
        private readonly ApplicationSettingsService _settingsService;
        private readonly IGlobalEventHub _globalEventHub;
        private readonly GameTypeEnum _originalGame;
        private readonly string? _originalGamePath;
        private readonly bool _originalShowCaWemFiles;
        private readonly bool _originalOnlyLoadLod0;
        private readonly ViewportRenderSettings _originalViewportSettings;

        public ApplicationSettingsApplier(
            ApplicationSettingsService settingsService,
            IGlobalEventHub globalEventHub)
        {
            _settingsService = settingsService;
            _globalEventHub = globalEventHub;

            var settings = settingsService.CurrentSettings;
            _originalGame = settings.CurrentGame;
            _originalGamePath = settingsService.GetGamePathForCurrentGame();
            _originalShowCaWemFiles = settings.ShowCAWemFiles;
            _originalOnlyLoadLod0 =
                settings.OnlyLoadLod0ForReferenceMeshes;
            _originalViewportSettings =
                ViewportRenderSettings.From(settings);
        }

        public void PreviewViewport(ViewportRenderSettings settings)
        {
            _globalEventHub.PublishGlobalEvent(
                new ViewportRenderSettingsChangedEvent(settings));
        }

        public void RestoreViewportPreview()
        {
            PreviewViewport(_originalViewportSettings);
        }

        public ApplicationSettingsApplyResult CompleteSave()
        {
            var settings = _settingsService.CurrentSettings;
            var viewportSettings = ViewportRenderSettings.From(settings);
            if (viewportSettings != _originalViewportSettings)
                PreviewViewport(viewportSettings);

            if (settings.ShowCAWemFiles != _originalShowCaWemFiles)
            {
                _globalEventHub.PublishGlobalEvent(
                    new ShowCaWemFilesChangedEvent(
                        settings.ShowCAWemFiles));
            }

            var requiresRestart =
                settings.CurrentGame != _originalGame ||
                !string.Equals(
                    _settingsService.GetGamePathForCurrentGame() ?? "",
                    _originalGamePath ?? "",
                    StringComparison.OrdinalIgnoreCase);
            var requiresModelReload =
                settings.OnlyLoadLod0ForReferenceMeshes !=
                _originalOnlyLoadLod0;

            _settingsService.Save();
            return new ApplicationSettingsApplyResult(
                requiresRestart,
                requiresModelReload);
        }
    }
}
