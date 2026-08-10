using CommunityToolkit.Mvvm.ComponentModel;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf;
using Editors.ImportExport.Exporting.Presentation.RmvToGltf;
using Editors.ImportExport.Importing.Importers;
using Editors.ImportExport.Importing;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Importing.Presentation;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.Ui.Common.DataTemplates;
using Editors.ImportExport.Importing.Presentation.RmvToGltf;
using Editors.ImportExport.Misc;
using Editors.ImportExport.Common;
using Shared.Core.Services;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Shared.Ui.Common.OperationProgress;

namespace Editors.ImportImport.Importing.Presentation.RmvToGltf
{
    public partial class RmvToGltfImporterViewModel : ObservableObject, IImporterViewModel, IViewProvider<RmvToGltfImporterView>
    {
        private readonly GltfImporter _Importer;

        public string DisplayName => LocalizationManager.Instance.Get("RmvToGltfImporter.DisplayName");
        public string OutputExtension => ".rigid_model_v2";
        public string[] InputExtensions => new string[] { ".gltf", ".glb" };

        [ObservableProperty] bool _importMeshes = true;        
        [ObservableProperty] bool _importMaterials = true;
        [ObservableProperty] bool _convertFromBlenderMaterialMap = true;
        [ObservableProperty] bool _convertNormalTextureToOrange = true;
        [ObservableProperty] bool _importAnimations = true;
        [ObservableProperty] bool _autoScaleHumanoid = true;
        [ObservableProperty]
        GltfSourceForwardDirection _sourceForwardDirection =
            GltfSourceForwardDirection.PositiveZ;
        [ObservableProperty] bool _autoDetectAnimationKeysPerSecond = true;
        [ObservableProperty] float _animationKeysPerSecond = 20.0f;
        [ObservableProperty] string _newSkeletonName = "";

        public bool CanEditAnimationKeysPerSecond =>
            ImportAnimations && !AutoDetectAnimationKeysPerSecond;

        public IReadOnlyList<KeyValuePair<string, GltfSourceForwardDirection>>
            SourceForwardDirections { get; } =
            [
                new(
                    LocalizationManager.Instance.Get(
                        "GltfImporter.SourceForward.PositiveZ"),
                    GltfSourceForwardDirection.PositiveZ),
                new(
                    LocalizationManager.Instance.Get(
                        "GltfImporter.SourceForward.PositiveX"),
                    GltfSourceForwardDirection.PositiveX),
            ];

        public RmvToGltfImporterViewModel(GltfImporter Importer)
        {
            _Importer = Importer;
        }

        public ImportSupportEnum CanImportFile(PackFile file) => _Importer.CanImportFile(file);

        public void Initialize(PackFile inputFile) =>
            NewSkeletonName = GltfSkeletonNameReader.GetDefaultName(inputFile.Name);

        public ImportResult Execute(
            PackFile importSource,
            string outputPath,
            PackFileContainer packFileContainer,
            GameTypeEnum gameType) =>
            Execute(
                importSource,
                outputPath,
                packFileContainer,
                gameType,
                null);

        public ImportResult Execute(
            PackFile importSource,
            string outputPath,
            PackFileContainer packFileContainer,
            GameTypeEnum gameType,
            IProgress<OperationProgressUpdate>? progress)
        {
            if (!float.IsFinite(AnimationKeysPerSecond) || AnimationKeysPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(AnimationKeysPerSecond), "动画采样率必须大于 0。");

            var settings = new GltfImporterSettings(
                InputGltfFile: importSource.Name,
                DestinationPackPath: outputPath,
                DestinationPackFileContainer: packFileContainer,
                SelectedGame: gameType,
                ImportMeshes: this.ImportMeshes,
                ImportMaterials: this.ImportMaterials,
                ConvertMaterialFromBlenderType: this.ConvertFromBlenderMaterialMap,
                ConvertNormalTextureFromBlueToOrangeType: this.ConvertNormalTextureToOrange,
                ImportAnimations: this.ImportAnimations,
                AnimationKeysPerSecond: this.AnimationKeysPerSecond,
                MirrorMesh: true,
                AutoDetectAnimationKeysPerSecond: this.AutoDetectAnimationKeysPerSecond,
                NewSkeletonName: this.NewSkeletonName,
                AutoScaleHumanoid: this.AutoScaleHumanoid,
                SourceForwardDirection: this.SourceForwardDirection);

            return _Importer.Import(settings, progress);
        }

        partial void OnImportAnimationsChanged(bool value) =>
            OnPropertyChanged(nameof(CanEditAnimationKeysPerSecond));

        partial void OnAutoDetectAnimationKeysPerSecondChanged(bool value) =>
            OnPropertyChanged(nameof(CanEditAnimationKeysPerSecond));
    }
}
