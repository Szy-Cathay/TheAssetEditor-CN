using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.ImportExport.Importing;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;

namespace Editors.ImportExport.Importing.Presentation
{
    // Importer
    // --------------------------
    //  Checkbox | Type | Path | Last changed | Need refresh    | Remove Button |
    // --------------------------
    //  Checkbox | Type | Path | Last changed | Need refresh    | Remove Button |
    // --------------------------
    // | Update all Button |
    // => SHow status window after done with OK | Errors


    public partial class ImporterCoreViewModel : ObservableObject
    {
        private readonly ApplicationSettingsService _applicationSettings;

        private readonly IEnumerable<IImporterViewModel> _exporterViewModels;
        PackFile? _inputFile;
        PackFileContainer? _destPackFileContainer;
        string _packPath = "";

        [ObservableProperty] IImporterViewModel? _selectedImporterViewModel;
        [ObservableProperty] ObservableCollection<IImporterViewModel> _possibleImporters = [];
        [ObservableProperty] IImporterViewModel? _selectedImporter;
        [ObservableProperty] string _systemPath = "";
        [ObservableProperty] bool _isOperationActive;

        public ImporterCoreViewModel(IEnumerable<IImporterViewModel> exporterViewModels, ApplicationSettingsService applicationSettings)
        {
            _exporterViewModels = exporterViewModels;
            _applicationSettings = applicationSettings;
        }

        public void Initialize(PackFileContainer packFile, string packPath, string diskFile)
        {
            _destPackFileContainer = packFile;
            _packPath = packPath;
            SystemPath = diskFile;

            _inputFile = new PackFile(SystemPath, new FileSystemSource(SystemPath));
            FindImporter();
        }

        public void FindImporter()        
        {            
            

            if(_inputFile == null)
                throw new ArgumentNullException(nameof(_inputFile), "Fatal Eroor, cannot be null");

            PossibleImporters.Clear();
            SelectedImporter = null;

            foreach (var viewModel in _exporterViewModels)
            {
                var supported = viewModel.CanImportFile(_inputFile);
                if (supported == ImportSupportEnum.NotSupported)
                    continue;

                viewModel.Initialize(_inputFile);
                PossibleImporters.Add(viewModel);                

                if (supported == ImportSupportEnum.HighPriority)
                    SelectedImporter = viewModel;
            }

            if (SelectedImporter == null && PossibleImporters.Count > 0)
                SelectedImporter = PossibleImporters.First();
        }

        public Task<ImportResult> ImportAsync(
            IProgress<OperationProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (SelectedImporter == null || _inputFile == null || _destPackFileContainer == null)
                throw new InvalidOperationException("没有可用于当前文件的导入器。");

            var importer = SelectedImporter;
            return Task.Run(() => importer.Execute(
                _inputFile,
                _packPath,
                _destPackFileContainer,
                _applicationSettings.CurrentSettings.CurrentGame,
                progress,
                cancellationToken),
                cancellationToken);
        }

        [RelayCommand]
        public void BrowsePathCommand()
        {
            int i = 10;
            i = i + 10;
        }
    }
}
