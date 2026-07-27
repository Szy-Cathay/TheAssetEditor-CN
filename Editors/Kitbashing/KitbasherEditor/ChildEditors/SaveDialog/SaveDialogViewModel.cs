using System.Collections.ObjectModel;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Geometry;
using GameWorld.Core.Services.SceneSaving.Lod;
using GameWorld.Core.Services.SceneSaving.Material;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace KitbasherEditor.ViewModels.SaveDialog
{
    public partial class SaveDialogViewModel : ObservableObject 
    {
        private readonly SceneManager _sceneManager;
        private readonly SaveService _saveService;
        private readonly IPackFileService _pfs;
        private readonly IStandardDialogs _packFileUiProvider;
        private GeometrySaveSettings? _saveSettings;
        private List<LodGenerationSettings> _draftLodSettings = [];
        private bool _isInitializing;

        [ObservableProperty] ObservableCollection<LodGroupNodeViewModel> _lodNodes = [];
        [ObservableProperty] List<ComboBoxItem<GeometryStrategy>> _meshStrategies;
        [ObservableProperty] List<ComboBoxItem<MaterialStrategy>> _wsStrategies;
        [ObservableProperty] List<ComboBoxItem<LodStrategy>> _lodStrategies;
        [ObservableProperty] List<int> _possibleLodNumbers  = [1,2,3,4,5];

        [ObservableProperty] string _outputPath;
        [ObservableProperty] ComboBoxItem<GeometryStrategy> _selectedMeshStrategy;
        [ObservableProperty] ComboBoxItem<MaterialStrategy> _selectedWsModelStrategy;
        [ObservableProperty] ComboBoxItem<LodStrategy> _selectedLodStrategy;
        [ObservableProperty] bool _onlySaveVisible = false;
        [ObservableProperty] int _numberOfLodsToGenerate;

        public SaveDialogViewModel(SceneManager sceneManager, SaveService saveService, IPackFileService pfs, IStandardDialogs packFileUiProvider)
        {
            _sceneManager = sceneManager;
            _saveService = saveService;
            _pfs = pfs;
            _packFileUiProvider = packFileUiProvider;
            MeshStrategies = _saveService.GetGeometryStrategies().Select(x => new ComboBoxItem<GeometryStrategy>(x.StrategyId, x.Name, x.Description)).ToList();
            WsStrategies = _saveService.GetMaterialStrategies().Select(x => new ComboBoxItem<MaterialStrategy>(x.StrategyId, x.Name, x.Description)).ToList();
            LodStrategies = _saveService.GetLodStrategies().Select(x => new ComboBoxItem<LodStrategy>(x.StrategyId, x.Name, x.Description)).ToList();

            OutputPath = "";
            SelectedMeshStrategy = MeshStrategies.First();
            SelectedWsModelStrategy = WsStrategies.First();
            SelectedLodStrategy = LodStrategies.First();
        }

        internal void Initialize(GeometrySaveSettings saveSettings)
        {
            _saveSettings = saveSettings;
            _draftLodSettings = saveSettings.LodSettingsPerLod
                .Select(CloneLodSettings)
                .ToList();
            ResizeDraftLodSettings(saveSettings.NumberOfLodsToGenerate);

            _isInitializing = true;
            OutputPath = _saveSettings.OutputName;
            SelectedMeshStrategy= MeshStrategies.First(x => x.Value == _saveSettings.GeometryOutputType);
            SelectedWsModelStrategy= WsStrategies.First(x => x.Value == _saveSettings.MaterialOutputType);
            SelectedLodStrategy = LodStrategies.First(x => x.Value == _saveSettings.LodGenerationMethod);
            OnlySaveVisible = _saveSettings.OnlySaveVisible;
            NumberOfLodsToGenerate = _saveSettings.NumberOfLodsToGenerate;
            _isInitializing = false;

            BuildLodOverview();
        }

        partial void OnNumberOfLodsToGenerateChanged(int value) 
        {
            if (_isInitializing || _saveSettings == null)
                return;

            ResizeDraftLodSettings(value);
            BuildLodOverview();
        }

        partial void OnOnlySaveVisibleChanged(bool value)
        {
            if (_isInitializing || _saveSettings == null)
                return;

            BuildLodOverview();
        }

        void BuildLodOverview()
        {
            LodNodes.Clear();
            var lodNodesInModel = _sceneManager
                .GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)
                .GetLodNodes();

            for (var i = 0; i < NumberOfLodsToGenerate; i++)
            {
                Rmv2LodNode? lodNode = null;
                if(i < lodNodesInModel.Count)
                    lodNode = lodNodesInModel[i];
                LodNodes.Add(new LodGroupNodeViewModel(lodNode, i, _draftLodSettings[i], OnlySaveVisible));
            }
        }
      
        public void ApplySettings()
        {
            Guard.IsNotNull(_saveSettings);
            _saveSettings.OutputName = OutputPath;
            _saveSettings.OnlySaveVisible = OnlySaveVisible;
            _saveSettings.GeometryOutputType = SelectedMeshStrategy.Value;
            _saveSettings.MaterialOutputType = SelectedWsModelStrategy.Value;
            _saveSettings.LodGenerationMethod = SelectedLodStrategy.Value;
            _saveSettings.NumberOfLodsToGenerate = NumberOfLodsToGenerate;
            _saveSettings.LodSettingsPerLod = _draftLodSettings
                .Select(CloneLodSettings)
                .ToList();
            _saveSettings.IsUserInitialized = true;
        }

        void ResizeDraftLodSettings(int lodCount)
        {
            var resizedSettings = GeometrySaveSettings.CreateDefaultLodSettings(lodCount);
            var settingsToKeep = Math.Min(_draftLodSettings.Count, resizedSettings.Count);
            for (var index = 0; index < settingsToKeep; index++)
                resizedSettings[index] = _draftLodSettings[index];
            _draftLodSettings = resizedSettings;
        }

        static LodGenerationSettings CloneLodSettings(LodGenerationSettings source)
        {
            return new LodGenerationSettings
            {
                LodRectionFactor = source.LodRectionFactor,
                OptimizeAlpha = source.OptimizeAlpha,
                OptimizeVertex = source.OptimizeVertex,
                QualityLvl = source.QualityLvl,
                CameraDistance = source.CameraDistance
            };
        }

        [RelayCommand]
        void HandleBrowseLocation()
        {
            var extension = ".rigid_model_v2";
            var dialogResult = _packFileUiProvider.DisplaySaveDialog(_pfs, [extension]);

            if (dialogResult.Result == true)
            {
                var path = dialogResult.SelectedFilePath!;
                if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    path += extension;

                OutputPath = path;
            }
        }
    }
}
