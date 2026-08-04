using System;
using System.Windows.Forms;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class SavePackFileContainerCommand(
        IPackFileService packFileService,
        IStandardDialogs standardDialogs,
        ApplicationSettingsService applicationSettingsService) : IContextMenuCommand
    {
        private readonly ILogger _logger = Logging.Create<SavePackFileContainerCommand>();
        public string GetDisplayName(TreeNode node) =>
            LocalizationManager.Instance.Get(
                node.FileOwner is FolderProjectContainer
                    ? "MenuBar.File.GeneratePack"
                    : "ContextMenu.Save");
        public bool IsEnabled(TreeNode node) => true;

        public void Execute(TreeNode _selectedNode)
        {
            var systemPath = GetSavePath(_selectedNode.FileOwner);
            if (string.IsNullOrWhiteSpace(systemPath))
            {
                var saveFileDialog = new SaveFileDialog();
                saveFileDialog.FileName = _selectedNode.FileOwner.Name;
                saveFileDialog.Filter = LocalizationManager.Instance.Get(
                    "FolderProject.PackFilter");
                saveFileDialog.DefaultExt = "pack";
                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                    return;
                systemPath = saveFileDialog.FileName;
            }

            using (new WaitCursor())
            {
                try
                {
                    var gameInformation = GameInformationDatabase.GetGameById(applicationSettingsService.CurrentSettings.CurrentGame);
                    packFileService.SavePackContainer(_selectedNode.FileOwner, systemPath, false, gameInformation);
                }
                catch (Exception e)
                {
                    _logger.Here().Error(e, "Exception while saving");
                    standardDialogs.ShowDialogBox(
                        LocalizationManager.Instance.GetFormat(
                            "Msg.ErrorSavingPack",
                            e.Message),
                        LocalizationManager.Instance.Get("Msg.GeneralError"));
                }
            }
        }

        public void Execute()
        {
            var pack = packFileService.GetEditablePack();
            if (pack == null)
            {
                standardDialogs.ShowDialogBox(LocalizationManager.Instance.Get("Msg.NoEditablePack"), LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            var systemPath = GetSavePath(pack);
            if (string.IsNullOrWhiteSpace(systemPath))
            {
                var saveFileDialog = new SaveFileDialog();
                saveFileDialog.FileName = pack.Name;
                saveFileDialog.Filter = LocalizationManager.Instance.Get(
                    "FolderProject.PackFilter");
                saveFileDialog.DefaultExt = "pack";
                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                    return;
                systemPath = saveFileDialog.FileName;
            }

            using (new WaitCursor())
            {
                try
                {
                    var gameInformation = GameInformationDatabase.GetGameById(applicationSettingsService.CurrentSettings.CurrentGame);
                    packFileService.SavePackContainer(pack, systemPath, false, gameInformation);
                }
                catch (Exception e)
                {
                    _logger.Here().Error(e, "Exception while saving");
                    standardDialogs.ShowDialogBox(
                        LocalizationManager.Instance.GetFormat(
                            "Msg.ErrorSavingPack",
                            e.Message),
                        LocalizationManager.Instance.Get("Msg.GeneralError"));
                }
            }
        }

        private static string? GetSavePath(PackFileContainer container)
        {
            return container is FolderProjectContainer folderProject
                ? folderProject.ProjectSettings.OutputPackPath
                : container.SystemFilePath;
        }
    }
}
