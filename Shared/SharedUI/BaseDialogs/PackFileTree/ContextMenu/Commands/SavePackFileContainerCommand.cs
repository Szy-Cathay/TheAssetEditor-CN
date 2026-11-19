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
        ApplicationSettingsService applicationSettingsService,
        IFolderProjectPackGenerationGuard packGenerationGuard) : IContextMenuCommand
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
            var systemPath = SelectOutputPath(_selectedNode.FileOwner);
            if (systemPath == null) return;

            if (!packGenerationGuard.CanGenerate(_selectedNode.FileOwner))
                return;

            using (new WaitCursor())
            {
                try
                {
                    var gameInformation = GameInformationDatabase.GetGameById(applicationSettingsService.CurrentSettings.CurrentGame);
                    packFileService.SavePackContainer(
                        _selectedNode.FileOwner,
                        systemPath,
                        gameInformation);
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

            var systemPath = SelectOutputPath(pack);
            if (systemPath == null) return;

            if (!packGenerationGuard.CanGenerate(pack))
                return;

            using (new WaitCursor())
            {
                try
                {
                    var gameInformation = GameInformationDatabase.GetGameById(applicationSettingsService.CurrentSettings.CurrentGame);
                    packFileService.SavePackContainer(
                        pack,
                        systemPath,
                        gameInformation);
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

        public static string? SelectOutputPath(PackFileContainer container)
        {
            var configured = GetSavePath(container);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            using var dialog = new SaveFileDialog { FileName = container.Name, DefaultExt = "pack", Filter = LocalizationManager.Instance.Get("FolderProject.PackFilter") };
            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        }

        private static string? GetSavePath(PackFileContainer container)
        {
            return container is FolderProjectContainer folderProject
                ? folderProject.ProjectSettings.OutputPackPath
                : container.SystemFilePath;
        }
    }
}
