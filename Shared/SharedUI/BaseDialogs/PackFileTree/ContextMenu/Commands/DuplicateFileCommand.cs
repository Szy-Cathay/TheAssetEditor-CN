using System.IO;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class DuplicateFileCommand(IPackFileService packFileService) : IContextMenuCommand
    {
        public string GetDisplayName(TreeNode node) => LocalizationManager.Instance.Get("ContextMenu.Duplicate");
        public bool IsEnabled(TreeNode node) => true;

        public void Execute(TreeNode _selectedNode) => Execute(_selectedNode.Item);

        public void Execute(PackFile item)
        {
            var fileName = item.Name;
            var extension = "";
            if (Path.HasExtension(item.Name) == true)
            {
                var index = item.Name.IndexOf('.');
                fileName = item.Name.Substring(0, index);
                extension = item.Name.Substring(index);
            }
            ReadAndSave(fileName, extension, item);
        }

        private void ReadAndSave(
            string fileName,
            string extension,
            PackFile item)
        {
            var bytes = item.DataSource.ReadData();
            var parentPath = packFileService.GetFullPath(item);
            var path = Path.GetDirectoryName(parentPath);
            var editablePack = packFileService.GetEditablePack();
            var newName = fileName + "_copy" + extension;
            var suffix = 2;
            while (packFileService.FindFile(
                       Path.Combine(path ?? string.Empty, newName),
                       editablePack) != null)
            {
                newName = $"{fileName}_copy_{suffix}{extension}";
                suffix++;
            }

            var packFile = new PackFile(newName, new MemorySource(bytes));
            var fileEntry = new NewPackFileEntry(path, packFile);
            packFileService.AddFilesToPack(
                editablePack,
                [fileEntry],
                overwriteExisting: false);
        }
    }


}
//_uiCommandFactory.Create<DuplicateFileCommand>().Execute(_selectedNode.Item);
