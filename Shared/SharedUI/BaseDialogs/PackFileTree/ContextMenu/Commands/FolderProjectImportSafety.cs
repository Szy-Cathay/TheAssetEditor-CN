using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    internal static class FolderProjectImportSafety
    {
        public static bool TryApproveOverwrite(
            PackFileContainer owner,
            IReadOnlyList<NewPackFileEntry> files,
            out bool overwriteExisting)
        {
            overwriteExisting = true;
            if (owner is not FolderProjectContainer folderProject)
                return true;

            var existingCount = files.Count(
                entry =>
                {
                    var relativePath =
                        FolderProjectPathPolicy.NormalizeRelativePath(
                            Path.Combine(
                                entry.DirectoyPath ?? "",
                                entry.PackFile.Name.Trim()));
                    return folderProject.FileList.ContainsKey(
                        relativePath.ToLowerInvariant());
                });
            overwriteExisting = existingCount > 0;
            if (existingCount == 0)
                return true;

            return System.Windows.MessageBox.Show(
                       LocalizationManager.Instance.GetFormat(
                           "FolderProject.Import.ConfirmOverwrite",
                           existingCount),
                       LocalizationManager.Instance.Get(
                           "FolderProject.Import.OverwriteTitle"),
                       System.Windows.MessageBoxButton.YesNo,
                       System.Windows.MessageBoxImage.Warning) ==
                   System.Windows.MessageBoxResult.Yes;
        }
    }
}
