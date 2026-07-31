using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class ImportFileCommand(IPackFileService packFileService) : IContextMenuCommand
    {
        public string GetDisplayName(TreeNode node) => LocalizationManager.Instance.Get("ContextMenu.ImportFile");
        public bool IsEnabled(TreeNode node) => true;

        public void Execute(TreeNode _selectedNode)
        {
            if (_selectedNode.FileOwner.IsCaPackFile)
            {
                System.Windows.MessageBox.Show(LocalizationManager.Instance.Get("Msg.UnableToEditPackfile"));
                return;
            }

            var dialog = new OpenFileDialog()
            {
                Multiselect = true,
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var parentPath = _selectedNode.GetFullPath();
                var files = dialog.FileNames;
                var items = new List<NewPackFileEntry>();
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var packFile = new PackFile(fileName, new MemorySource(File.ReadAllBytes(file)));
                    items.Add(new NewPackFileEntry(parentPath, packFile));
                }

                if (!FolderProjectImportSafety.TryApproveOverwrite(
                        _selectedNode.FileOwner,
                        items,
                        out var overwriteExisting))
                {
                    return;
                }

                packFileService.AddFilesToPack(
                    _selectedNode.FileOwner,
                    items,
                    overwriteExisting);
            }
        }
    }



}
