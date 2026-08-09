using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.ImportExport.Common;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf;
using Editors.ImportExport.Misc;
using GameWorld.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.Ui.Common.DataTemplates;

namespace Editors.ImportExport.Exporting.Presentation.RmvToGltf;

internal partial class RmvToGltfExporterViewModel :
    ObservableObject,
    IExporterViewModel,
    IViewProvider<RmvToGltfExporterView>
{
    private readonly RmvToGltfExporter _exporter;
    private readonly IPackFileService _packFileService;
    private readonly ISkeletonAnimationLookUpHelper _skeletonLookUpHelper;
    private readonly ApplicationSettingsService _applicationSettings;
    private PackFile? _currentExportSource;

    public string DisplayName => LocalizationManager.Instance.Get("RmvToGltfExporter.DisplayName");
    public string OutputExtension => ".gltf";
    public ObservableCollection<ExportAnimationOption> AvailableAnimations { get; } = [];
    public ObservableCollection<ExportAnimationOption> AnimationFiles { get; } = [];

    [ObservableProperty] private bool _exportTextures = true;
    [ObservableProperty] private bool _convertMaterialTextureToBlender = true;
    [ObservableProperty] private bool _convertNormalTextureToBlue = true;
    [ObservableProperty] private bool _hasSkeleton;
    [ObservableProperty] private bool _exportSkeleton = true;
    [ObservableProperty] private bool _exportAnimations;
    [ObservableProperty] private ExportAnimationOption? _selectedAvailableAnimation;
    [ObservableProperty] private ExportAnimationOption? _selectedAnimation;

    public bool CanExportAnimations => HasSkeleton && ExportSkeleton;
    public bool CanAddAnimation =>
        CanExportAnimations &&
        SelectedAvailableAnimation != null &&
        AnimationFiles.All(option => !string.Equals(
            option.PackPath,
            SelectedAvailableAnimation.PackPath,
            StringComparison.OrdinalIgnoreCase));
    public bool CanRemoveAnimation => SelectedAnimation != null;

    public RmvToGltfExporterViewModel(
        RmvToGltfExporter exporter,
        IPackFileService packFileService,
        ISkeletonAnimationLookUpHelper skeletonLookUpHelper,
        ApplicationSettingsService applicationSettings)
    {
        _exporter = exporter;
        _packFileService = packFileService;
        _skeletonLookUpHelper = skeletonLookUpHelper;
        _applicationSettings = applicationSettings;
    }

    public ExportSupportEnum CanExportFile(PackFile file)
    {
        var support = _exporter.CanExportFile(file);
        if (support != ExportSupportEnum.NotSupported && !ReferenceEquals(_currentExportSource, file))
        {
            try
            {
                RefreshAnimationOptions(file);
            }
            catch
            {
                ResetAnimationOptions(file);
            }
        }

        return support;
    }

    public bool Execute(PackFile exportSource, string outputPath)
    {
        if (ExportAnimations && !ExportSkeleton)
            throw new InvalidOperationException("导出动画必须同时导出骨架。");
        if (ExportAnimations && AnimationFiles.Count == 0)
            throw new InvalidOperationException("已启用动画导出，但尚未选择任何 ANIM 文件。");

        var settings = new RmvToGltfExporterSettings(
            exportSource,
            AnimationFiles.Select(option => option.File).ToList(),
            outputPath,
            ExportTextures,
            ConvertMaterialTextureToBlender,
            ConvertNormalTextureToBlue,
            ExportAnimations,
            true,
            SelectedGame: _applicationSettings.CurrentSettings.CurrentGame,
            ExportSkeleton: ExportSkeleton);
        return _exporter.Export(settings);
    }

    [RelayCommand]
    private void AddAnimation()
    {
        if (!CanAddAnimation || SelectedAvailableAnimation == null)
            return;

        AnimationFiles.Add(SelectedAvailableAnimation);
        SelectedAnimation = SelectedAvailableAnimation;
        ExportAnimations = true;
        OnPropertyChanged(nameof(CanAddAnimation));
    }

    [RelayCommand]
    private void RemoveAnimation()
    {
        if (SelectedAnimation == null)
            return;

        AnimationFiles.Remove(SelectedAnimation);
        SelectedAnimation = AnimationFiles.LastOrDefault();
        if (AnimationFiles.Count == 0)
            ExportAnimations = false;

        OnPropertyChanged(nameof(CanAddAnimation));
    }

    private void RefreshAnimationOptions(PackFile exportSource)
    {
        ResetAnimationOptions(exportSource);

        var rmvFile = new ModelFactory().Load(exportSource.DataSource.ReadData());
        var skeletonName = rmvFile.Header.SkeletonName;
        HasSkeleton = !string.IsNullOrWhiteSpace(skeletonName);
        ExportSkeleton = HasSkeleton;
        if (!HasSkeleton)
            return;

        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var animationReference in _skeletonLookUpHelper
                     .GetAnimationsForSkeleton(skeletonName)
                     .ToList())
        {
            if (animationReference.IsSkeletonFile)
                continue;

            var file = _packFileService.FindFile(animationReference.AnimationFile);
            var activeReference = file == null
                ? null
                : _skeletonLookUpHelper.FindAnimationRefFromPackFile(file);
            if (activeReference == null ||
                activeReference.IsSkeletonFile ||
                !ReferenceEquals(activeReference.Container, animationReference.Container) ||
                !uniquePaths.Add(animationReference.AnimationFile))
                continue;

            AvailableAnimations.Add(new ExportAnimationOption(
                animationReference.AnimationFile,
                file!));
        }

        SelectedAvailableAnimation = AvailableAnimations.FirstOrDefault();
    }

    private void ResetAnimationOptions(PackFile exportSource)
    {
        _currentExportSource = exportSource;
        AvailableAnimations.Clear();
        AnimationFiles.Clear();
        SelectedAvailableAnimation = null;
        SelectedAnimation = null;
        ExportAnimations = false;
        HasSkeleton = false;
        ExportSkeleton = false;
    }

    partial void OnHasSkeletonChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExportAnimations));
        OnPropertyChanged(nameof(CanAddAnimation));
    }

    partial void OnExportSkeletonChanged(bool value)
    {
        if (!value)
            ExportAnimations = false;

        OnPropertyChanged(nameof(CanExportAnimations));
        OnPropertyChanged(nameof(CanAddAnimation));
    }

    partial void OnSelectedAvailableAnimationChanged(ExportAnimationOption? value) =>
        OnPropertyChanged(nameof(CanAddAnimation));

    partial void OnSelectedAnimationChanged(ExportAnimationOption? value) =>
        OnPropertyChanged(nameof(CanRemoveAnimation));

    internal sealed record ExportAnimationOption(string PackPath, PackFile File)
    {
        public string DisplayName => PackPath;
    }
}
