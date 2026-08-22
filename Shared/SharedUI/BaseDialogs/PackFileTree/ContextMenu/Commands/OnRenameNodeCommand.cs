using System.Linq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class OnRenameNodeCommand(IPackFileService packFileService, IStandardDialogs standardDialogs) : IContextMenuCommand
    {
        public string GetDisplayName(TreeNode node) => LocalizationManager.Instance.Get("ContextMenu.Rename");
        public bool IsEnabled(TreeNode node) => true;

        public void Execute(TreeNode _selectedNode)
        {
            var FileOwner = _selectedNode.FileOwner;
            if (_selectedNode.NodeType == NodeType.Root &&
                FileOwner is FolderProjectContainer project)
            {
                var result = standardDialogs.ShowTextInputDialog(
                    LocalizationManager.Instance.Get(
                        "FolderProject.RenameProject.Title"),
                    project.Name);
                var newProjectName = result.Text.Trim();
                if (!result.Result ||
                    string.IsNullOrWhiteSpace(newProjectName) ||
                    string.Equals(
                        newProjectName,
                        project.Name,
                        System.StringComparison.Ordinal))
                {
                    return;
                }

                try
                {
                    project.SetProjectDisplayName(newProjectName);
                    _selectedNode.Name = project.Name;
                    _selectedNode.UnsavedChanged = true;
                }
                catch (System.Exception exception)
                {
                    standardDialogs.ShowExceptionWindow(
                        exception,
                        LocalizationManager.Instance.Get(
                            "FolderProject.RenameProject.Failed"));
                }
                return;
            }

            if (FileOwner.IsCaPackFile)
            {
                standardDialogs.ShowDialogBox(LocalizationManager.Instance.Get("Msg.UnableToEditPackfile"), LocalizationManager.Instance.Get("Msg.GeneralError"));
                return;
            }

            if (_selectedNode.NodeType == NodeType.Directory)
            {
                var currentPath = _selectedNode.GetFullPath();
                var newFolderName = EditFileNameDialog.ShowDialog(_selectedNode.Parent, _selectedNode.Name);
                if (newFolderName.Any())
                {
                    if (_selectedNode.FileOwner is FolderProjectContainer)
                    {
                        try
                        {
                            packFileService.RenameDirectory(
                                _selectedNode.FileOwner,
                                currentPath,
                                newFolderName);
                            _selectedNode.Name = newFolderName;
                        }
                        catch
                        {
                            standardDialogs.ShowDialogBox(
                                LocalizationManager.Instance.Get(
                                    "FolderProject.Rename.Failed"),
                                LocalizationManager.Instance.Get(
                                    "FolderProject.ErrorTitle"));
                        }
                        return;
                    }

                    _selectedNode.Name = newFolderName;
                    packFileService.RenameDirectory(_selectedNode.FileOwner, currentPath, newFolderName);
                }

            }
            else if (_selectedNode.NodeType == NodeType.File)
            {
                var newFileName = EditFileNameDialog.ShowDialog(_selectedNode.Parent, _selectedNode.Name);
                if (newFileName.Any())
                    packFileService.RenameFile(_selectedNode.FileOwner, _selectedNode.Item, newFileName);

            }
        }
    }
}
