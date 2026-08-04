using System.Linq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common;
namespace AssetEditor.UiCommands
{
    internal class OpenGamePackCommand : IUiCommand
    {
        private readonly IPackFileService _packFileService;
        private readonly IPackFileContainerLoader _packFileContainerLoader;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly IStandardDialogs _standardDialogs;

        public OpenGamePackCommand(
            IPackFileService packFileService,
            IPackFileContainerLoader packFileContainerLoader,
            ApplicationSettingsService applicationSettingsService,
            IStandardDialogs standardDialogs)
        {
            _packFileService = packFileService;
            _packFileContainerLoader = packFileContainerLoader;
            _applicationSettingsService = applicationSettingsService;
            _standardDialogs = standardDialogs;
        }

        public void Execute(GameTypeEnum game)
        {
            var settingsService = _applicationSettingsService;
            var settings = settingsService.CurrentSettings;
            var gamePath = settings.GameDirectories.FirstOrDefault(x => x.Game == game);

            if (gamePath == null || string.IsNullOrWhiteSpace(gamePath.Path))
            {
                _standardDialogs.ShowDialogBox(LocalizationManager.Instance.Get("Msg.NoGamePath"));
                return;
            }

            var packFileContainer = _packFileService.GetAllPackfileContainers();
            foreach (var packFile in packFileContainer)
            {
                if (packFile.SystemFilePath == gamePath.Path)
                {
                    _standardDialogs.ShowDialogBox(
                        LocalizationManager.Instance.GetFormat(
                            "Msg.PackFilesAlreadyLoaded",
                            GameInformationDatabase.GetGameById(game).DisplayName),
                        LocalizationManager.Instance.Get("Msg.GeneralError"));
                    return;
                }
            }

            using (new WaitCursor())
            {
                var res = _packFileContainerLoader.LoadAllCaFiles(game);
                _packFileService.AddContainer(res);
            }
        }
    }
}
