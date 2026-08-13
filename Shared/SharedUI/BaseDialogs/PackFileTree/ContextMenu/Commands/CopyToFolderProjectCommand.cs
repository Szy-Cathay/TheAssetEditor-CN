using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.Common;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class CopyToFolderProjectCommand(
        IPackFileService packFileService,
        IStandardDialogs standardDialogs) : IContextMenuCommand
    {
        public string GetDisplayName(TreeNode node) =>
            LocalizationManager.Instance.Get(
                packFileService.GetEditablePack() is
                    FolderProjectContainer
                    ? "ContextMenu.CopyToProject"
                    : "ContextMenu.CopyToProjectUnavailable");
        public bool IsEnabled(TreeNode node) =>
            packFileService.GetEditablePack() is
                FolderProjectContainer;

        public void Execute(TreeNode _selectedNode)
        {
            if (packFileService.GetEditablePack() is not
                FolderProjectContainer project)
            {
                standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get(
                        "Msg.NoEditableFolderProject"),
                    LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            using (new WaitCursor())
            {
                var files = _selectedNode.GetAllChildFileNodes();
                foreach (var file in files)
                    packFileService.CopyFileFromOtherPackFile(
                        file.FileOwner,
                        file.GetFullPath(),
                        project);
            }
        }
    }
}
