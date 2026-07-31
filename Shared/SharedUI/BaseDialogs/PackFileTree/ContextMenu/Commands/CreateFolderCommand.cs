using System.Linq;
using System.Windows;
using System.IO;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class CreateFolderCommand(IPackFileService packFileService) : IContextMenuCommand
    {
        public string GetDisplayName(TreeNode node) => LocalizationManager.Instance.Get("ContextMenu.CreateFolder");
        public bool IsEnabled(TreeNode node) => true;

        public void Execute(TreeNode _selectedNode)
        {
            if (_selectedNode.FileOwner.IsCaPackFile)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.UnableToEditPackfile"));
                return;
            }

            var folderName = EditFileNameDialog.ShowDialog(_selectedNode, "");

            if (folderName.Any())
            {
                var parentPath = _selectedNode.GetFullPath();
                var folderPath = string.IsNullOrEmpty(parentPath)
                    ? folderName
                    : Path.Combine(parentPath, folderName);
                packFileService.CreateFolder(
                    _selectedNode.FileOwner,
                    folderPath);
                _selectedNode.Children.Add(new TreeNode(folderName, NodeType.Directory, _selectedNode.FileOwner, _selectedNode));
            }
        }
    }
}
