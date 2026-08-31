using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using GameWorld.Core.Animation;
using GameWorld.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.Ui.Common.OperationProgress;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed record AnimationWorkbenchBaseAnimationRoleOption(
    AnimationWorkbenchBaseAnimationRole Value,
    string DisplayName);

public sealed record AnimationWorkbenchBaseAnimationStyleOption(
    AnimationWorkbenchBaseAnimationStyleMode Value,
    string DisplayName);

public sealed class AnimationWorkbenchBaseAnimationItemViewModel :
    INotifyPropertyChanged
{
    private bool _isSelected;
    private AnimationWorkbenchBaseAnimationRole _role;
    private string _outputPath = string.Empty;
    private AnimationWorkbenchBaseAnimationItemStatus _status =
        AnimationWorkbenchBaseAnimationItemStatus.NotProcessed;
    private string _statusText;
    private string _detailText = string.Empty;

    internal AnimationWorkbenchBaseAnimationItemViewModel(
        AnimationReference reference,
        AnimationWorkbenchBaseAnimationRole role,
        bool isSelected)
    {
        Reference = reference;
        _role = role;
        _isSelected = isSelected;
        _statusText = LocalizeStatus(_status);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? RecipeChanged;

    internal AnimationReference Reference { get; }

    public string SourcePath => Reference.AnimationFile;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value))
                return;
            RecipeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public AnimationWorkbenchBaseAnimationRole Role
    {
        get => _role;
        set
        {
            if (!SetProperty(ref _role, value))
                return;
            OnPropertyChanged(nameof(RoleText));
            RecipeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string RoleText => LocalizationManager.Instance.Get(
        $"AnimationWorkbench.BaseAnimation.Role.{Role}");

    public string OutputPath
    {
        get => _outputPath;
        private set => SetProperty(ref _outputPath, value);
    }

    public AnimationWorkbenchBaseAnimationItemStatus Status
    {
        get => _status;
        private set
        {
            if (!SetProperty(ref _status, value))
                return;
            StatusText = LocalizeStatus(value);
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    internal AnimationWorkbenchBaseAnimationCandidate? Candidate
    {
        get;
        private set;
    }

    internal void ApplyCandidate(
        AnimationWorkbenchBaseAnimationCandidate candidate)
    {
        Candidate = candidate;
        OutputPath = candidate.OutputPath;
        Status = candidate.Status;
        DetailText = string.Join(
            "；",
            candidate.Diagnostics.Select(diagnostic =>
                LocalizationManager.Instance.Get(
                    diagnostic.ReasonKey)));
    }

    internal void Invalidate()
    {
        Candidate = null;
        Status = AnimationWorkbenchBaseAnimationItemStatus.NotProcessed;
        OutputPath = string.Empty;
        DetailText = string.Empty;
    }

    private static string LocalizeStatus(
        AnimationWorkbenchBaseAnimationItemStatus status) =>
        LocalizationManager.Instance.Get(
            $"AnimationWorkbench.BaseAnimation.Status.{status}");

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed partial class AnimationWorkbenchBaseAnimationController :
    ObservableObject,
    IDisposable
{
    private readonly AnimationWorkbenchDocument _document;
    private readonly IAnimationWorkbenchViewport _viewport;
    private readonly IPackFileService _packFileService;
    private readonly ISkeletonAnimationLookUpHelper _skeletonLookup;
    private readonly IStandardDialogs _dialogs;
    private readonly AnimationWorkbenchSourceInput _styleReference;
    private readonly GameSkeleton _targetSkeleton;
    private readonly AnimationWorkbenchBaseAnimationCompletionModule _module;
    private readonly CharacterRetargetProfileStore _profileStore;
    private GameSkeleton? _donorSkeleton;
    private AnimationWorkbenchBaseAnimationCompletionResult? _generated;
    private IAnimationWorkbenchPreviewSession? _previewSession;
    private CancellationTokenSource? _operationCancellation;
    private bool _disposed;

    [ObservableProperty]
    private string _donorSummary;

    [ObservableProperty]
    private string _outputFolder;

    [ObservableProperty]
    private string _outputPrefix = "ext_";

    [ObservableProperty]
    private AnimationWorkbenchBaseAnimationStyleMode _styleMode;

    [ObservableProperty]
    private double _styleWeight = 0.25;

    [ObservableProperty]
    private bool _includeRootMotion;

    [ObservableProperty]
    private bool _overwriteExisting;

    [ObservableProperty]
    private AnimationWorkbenchBaseAnimationItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private long _progressValue;

    [ObservableProperty]
    private long _progressMaximum = 1;

    [ObservableProperty]
    private string _progressDetail = string.Empty;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    public AnimationWorkbenchBaseAnimationController(
        AnimationWorkbenchDocument document,
        IAnimationWorkbenchViewport viewport,
        IPackFileService packFileService,
        ISkeletonAnimationLookUpHelper skeletonLookup,
        IStandardDialogs dialogs,
        AnimationWorkbenchSourceInput styleReference,
        GameSkeleton targetSkeleton,
        AnimationWorkbenchBaseAnimationCompletionModule? module = null,
        CharacterRetargetProfileStore? profileStore = null)
    {
        _document = document ?? throw new ArgumentNullException(
            nameof(document));
        _viewport = viewport ?? throw new ArgumentNullException(
            nameof(viewport));
        _packFileService = packFileService ?? throw new ArgumentNullException(
            nameof(packFileService));
        _skeletonLookup = skeletonLookup ?? throw new ArgumentNullException(
            nameof(skeletonLookup));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _styleReference = styleReference ?? throw new ArgumentNullException(
            nameof(styleReference));
        _targetSkeleton = targetSkeleton ?? throw new ArgumentNullException(
            nameof(targetSkeleton));
        _module = module ??
            new AnimationWorkbenchBaseAnimationCompletionModule();
        _profileStore = profileStore ?? new CharacterRetargetProfileStore();
        _donorSummary = Localize(
            "AnimationWorkbench.BaseAnimation.DonorMissing");
        _statusText = Localize(
            "AnimationWorkbench.BaseAnimation.ReadyHint");
        _outputFolder = BuildDefaultOutputFolder(targetSkeleton.SkeletonName);
        _styleMode = SkeletonsMatch(
            styleReference.Skeleton,
            targetSkeleton)
                ? AnimationWorkbenchBaseAnimationStyleMode.PreserveMotion
                : AnimationWorkbenchBaseAnimationStyleMode.None;

        RoleOptions = Enum.GetValues<AnimationWorkbenchBaseAnimationRole>()
            .Select(role => new AnimationWorkbenchBaseAnimationRoleOption(
                role,
                Localize($"AnimationWorkbench.BaseAnimation.Role.{role}")))
            .ToArray();
        StyleOptions = Enum
            .GetValues<AnimationWorkbenchBaseAnimationStyleMode>()
            .Select(mode => new AnimationWorkbenchBaseAnimationStyleOption(
                mode,
                Localize($"AnimationWorkbench.BaseAnimation.Style.{mode}")))
            .ToArray();
        CancelCommand = new RelayCommand(CancelOperation);
    }

    public event EventHandler? Changed;

    public ObservableCollection<AnimationWorkbenchBaseAnimationItemViewModel>
        Items
    { get; } = [];

    public IReadOnlyList<AnimationWorkbenchBaseAnimationRoleOption>
        RoleOptions
    { get; }

    public IReadOnlyList<AnimationWorkbenchBaseAnimationStyleOption>
        StyleOptions
    { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand? ActiveCancelCommand =>
        IsBusy && !IsProgressIndeterminate ? CancelCommand : null;

    public bool HasDonor => _donorSkeleton != null;

    public bool CanGenerate => !IsBusy &&
        HasDonor &&
        Items.Any(item => item.IsSelected) &&
        !string.IsNullOrWhiteSpace(OutputFolder) &&
        StyleWeight is >= 0 and <= 1;

    public bool CanPreview => !IsBusy &&
        SelectedItem?.Candidate is
        {
            PreviewAnimation: not null,
            Status: AnimationWorkbenchBaseAnimationItemStatus.Ready or
                AnimationWorkbenchBaseAnimationItemStatus.Saved or
                AnimationWorkbenchBaseAnimationItemStatus.Skipped,
        };

    public bool CanSave => !IsBusy &&
        _generated != null &&
        (_generated.ReadyCount > 0 ||
         OverwriteExisting && _generated.SkippedCount > 0) &&
        (_generated.AnimationSet?.Status ==
            AnimationWorkbenchBaseAnimationItemStatus.Ready ||
         OverwriteExisting && _generated.AnimationSet?.Status ==
            AnimationWorkbenchBaseAnimationItemStatus.Skipped) &&
        _packFileService.GetEditablePack() is FolderProjectContainer;

    public string AnimationSetOutputPath => Path.Combine(
        "animations",
        "database",
        "battle",
        "bin",
        $"{OutputPrefix}{Path.GetFileNameWithoutExtension(_targetSkeleton.SkeletonName)}_base.animpack");

    [RelayCommand]
    private void BrowseDonorAnimation()
    {
        if (IsBusy)
            return;
        var result = _dialogs.DisplayBrowseDialog([".anim"]);
        if (!result.Result || result.File == null)
            return;

        try
        {
            var parsed = AnimationFile.Create(result.File);
            var skeletonFile = _skeletonLookup.GetSkeletonFileFromName(
                parsed.Header.SkeletonName);
            if (skeletonFile == null)
            {
                StatusText = Localize(
                    "AnimationWorkbench.BaseAnimation.DonorSkeletonMissing");
                return;
            }

            _donorSkeleton = new GameSkeleton(
                skeletonFile,
                new AnimationPlayer());
            var selectedPath = _packFileService.GetFullPath(result.File);
            var familyRoot = AnimationWorkbenchBaseAnimationClassifier
                .GetFamilyRoot(selectedPath);
            DonorSummary = LocalizationManager.Instance.GetFormat(
                "AnimationWorkbench.BaseAnimation.DonorFamilyFormat",
                _donorSkeleton.SkeletonName,
                familyRoot);
            ReplaceCandidates(_skeletonLookup
                .GetAnimationsForSkeleton(_donorSkeleton.SkeletonName),
                familyRoot);
            StatusText = Items.Count == 0
                ? Localize(
                    "AnimationWorkbench.BaseAnimation.NoDonorAnimations")
                : LocalizationManager.Instance.GetFormat(
                    "AnimationWorkbench.BaseAnimation.CandidatesLoaded",
                    Items.Count,
                    Items.Count(item => item.IsSelected));
        }
        catch (Exception)
        {
            _donorSkeleton = null;
            ReplaceCandidates([], null);
            DonorSummary = Localize(
                "AnimationWorkbench.BaseAnimation.DonorMissing");
            StatusText = Localize(
                "AnimationWorkbench.BaseAnimation.DonorLoadFailed");
        }
        RaiseStateChanged();
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        if (_packFileService.GetEditablePack() is not
                FolderProjectContainer project)
        {
            StatusText = Localize(
                "AnimationWorkbench.BaseAnimation.FolderProjectRequired");
            return;
        }
        var result = _dialogs.DisplayBrowseFolderDialog(project);
        if (result.Result)
            OutputFolder = result.Folder;
    }

    [RelayCommand]
    private async Task GenerateSelectedAsync()
    {
        if (!CanGenerate || _donorSkeleton == null)
            return;

        ReleasePreview();
        var selected = Items.Where(item => item.IsSelected).ToArray();
        SetOperationState(
            true,
            selected.Length * 2,
            "AnimationWorkbench.BaseAnimation.ProgressGenerate",
            isIndeterminate: false);
        _operationCancellation = new CancellationTokenSource();
        var cancellationToken = _operationCancellation.Token;
        try
        {
            var preparationProgress =
                new Progress<OperationProgressUpdate>(update =>
                {
                    ProgressValue = update.Completed;
                    ProgressMaximum = Math.Max(1, selected.Length * 2);
                    ProgressDetail = update.Detail ?? string.Empty;
                });
            var prepared = await Task.Run(() => PrepareRecipe(
                selected,
                _donorSkeleton,
                preparationProgress,
                cancellationToken));
            foreach (var failure in prepared.Failures)
            {
                Items.First(item => string.Equals(
                        item.SourcePath,
                        failure.SourcePath,
                        StringComparison.OrdinalIgnoreCase))
                    .ApplyCandidate(failure);
            }
            if (prepared.Items.Count == 0)
            {
                StatusText = cancellationToken.IsCancellationRequested
                    ? Localize(
                        "AnimationWorkbench.BaseAnimation.OperationCancelled")
                    : Localize(
                        "AnimationWorkbench.BaseAnimation.NoReadableCandidates");
                RaiseStateChanged();
                return;
            }

            var outputFormat = _styleReference.Format?.Version == 8
                ? _styleReference.Format
                : new AnimationWorkbenchSourceFormat(8, 1);
            var request = new AnimationWorkbenchBaseAnimationRequest(
                prepared.Items,
                _targetSkeleton,
                StyleMode == AnimationWorkbenchBaseAnimationStyleMode.None
                    ? null
                    : _styleReference,
                StyleMode,
                StyleWeight,
                IncludeRootMotion,
                prepared.Mappings,
                outputFormat)
            {
                AnimationSetOutputPath = AnimationSetOutputPath,
            };
            var generationProgress =
                new Progress<OperationProgressUpdate>(update =>
                {
                    ProgressValue = selected.Length + update.Completed;
                    ProgressMaximum = Math.Max(
                        1,
                        selected.Length + update.Total);
                    ProgressDetail = update.Detail ?? string.Empty;
                });
            var generated = await Task.Run(() => _module.Generate(
                request,
                generationProgress,
                cancellationToken));
            var sourceFailures = prepared.Failures;
            var animationSet = generated.AnimationSet;
            if (sourceFailures.Count != 0 && animationSet != null)
            {
                animationSet = animationSet with
                {
                    Status = AnimationWorkbenchBaseAnimationItemStatus.Failed,
                    Bytes = null,
                    Diagnostics =
                    [
                        new AnimationWorkbenchDiagnostic(
                            AnimationWorkbenchDiagnosticCode
                                .BaseAnimationSetIncomplete,
                            AnimationWorkbenchDiagnosticSeverity.Error),
                    ],
                };
            }
            _generated = new AnimationWorkbenchBaseAnimationCompletionResult(
                generated.Items.Concat(sourceFailures).ToArray(),
                animationSet);
            ApplyGeneratedResult(_generated);
            StatusText = LocalizationManager.Instance.GetFormat(
                "AnimationWorkbench.BaseAnimation.GenerateResult",
                _generated.ReadyCount,
                _generated.FailedCount,
                _generated.NotProcessedCount,
                LocalizeStatus(_generated.AnimationSet?.Status));
        }
        catch (Exception)
        {
            StatusText = cancellationToken.IsCancellationRequested
                ? Localize(
                    "AnimationWorkbench.BaseAnimation.OperationCancelled")
                : Localize(
                    "AnimationWorkbench.BaseAnimation.GenerateFailed");
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetOperationState(false, 1, null, isIndeterminate: false);
        }
    }

    [RelayCommand]
    private void PreviewSelected()
    {
        if (!CanPreview || SelectedItem?.Candidate?.PreviewAnimation == null)
            return;
        _previewSession?.Dispose();
        _previewSession = _viewport.Show(
            new AnimationWorkbenchPreviewSnapshot(
                AnimationWorkbenchPreviewKind.Result,
                SelectedItem.OutputPath,
                SelectedItem.Candidate.PreviewAnimation,
                _targetSkeleton),
            CancellationToken.None);
        StatusText = LocalizationManager.Instance.GetFormat(
            "AnimationWorkbench.BaseAnimation.Previewing",
            SelectedItem.SourcePath);
    }

    [RelayCommand]
    private async Task SaveGeneratedAsync()
    {
        if (!CanSave ||
            _generated == null ||
            _packFileService.GetEditablePack() is not
                FolderProjectContainer project)
        {
            StatusText = Localize(
                "AnimationWorkbench.BaseAnimation.FolderProjectRequired");
            return;
        }

        SetOperationState(
            true,
            _generated.ReadyCount,
            "AnimationWorkbench.BaseAnimation.ProgressSave",
            isIndeterminate: true);
        try
        {
            _generated = await _module.SaveReadyCandidatesAsync(
                _packFileService,
                project,
                _generated,
                OverwriteExisting,
                CancellationToken.None);
            ApplyGeneratedResult(_generated);
            StatusText = LocalizationManager.Instance.GetFormat(
                "AnimationWorkbench.BaseAnimation.SaveResult",
                _generated.SavedCount,
                _generated.SkippedCount,
                _generated.FailedCount,
                LocalizeStatus(_generated.AnimationSet?.Status));
        }
        catch (Exception)
        {
            StatusText = Localize(
                "AnimationWorkbench.BaseAnimation.SaveFailed");
        }
        finally
        {
            SetOperationState(false, 1, null, isIndeterminate: false);
        }
    }

    public void ReleasePreview()
    {
        if (_previewSession == null)
            return;
        _previewSession.Dispose();
        _previewSession = null;
        if (!_document.IsClosed &&
            _document.GetState().Result != null)
        {
            _document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        }
        RaiseStateChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _operationCancellation?.Cancel();
        ReleasePreview();
        foreach (var item in Items)
            item.RecipeChanged -= Item_RecipeChanged;
    }

    partial void OnOutputFolderChanged(string value) => InvalidateGenerated();

    partial void OnOutputPrefixChanged(string value)
    {
        OnPropertyChanged(nameof(AnimationSetOutputPath));
        InvalidateGenerated();
    }

    partial void OnStyleModeChanged(
        AnimationWorkbenchBaseAnimationStyleMode value) =>
        InvalidateGenerated();

    partial void OnStyleWeightChanged(double value) => InvalidateGenerated();

    partial void OnIncludeRootMotionChanged(bool value) =>
        InvalidateGenerated();

    partial void OnOverwriteExistingChanged(bool value) =>
        RaiseStateChanged();

    partial void OnSelectedItemChanged(
        AnimationWorkbenchBaseAnimationItemViewModel? value) =>
        RaiseStateChanged();

    private void ReplaceCandidates(
        IEnumerable<AnimationReference> candidates,
        string? familyRoot)
    {
        foreach (var item in Items)
            item.RecipeChanged -= Item_RecipeChanged;
        Items.Clear();
        foreach (var reference in candidates
                     .Where(item => !item.IsSkeletonFile)
                     .Where(item => string.IsNullOrWhiteSpace(familyRoot) ||
                         AnimationWorkbenchBaseAnimationClassifier.IsInFamily(
                             item.AnimationFile,
                             familyRoot))
                     .GroupBy(
                         item => item.AnimationFile,
                         StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(
                         item => item.AnimationFile,
                         StringComparer.OrdinalIgnoreCase))
        {
            var role = AnimationWorkbenchBaseAnimationClassifier.Classify(
                reference.AnimationFile);
            var item = new AnimationWorkbenchBaseAnimationItemViewModel(
                reference,
                role,
                role != AnimationWorkbenchBaseAnimationRole.Other &&
                !PathContainsSegment(reference.AnimationFile, "tech"));
            item.RecipeChanged += Item_RecipeChanged;
            Items.Add(item);
        }
        SelectedItem = Items.FirstOrDefault(item => item.IsSelected) ??
            Items.FirstOrDefault();
        InvalidateGenerated();
    }

    private AnimationWorkbenchSourceInput? LoadSource(
        AnimationReference reference,
        GameSkeleton skeleton)
    {
        try
        {
            var file = _packFileService.FindFile(
                reference.AnimationFile,
                reference.Container);
            if (file == null)
                return null;
            var parsed = AnimationFile.Create(file);
            return new AnimationWorkbenchSourceInput(
                reference.AnimationFile,
                new AnimationClip(parsed, skeleton),
                skeleton,
                AnimationWorkbenchSourceFormat.FromFile(parsed));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private PreparedRecipe PrepareRecipe(
        IReadOnlyList<AnimationWorkbenchBaseAnimationItemViewModel> selected,
        GameSkeleton donorSkeleton,
        IProgress<OperationProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        var items = new List<AnimationWorkbenchBaseAnimationRecipeItem>(
            selected.Count);
        var failures = new List<AnimationWorkbenchBaseAnimationCandidate>();
        for (var index = 0; index < selected.Count; index++)
        {
            var item = selected[index];
            if (cancellationToken.IsCancellationRequested)
            {
                failures.Add(new AnimationWorkbenchBaseAnimationCandidate(
                    item.SourcePath,
                    BuildOutputPath(item),
                    item.Role,
                    AnimationWorkbenchBaseAnimationItemStatus.NotProcessed,
                    null,
                    null,
                    [
                        new AnimationWorkbenchDiagnostic(
                            AnimationWorkbenchDiagnosticCode
                                .BaseAnimationGenerationCancelled,
                            AnimationWorkbenchDiagnosticSeverity.Error),
                    ]));
                progress.Report(new OperationProgressUpdate(
                    string.Empty,
                    item.SourcePath,
                    index + 1,
                    selected.Count * 2));
                continue;
            }

            var source = LoadSource(item.Reference, donorSkeleton);
            if (source == null)
            {
                failures.Add(new AnimationWorkbenchBaseAnimationCandidate(
                    item.SourcePath,
                    BuildOutputPath(item),
                    item.Role,
                    AnimationWorkbenchBaseAnimationItemStatus.Failed,
                    null,
                    null,
                    [
                        new AnimationWorkbenchDiagnostic(
                            AnimationWorkbenchDiagnosticCode
                                .RetargetSourceMissing,
                            AnimationWorkbenchDiagnosticSeverity.Error),
                    ]));
            }
            else
            {
                items.Add(new AnimationWorkbenchBaseAnimationRecipeItem(
                    item.SourcePath,
                    BuildOutputPath(item),
                    item.Role,
                    source));
            }
            progress.Report(new OperationProgressUpdate(
                string.Empty,
                item.SourcePath,
                index + 1,
                selected.Count * 2));
        }

        var mappings = items.Count == 0
            ? Array.Empty<AnimationWorkbenchRetargetBoneMapping>()
            : LoadMappings(items[0].Source);
        return new PreparedRecipe(items, failures, mappings);
    }

    private IReadOnlyList<AnimationWorkbenchRetargetBoneMapping> LoadMappings(
        AnimationWorkbenchSourceInput source)
    {
        using var document = new AnimationWorkbenchDocument();
        document.Load(new AnimationWorkbenchLoadRequest(
            source,
            null,
            GameTypeEnum.Warhammer3,
            _targetSkeleton));
        var stored = document.LoadRetargetProfile(
            AnimationWorkbenchSourceSlot.AnimationA,
            _profileStore);
        return stored.Succeeded
            ? stored.Mappings
            : document.CreateRetargetMapping(
                AnimationWorkbenchSourceSlot.AnimationA).Mappings;
    }

    private void ApplyGeneratedResult(
        AnimationWorkbenchBaseAnimationCompletionResult result)
    {
        var bySource = result.Items.ToLookup(
            item => item.SourcePath,
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items.Where(item => item.IsSelected))
        {
            var candidate = bySource[item.SourcePath].FirstOrDefault();
            if (candidate != null)
                item.ApplyCandidate(candidate);
        }
        RaiseStateChanged();
    }

    private string BuildOutputPath(
        AnimationWorkbenchBaseAnimationItemViewModel item)
    {
        var fileName = (OutputPrefix ?? string.Empty) +
            Path.GetFileName(item.SourcePath);
        return Path.Combine(
            OutputFolder ?? string.Empty,
            GetRoleFolder(item.Role),
            fileName);
    }

    private void Item_RecipeChanged(object? sender, EventArgs e) =>
        InvalidateGenerated();

    private void InvalidateGenerated()
    {
        if (_generated == null)
        {
            RaiseStateChanged();
            return;
        }
        _generated = null;
        foreach (var item in Items)
            item.Invalidate();
        StatusText = Localize(
            "AnimationWorkbench.BaseAnimation.SettingsChanged");
        RaiseStateChanged();
    }

    private void SetOperationState(
        bool isBusy,
        int maximum,
        string? statusKey,
        bool isIndeterminate)
    {
        IsBusy = isBusy;
        IsProgressIndeterminate = isIndeterminate;
        ProgressValue = 0;
        ProgressMaximum = Math.Max(1, maximum);
        ProgressDetail = string.Empty;
        if (statusKey != null)
            StatusText = Localize(statusKey);
        RaiseStateChanged();
    }

    private void CancelOperation() => _operationCancellation?.Cancel();

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(HasDonor));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ActiveCancelCommand));
        BrowseDonorAnimationCommand.NotifyCanExecuteChanged();
        BrowseOutputFolderCommand.NotifyCanExecuteChanged();
        GenerateSelectedCommand.NotifyCanExecuteChanged();
        PreviewSelectedCommand.NotifyCanExecuteChanged();
        SaveGeneratedCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool SkeletonsMatch(
        GameSkeleton left,
        GameSkeleton right) =>
        left.SkeletonName == right.SkeletonName &&
        left.BoneCount == right.BoneCount &&
        left.BoneNames.SequenceEqual(right.BoneNames) &&
        Enumerable.Range(0, left.BoneCount).All(index =>
            left.GetParentBoneIndex(index) == right.GetParentBoneIndex(index));

    private static string BuildDefaultOutputFolder(string skeletonName)
    {
        var name = Path.GetFileNameWithoutExtension(skeletonName);
        return Path.Combine(
            "animations",
            "battle",
            name,
            "base_completion");
    }

    private static string GetRoleFolder(
        AnimationWorkbenchBaseAnimationRole role) => role switch
        {
            AnimationWorkbenchBaseAnimationRole.Idle => "idle",
            AnimationWorkbenchBaseAnimationRole.Walk => "walk",
            AnimationWorkbenchBaseAnimationRole.Run => "run",
            AnimationWorkbenchBaseAnimationRole.HitReaction => "hit",
            AnimationWorkbenchBaseAnimationRole.Death => "death",
            _ => "other",
        };

    private static bool PathContainsSegment(string path, string segment) =>
        path.Replace('/', '\\')
            .Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);

    private static string Localize(string key) =>
        LocalizationManager.Instance.Get(key);

    private static string LocalizeStatus(
        AnimationWorkbenchBaseAnimationItemStatus? status) => status == null
            ? Localize("AnimationWorkbench.BaseAnimation.Status.NotProcessed")
            : Localize($"AnimationWorkbench.BaseAnimation.Status.{status}");

    private sealed record PreparedRecipe(
        IReadOnlyList<AnimationWorkbenchBaseAnimationRecipeItem> Items,
        IReadOnlyList<AnimationWorkbenchBaseAnimationCandidate> Failures,
        IReadOnlyList<AnimationWorkbenchRetargetBoneMapping> Mappings);
}
