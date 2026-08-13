using System;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;

namespace AssetEditor.Services
{
    class RecentFilesTracker : IDisposable
    {
        private readonly IGlobalEventHub _globalEventHub;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly Action _saveSettings;

        public RecentFilesTracker(
            IGlobalEventHub globalEventHub,
            ApplicationSettingsService applicationSettingsService)
            : this(
                globalEventHub,
                applicationSettingsService,
                applicationSettingsService.Save)
        {
        }

        internal RecentFilesTracker(
            IGlobalEventHub globalEventHub,
            ApplicationSettingsService applicationSettingsService,
            Action saveSettings)
        {
            _globalEventHub = globalEventHub;
            _applicationSettingsService = applicationSettingsService;
            _saveSettings = saveSettings;
            _globalEventHub.Register<PackFileContainerAddedEvent>(this, Handler);
        }

        private void Handler(PackFileContainerAddedEvent e)
        {
            if (e.Reason ==
                PackFileContainerAddedReason.InternalReattach)
            {
                return;
            }

            if (e.Container.IsCaPackFile)
                return;

            if (string.IsNullOrEmpty(e.Container.SystemFilePath))
                return;

            if (e.Container is FolderProjectContainer)
            {
                _applicationSettingsService
                    .AddRecentlyOpenedFolderProject(
                        e.Container.SystemFilePath);
            }
            else if (e.Container.Role ==
                     PackFileContainerRole.Reference)
            {
                _applicationSettingsService.AddRecentlyOpenedPackFile(
                    e.Container.SystemFilePath);
            }
            else
            {
                return;
            }
            _saveSettings();
        }

        public void Dispose()
        {
            _globalEventHub.UnRegister(this);
        }
    }
}
