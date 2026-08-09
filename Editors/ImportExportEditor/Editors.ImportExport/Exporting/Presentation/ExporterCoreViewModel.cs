using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;

namespace Editors.ImportExport.Exporting.Presentation
{
    // Importer
    // --------------------------
    //  Checkbox | Type | Path | Last changed | Need refresh    | Remove Button |
    // --------------------------
    //  Checkbox | Type | Path | Last changed | Need refresh    | Remove Button |
    // --------------------------
    // | Update all Button |
    // => SHow status window after done with OK | Errors


    public partial class ExporterCoreViewModel : ObservableObject
    {
        private readonly IEnumerable<IExporterViewModel> _exporterViewModels;
        PackFile? _inputFile;

        [ObservableProperty] IExporterViewModel? _selectedExporterViewModel;
        [ObservableProperty] ObservableCollection<IExporterViewModel> _possibleExporters = [];
        [ObservableProperty] IExporterViewModel? _selectedExporter;
        [ObservableProperty] string _systemPath = "";
        [ObservableProperty] bool _isOperationActive;

        public ExporterCoreViewModel(IEnumerable<IExporterViewModel> exporterViewModels)
        {
            _exporterViewModels = exporterViewModels;
        }

        public void Initialize(PackFile packFile)
        {
            _inputFile = packFile;
            foreach (var viewModel in _exporterViewModels)
            {
                var supported = viewModel.CanExportFile(packFile);
                if (supported == ExportSupportEnum.NotSupported)
                    continue;

                PossibleExporters.Add(viewModel);
                if(supported == ExportSupportEnum.HighPriority)
                    SelectedExporter = viewModel;
            }

            if (SelectedExporter == null && PossibleExporters.Count > 0)
                SelectedExporter = PossibleExporters.First();
        }

        public Task<bool> ExportAsync()
        {
            if (SelectedExporter == null || _inputFile == null)
                throw new InvalidOperationException("没有可用于当前文件的导出器。");

            return Task.Run(() => SelectedExporter.Execute(_inputFile, SystemPath));
        }

        [RelayCommand]
        public void BrowsePathCommand()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(_inputFile!.Name),
                DefaultExt = SelectedExporter!.OutputExtension,
                Filter = $"File ({SelectedExporter!.OutputExtension})|*{SelectedExporter!.OutputExtension}"
            };

            if (dlg.ShowDialog() == true)
                SystemPath = dlg.FileName;
        }
    }
}
