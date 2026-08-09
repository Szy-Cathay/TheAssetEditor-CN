using System.Windows;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;

internal static class PackFileDispatcherWriter
{
    public static IReadOnlyList<string> AddFilesToPackIfNoConflicts(
        IPackFileService packFileService,
        PackFileContainer container,
        List<NewPackFileEntry> files,
        IReadOnlyList<string> targetPaths)
    {
        IReadOnlyList<string> AddFiles()
        {
            var existingPaths = container.FileList.Keys
                .Select(path => path.Replace('/', '\\').Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var conflicts = targetPaths
                .Where(existingPaths.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (conflicts.Count != 0)
                return conflicts;

            try
            {
                packFileService.AddFilesToPack(
                    container,
                    files,
                    overwriteExisting: false);
                return [];
            }
            catch (FolderProjectFileConflictException exception)
            {
                return exception.Paths;
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            return AddFiles();

        return dispatcher.Invoke(AddFiles);
    }
}
