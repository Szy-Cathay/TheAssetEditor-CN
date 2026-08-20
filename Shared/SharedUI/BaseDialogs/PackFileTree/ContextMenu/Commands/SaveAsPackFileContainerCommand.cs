using System.Windows.Forms;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using System;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class SaveAsPackFileContainerCommand(
        IPackFileService packFileService,
        ApplicationSettingsService applicationSettingsService,
        IFolderProjectPackGenerationGuard packGenerationGuard) : IContextMenuCommand
    {
        public string GetDisplayName(TreeNode node) =>
            LocalizationManager.Instance.Get(
                node.FileOwner is FolderProjectContainer
                    ? "MenuBar.File.GeneratePackAs"
                    : "ContextMenu.SaveAs");
        public bool IsEnabled(TreeNode node) => true;

        public void Execute(TreeNode _selectedNode)
        {
            var saveFileDialog = new SaveFileDialog();
            saveFileDialog.FileName = _selectedNode.FileOwner.Name;
            saveFileDialog.Filter = LocalizationManager.Instance.Get(
                "FolderProject.PackFilter");
            saveFileDialog.DefaultExt = "pack";
            if (saveFileDialog.ShowDialog() != DialogResult.OK)
                return;

            if (!packGenerationGuard.CanGenerate(_selectedNode.FileOwner))
                return;

            using (new WaitCursor())
            {
                try
                {
                    var gameInformation =
                        GameInformationDatabase.GetGameById(
                            applicationSettingsService
                                .CurrentSettings.CurrentGame);
                    packFileService.SavePackContainer(
                        _selectedNode.FileOwner,
                        saveFileDialog.FileName,
                        gameInformation);
                    if (_selectedNode.FileOwner is not
                        FolderProjectContainer)
                    {
                        _selectedNode.UnsavedChanged = false;
                        _selectedNode.ForeachNode(
                            node => node.UnsavedChanged = false);
                    }
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        LocalizationManager.Instance.GetFormat(
                            "Msg.ErrorSavingPack",
                            exception.Message),
                        LocalizationManager.Instance.Get(
                            "Msg.GeneralError"));
                }
            }
        }
    }
}
