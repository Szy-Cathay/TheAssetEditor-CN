using System.Windows;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;

internal static class PackFileDispatcherWriter
{
    public static void AddFilesToPack(
        IPackFileService packFileService,
        PackFileContainer container,
        List<NewPackFileEntry> files)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            packFileService.AddFilesToPack(container, files);
            return;
        }

        dispatcher.Invoke(() => packFileService.AddFilesToPack(
            container,
            files));
    }
}
