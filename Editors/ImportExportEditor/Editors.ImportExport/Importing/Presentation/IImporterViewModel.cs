using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Editors.ImportExport.Importing;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;

namespace Editors.ImportExport.Importing.Presentation
{
    public interface IImporterViewModel
    {
        public string DisplayName { get; }
        string OutputExtension { get; }
        string[] InputExtensions { get; } // ADDed THIS!
        void Initialize(PackFile inputFile) { }
        ImportResult Execute(PackFile exportSource, string outputPath, PackFileContainer packFileContainer, GameTypeEnum gameType);
        ImportResult Execute(
            PackFile exportSource,
            string outputPath,
            PackFileContainer packFileContainer,
            GameTypeEnum gameType,
            IProgress<OperationProgressUpdate>? progress) =>
            Execute(
                exportSource,
                outputPath,
                packFileContainer,
                gameType);
        public ImportSupportEnum CanImportFile(PackFile file);

    }
}
