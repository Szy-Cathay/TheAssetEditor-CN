using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;

namespace Editors.ImportExport.Exporting.Exporters
{

    public interface IExporterViewModel
    {
        public string DisplayName { get; }
        string OutputExtension { get; }

        bool Execute(PackFile exportSource, string outputPath);
        public ExportSupportEnum CanExportFile(PackFile file);
    }
}
