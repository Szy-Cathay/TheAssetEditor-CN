using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Shared.Core.Common.AnimationPlayer;
using GameWorld.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using System.Windows;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public partial class TrustedAnimationPreviewViewModel :
    ObservableObject,
    IEditorInterface,
    IFileEditor
{
    private readonly ITrustedAnimationPreviewViewport _viewport;
    private readonly TrustedAnimationPreviewFeatureSession _session;
    private readonly ITrustedAnimationModelDiscovery _modelDiscovery;
    private readonly ITrustedAnimationDiscovery _animationDiscovery;
    private readonly ITrustedWsModelResolver _wsModelResolver;
    private readonly IPackFileService _packFileService;
    private readonly IStandardDialogs? _dialogs;
    private CancellationTokenSource? _modelScanCancellation;
    private CancellationTokenSource? _animationCancellation;
    private int _modelScanGeneration;
    private int _animationGeneration;
    private bool _closed;

    [ObservableProperty]
    private bool _showModel = true;

    [ObservableProperty]
    private bool _showSkeleton = true;

    [ObservableProperty]
    private bool _isModelPickerOpen;

    [ObservableProperty]
    private bool _isModelScanRunning;

    [ObservableProperty]
    private string _modelSearchText = string.Empty;

    [ObservableProperty]
    private string _modelScanStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseSelectedModelCommand))]
    private TrustedAnimationModelCandidate? _selectedModelCandidate;

    [ObservableProperty]
    private bool _isAnimationPickerOpen;

    [ObservableProperty]
    private bool _isAnimationScanRunning;

    [ObservableProperty]
    private string _animationSearchText = string.Empty;

    [ObservableProperty]
    private string _animationScanStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseSelectedAnimationCommand))]
    private TrustedAnimationCandidate? _selectedAnimationCandidate;

    public TrustedAnimationPreviewViewModel(
        ITrustedAnimationPreviewViewport viewport,
        IPackFileService packFileService,
        ITrustedAnimationModelDiscovery modelDiscovery)
        : this(
            viewport,
            packFileService,
            modelDiscovery,
            new TrustedAnimationDiscovery(packFileService),
            new TrustedWsModelResolver(
                packFileService,
                new TrustedRigidModelInspector()))
    {
    }

    public TrustedAnimationPreviewViewModel(
        ITrustedAnimationPreviewViewport viewport,
        IPackFileService packFileService,
        ITrustedAnimationModelDiscovery modelDiscovery,
        ITrustedAnimationDiscovery animationDiscovery)
        : this(
            viewport,
            packFileService,
            modelDiscovery,
            animationDiscovery,
            new TrustedWsModelResolver(
                packFileService,
                new TrustedRigidModelInspector()))
    {
    }

    public TrustedAnimationPreviewViewModel(
        ITrustedAnimationPreviewViewport viewport,
        IPackFileService packFileService,
        ITrustedAnimationModelDiscovery modelDiscovery,
        ITrustedAnimationDiscovery animationDiscovery,
        ITrustedWsModelResolver wsModelResolver,
        IStandardDialogs? dialogs = null)
    {
        _viewport = viewport;
        _packFileService = packFileService;
        _dialogs = dialogs;
        _modelDiscovery = modelDiscovery;
        _animationDiscovery = animationDiscovery;
        _wsModelResolver = wsModelResolver;
        _session = new TrustedAnimationPreviewFeatureSession(
            viewport,
            packFileService);
        _session.StateChanged += OnStateChanged;
        _viewport.PlaybackChanged += OnPlaybackChanged;
        DisplayName = LocalizationManager.Instance.Get(
            "DisplayName.AnimationWorkbench");
        ModelScanStatus = LocalizationManager.Instance.Get(
            "AnimationWorkbench.ModelPicker.Ready");
        AnimationScanStatus = LocalizationManager.Instance.Get(
            "AnimationWorkbench.AnimationPicker.Ready");
        ModelCandidatesView = CollectionViewSource.GetDefaultView(
            ModelCandidates);
        ModelCandidatesView.GroupDescriptions.Add(
            new PropertyGroupDescription(
                nameof(TrustedAnimationModelCandidate.SourceGroup)));
        ModelCandidatesView.Filter = MatchesModelSearch;
        AnimationCandidatesView = CollectionViewSource.GetDefaultView(
            AnimationCandidates);
        AnimationCandidatesView.Filter = MatchesAnimationSearch;
    }

    public string DisplayName { get; set; }

    public PackFile CurrentFile { get; private set; } = null!;

    public IWpfGame GameWorld => _viewport.GameWorld;

    public AnimationPlayerViewModel Player => _viewport.Player;

    public TrustedAnimationPreviewResourceState Model =>
        _session.State.Model;

    public TrustedAnimationPreviewResourceState Skeleton =>
        _session.State.Skeleton;

    public TrustedAnimationPreviewResourceState Animation =>
        _session.State.Animation;

    public string AnimationPathText =>
        string.IsNullOrWhiteSpace(Animation.Path)
            ? LocalizationManager.Instance.Get(
                "AnimationWorkbench.TrustedPreview.NotSelected")
            : Animation.Path;

    public bool IsReady => _session.State.IsReady;

    public int MeshCount => _session.State.MeshCount;

    public ObservableCollection<TrustedAnimationModelCandidate>
        ModelCandidates { get; } = [];

    public ICollectionView ModelCandidatesView { get; }

    public ObservableCollection<TrustedAnimationCandidate>
        AnimationCandidates { get; } = [];

    public ICollectionView AnimationCandidatesView { get; }

    public TrustedAnimationPlaybackState PlaybackState =>
        _viewport.PlaybackState ?? TrustedAnimationPlaybackState.Empty;

    public bool HasAnimation => PlaybackState.HasAnimation;

    public bool IsPlaying => PlaybackState.IsPlaying;

    public bool IsLooping
    {
        get => PlaybackState.IsLooping;
        set
        {
            if (!HasAnimation || value == PlaybackState.IsLooping)
                return;
            _viewport.SetLooping(value);
            NotifyPlaybackChanged();
        }
    }

    public double PlaybackMaximum =>
        Math.Max(0, PlaybackState.DurationSeconds);

    public double CurrentTimeSeconds
    {
        get => PlaybackState.CurrentTimeSeconds;
        set
        {
            if (!HasAnimation ||
                Math.Abs(value - PlaybackState.CurrentTimeSeconds) < 0.0001)
            {
                return;
            }
            _viewport.Seek(value);
            NotifyPlaybackChanged();
        }
    }

    public string PlaybackSummary => string.Format(
        LocalizationManager.Instance.Get(
            "AnimationWorkbench.TrustedPreview.PlaybackSummary"),
        PlaybackState.CurrentTimeSeconds,
        PlaybackState.DurationSeconds,
        PlaybackState.CurrentFrame,
        PlaybackState.FrameCount,
        PlaybackState.FramesPerSecond,
        TrustedAnimationFormatText.Get(
            PlaybackState.PartCount,
            PlaybackState.HasStaticFrame,
            PlaybackState.IsStaticPose));

    public bool HasModelDiagnostic =>
        !string.IsNullOrWhiteSpace(Model.Diagnostic);

    public bool HasSkeletonDiagnostic =>
        !string.IsNullOrWhiteSpace(Skeleton.Diagnostic);

    public bool HasAnimationDiagnostic =>
        !string.IsNullOrWhiteSpace(Animation.Diagnostic);

    public void LoadFile(PackFile file) =>
        _ = LoadFileAsync(file);

    public async Task LoadFileAsync(PackFile file)
    {
        CancelModelDiscovery(false);
        CancelAnimationWork(false);
        IsModelPickerOpen = false;
        IsAnimationPickerOpen = false;
        SelectedModelCandidate = null;
        SelectedAnimationCandidate = null;
        AnimationCandidates.Clear();
        CurrentFile = file;
        if (!IsCompositeModel(file))
        {
            _session.LoadModel(file);
            NotifyPlaybackChanged();
            return;
        }

        await LoadCompositeModelAsync(file);
    }

    public void Close()
    {
        _closed = true;
        CancelModelDiscovery(false);
        CancelAnimationWork(false);
        _session.StateChanged -= OnStateChanged;
        _viewport.PlaybackChanged -= OnPlaybackChanged;
        _viewport.Dispose();
    }

    public async Task StartModelDiscoveryAsync()
    {
        if (_closed)
            return;

        var generation = Interlocked.Increment(
            ref _modelScanGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(
            ref _modelScanCancellation,
            cancellation);
        previous?.Cancel();
        ModelCandidates.Clear();
        SelectedModelCandidate = null;
        IsModelScanRunning = true;
        ModelScanStatus = FormatModelScanStatus(
            "AnimationWorkbench.ModelPicker.Scanning",
            0);

        try
        {
            await foreach (var batch in _modelDiscovery.DiscoverAsync(
                               cancellation.Token))
            {
                if (generation != _modelScanGeneration ||
                    cancellation.IsCancellationRequested ||
                    _closed)
                {
                    return;
                }

                foreach (var candidate in batch)
                    ModelCandidates.Add(candidate);
                ModelScanStatus = FormatModelScanStatus(
                    "AnimationWorkbench.ModelPicker.Scanning",
                    ModelCandidates.Count);
            }

            if (generation == _modelScanGeneration && !_closed)
            {
                ModelScanStatus = FormatModelScanStatus(
                    "AnimationWorkbench.ModelPicker.Complete",
                    ModelCandidates.Count);
            }
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            if (generation == _modelScanGeneration && !_closed)
            {
                ModelScanStatus = FormatModelScanStatus(
                    "AnimationWorkbench.ModelPicker.Cancelled",
                    ModelCandidates.Count);
            }
        }
        catch (Exception exception)
        {
            if (generation == _modelScanGeneration && !_closed)
            {
                ModelScanStatus = string.Format(
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.ModelPicker.Failed"),
                    exception.Message);
            }
        }
        finally
        {
            if (generation == _modelScanGeneration)
            {
                Interlocked.CompareExchange(
                    ref _modelScanCancellation,
                    null,
                    cancellation);
                IsModelScanRunning = false;
            }
            cancellation.Dispose();
        }
    }

    partial void OnShowModelChanged(bool value) =>
        _viewport.SetModelVisible(value);

    partial void OnShowSkeletonChanged(bool value) =>
        _viewport.SetSkeletonVisible(value);

    partial void OnModelSearchTextChanged(string value) =>
        ModelCandidatesView.Refresh();

    partial void OnAnimationSearchTextChanged(string value) =>
        AnimationCandidatesView.Refresh();

    [RelayCommand]
    private void FocusModel() => _viewport.FocusModel();

    [RelayCommand]
    private void ShowFront() => _viewport.ShowFront();

    [RelayCommand]
    private void ResetCamera() => _viewport.ResetCamera();

    [RelayCommand]
    private async Task OpenModelPicker()
    {
        CancelAnimationWork(false);
        IsAnimationPickerOpen = false;
        CancelModelDiscovery(false);
        IsModelPickerOpen = false;
        if (_dialogs == null)
            return;

        var result = _dialogs.DisplayBrowseDialog(
            [".variantmeshdefinition", ".wsmodel", ".rigid_model_v2"]);
        if (result.Result && result.File != null)
            await LoadFileAsync(result.File);
    }

    [RelayCommand]
    private void CancelModelScan() => CancelModelDiscovery(true);

    [RelayCommand]
    private void CloseModelPicker()
    {
        CancelModelDiscovery(false);
        IsModelPickerOpen = false;
    }

    private bool CanUseSelectedModel() =>
        SelectedModelCandidate != null;

    [RelayCommand(CanExecute = nameof(CanUseSelectedModel))]
    private async Task UseSelectedModel()
    {
        var candidate = SelectedModelCandidate;
        if (candidate == null)
            return;
        await LoadFileAsync(candidate.File);
    }

    private async Task LoadCompositeModelAsync(PackFile file)
    {
        var generation = Interlocked.Increment(
            ref _modelScanGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(
            ref _modelScanCancellation,
            cancellation);
        previous?.Cancel();
        _session.BeginModelLoad(file);
        IsModelScanRunning = true;
        ModelScanStatus = LocalizationManager.Instance.Get(
            "AnimationWorkbench.ModelPicker.Loading");
        try
        {
            var result = await _wsModelResolver.ResolveAsync(
                file,
                cancellation.Token);
            if (generation != _modelScanGeneration ||
                cancellation.IsCancellationRequested ||
                _closed ||
                !ReferenceEquals(file, CurrentFile))
            {
                return;
            }

            if (!result.IsSuccess || result.Resolution == null)
            {
                _session.ReportModelFailure(
                    file,
                    result.Diagnostic);
                ModelScanStatus = result.Diagnostic;
                IsModelPickerOpen = true;
                return;
            }

            _session.LoadModel(
                file,
                result.Resolution.SkeletonGeometry,
                result.Resolution.Skeleton);
            ModelScanStatus = string.Format(
                LocalizationManager.Instance.Get(
                    "AnimationWorkbench.ModelPicker.Loaded"),
                result.Resolution.GeometryCount,
                result.Resolution.StaticAttachmentCount);
            IsModelPickerOpen = !_session.State.IsReady;
            NotifyPlaybackChanged();
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation == _modelScanGeneration && !_closed)
            {
                var diagnostic = string.Format(
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.TrustedPreview.WsModelUnexpectedFailure"),
                    exception.Message);
                _session.ReportModelFailure(file, diagnostic);
                ModelScanStatus = diagnostic;
                IsModelPickerOpen = true;
            }
        }
        finally
        {
            if (generation == _modelScanGeneration)
            {
                Interlocked.CompareExchange(
                    ref _modelScanCancellation,
                    null,
                    cancellation);
                IsModelScanRunning = false;
            }
            cancellation.Dispose();
        }
    }

    private static bool IsCompositeModel(PackFile file) =>
        file.Name.EndsWith(
            ".wsmodel",
            StringComparison.OrdinalIgnoreCase) ||
        file.Name.EndsWith(
            ".variantmeshdefinition",
            StringComparison.OrdinalIgnoreCase);

    public async Task StartAnimationDiscoveryAsync()
    {
        var skeleton = _session.SkeletonIdentity;
        if (_closed || !IsReady || skeleton == null)
            return;

        var generation = Interlocked.Increment(
            ref _animationGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(
            ref _animationCancellation,
            cancellation);
        previous?.Cancel();
        AnimationCandidates.Clear();
        SelectedAnimationCandidate = null;
        IsAnimationScanRunning = true;
        AnimationScanStatus = FormatAnimationScanStatus(
            "AnimationWorkbench.AnimationPicker.Scanning",
            0);

        try
        {
            await foreach (var batch in _animationDiscovery.DiscoverAsync(
                               skeleton,
                               cancellation.Token))
            {
                if (generation != _animationGeneration ||
                    cancellation.IsCancellationRequested ||
                    _closed ||
                    !ReferenceEquals(
                        skeleton,
                        _session.SkeletonIdentity))
                {
                    return;
                }

                foreach (var candidate in batch)
                    AnimationCandidates.Add(candidate);
                AnimationScanStatus = FormatAnimationScanStatus(
                    "AnimationWorkbench.AnimationPicker.Scanning",
                    AnimationCandidates.Count);
            }

            if (generation == _animationGeneration && !_closed)
            {
                AnimationScanStatus = FormatAnimationScanStatus(
                    "AnimationWorkbench.AnimationPicker.Complete",
                    AnimationCandidates.Count);
            }
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            if (generation == _animationGeneration && !_closed)
            {
                AnimationScanStatus = FormatAnimationScanStatus(
                    "AnimationWorkbench.AnimationPicker.Cancelled",
                    AnimationCandidates.Count);
            }
        }
        catch (Exception exception)
        {
            if (generation == _animationGeneration && !_closed)
            {
                AnimationScanStatus = string.Format(
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.AnimationPicker.Failed"),
                    exception.Message);
            }
        }
        finally
        {
            if (generation == _animationGeneration)
            {
                Interlocked.CompareExchange(
                    ref _animationCancellation,
                    null,
                    cancellation);
                IsAnimationScanRunning = false;
            }
            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private async Task OpenAnimationPicker()
    {
        CancelModelDiscovery(false);
        IsModelPickerOpen = false;
        CancelAnimationWork(false);
        IsAnimationPickerOpen = false;
        if (_dialogs == null || !IsReady)
            return;

        var result = _dialogs.DisplayBrowseDialog([".anim"]);
        if (result.Result && result.File != null)
            await LoadAnimationAsync(result.File, null);
    }

    [RelayCommand]
    private void CancelAnimationScan() => CancelAnimationWork(true);

    [RelayCommand]
    private void CloseAnimationPicker()
    {
        CancelAnimationWork(false);
        IsAnimationPickerOpen = false;
    }

    private bool CanUseSelectedAnimation() =>
        SelectedAnimationCandidate != null;

    [RelayCommand(CanExecute = nameof(CanUseSelectedAnimation))]
    private async Task UseSelectedAnimation()
    {
        var candidate = SelectedAnimationCandidate;
        if (candidate == null)
            return;

        await LoadAnimationAsync(candidate.File, candidate);
    }

    private async Task LoadAnimationAsync(
        PackFile file,
        TrustedAnimationCandidate? candidate)
    {
        var skeleton = _session.SkeletonIdentity;
        if (skeleton == null)
            return;
        var updateCandidateMetadata = candidate == null;
        candidate ??= CreateAnimationCandidate(file, null);

        var generation = Interlocked.Increment(
            ref _animationGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(
            ref _animationCancellation,
            cancellation);
        previous?.Cancel();
        IsAnimationScanRunning = true;
        AnimationScanStatus = LocalizationManager.Instance.Get(
            "AnimationWorkbench.AnimationPicker.Loading");
        try
        {
            var animation = await Task.Run(
                () =>
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    return AnimationFile.Create(file);
                },
                cancellation.Token);
            if (generation != _animationGeneration ||
                cancellation.IsCancellationRequested ||
                _closed ||
                !ReferenceEquals(skeleton, _session.SkeletonIdentity))
            {
                return;
            }

            if (updateCandidateMetadata)
                candidate = CreateAnimationCandidate(file, animation);
            _session.LoadAnimation(candidate, animation);
            IsAnimationPickerOpen = !_session.State.Animation.IsResolved;
            NotifyPlaybackChanged();
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _session.ReportAnimationFailure(candidate, exception.Message);
            AnimationScanStatus = string.Format(
                LocalizationManager.Instance.Get(
                    "AnimationWorkbench.AnimationPicker.Failed"),
                exception.Message);
        }
        finally
        {
            if (generation == _animationGeneration)
            {
                Interlocked.CompareExchange(
                    ref _animationCancellation,
                    null,
                    cancellation);
                IsAnimationScanRunning = false;
            }
            cancellation.Dispose();
        }
    }

    private TrustedAnimationCandidate CreateAnimationCandidate(
        PackFile file,
        AnimationFile? animation)
    {
        var container = _packFileService.GetPackFileContainer(file);
        var path = _packFileService.GetFullPath(file, container);
        var partCount = animation?.AnimationParts.Count ?? 0;
        var hasStaticFrame = animation?.AnimationParts.Any(part =>
            part.StaticFrame != null) == true;
        var isStaticPose = partCount > 0 && hasStaticFrame &&
            animation!.AnimationParts.All(part =>
                part.DynamicFrames.Count == 0);
        return new TrustedAnimationCandidate(
            file,
            Path.GetFileNameWithoutExtension(path),
            path,
            container?.Name ?? string.Empty,
            container?.SystemFilePath ?? string.Empty,
            GetSourceRole(container),
            animation?.Header.Version ?? 0,
            partCount == 0
                ? 0
                : animation!.AnimationParts.Max(part =>
                    part.DynamicFrames.Count),
            animation?.Header.AnimationTotalPlayTimeInSec ?? 0,
            animation?.Header.FrameRate ?? 0,
            partCount,
            hasStaticFrame,
            isStaticPose);
    }

    private static TrustedAnimationModelSourceRole GetSourceRole(
        PackFileContainer? container)
    {
        if (container?.Role == PackFileContainerRole.ProjectWorkspace)
            return TrustedAnimationModelSourceRole.FolderProject;
        if (container?.Role == PackFileContainerRole.Reference)
            return TrustedAnimationModelSourceRole.ReferencePack;
        return TrustedAnimationModelSourceRole.CaPack;
    }

    [RelayCommand]
    private void TogglePlayback()
    {
        if (!HasAnimation)
            return;
        if (IsPlaying)
            _viewport.Pause();
        else
            _viewport.Play();
        NotifyPlaybackChanged();
    }

    [RelayCommand]
    private void PreviousFrame()
    {
        if (!HasAnimation)
            return;
        _viewport.PreviousFrame();
        NotifyPlaybackChanged();
    }

    [RelayCommand]
    private void NextFrame()
    {
        if (!HasAnimation)
            return;
        _viewport.NextFrame();
        NotifyPlaybackChanged();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Skeleton));
        OnPropertyChanged(nameof(Animation));
        OnPropertyChanged(nameof(AnimationPathText));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(MeshCount));
        OnPropertyChanged(nameof(HasModelDiagnostic));
        OnPropertyChanged(nameof(HasSkeletonDiagnostic));
        OnPropertyChanged(nameof(HasAnimationDiagnostic));
    }

    private void OnPlaybackChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(NotifyPlaybackChanged);
            return;
        }
        NotifyPlaybackChanged();
    }

    private void NotifyPlaybackChanged()
    {
        OnPropertyChanged(nameof(PlaybackState));
        OnPropertyChanged(nameof(HasAnimation));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsLooping));
        OnPropertyChanged(nameof(PlaybackMaximum));
        OnPropertyChanged(nameof(CurrentTimeSeconds));
        OnPropertyChanged(nameof(PlaybackSummary));
    }

    private void CancelModelDiscovery(bool showCancelledStatus)
    {
        Interlocked.Increment(ref _modelScanGeneration);
        var cancellation = Interlocked.Exchange(
            ref _modelScanCancellation,
            null);
        cancellation?.Cancel();
        IsModelScanRunning = false;
        if (showCancelledStatus)
        {
            ModelScanStatus = FormatModelScanStatus(
                "AnimationWorkbench.ModelPicker.Cancelled",
                ModelCandidates.Count);
        }
    }

    private void CancelAnimationWork(bool showCancelledStatus)
    {
        Interlocked.Increment(ref _animationGeneration);
        var cancellation = Interlocked.Exchange(
            ref _animationCancellation,
            null);
        cancellation?.Cancel();
        IsAnimationScanRunning = false;
        if (showCancelledStatus)
        {
            AnimationScanStatus = FormatAnimationScanStatus(
                "AnimationWorkbench.AnimationPicker.Cancelled",
                AnimationCandidates.Count);
        }
    }

    private bool MatchesModelSearch(object item)
    {
        if (item is not TrustedAnimationModelCandidate candidate)
            return false;
        var query = ModelSearchText.Trim();
        return query.Length == 0 ||
            candidate.Path.Contains(
                query,
                StringComparison.OrdinalIgnoreCase) ||
            candidate.SourcePack.Contains(
                query,
                StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesAnimationSearch(object item)
    {
        if (item is not TrustedAnimationCandidate candidate)
            return false;
        var query = AnimationSearchText.Trim();
        return query.Length == 0 ||
            candidate.Name.Contains(
                query,
                StringComparison.OrdinalIgnoreCase) ||
            candidate.Path.Contains(
                query,
                StringComparison.OrdinalIgnoreCase) ||
            candidate.SourcePack.Contains(
                query,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatModelScanStatus(
        string key,
        int count) => string.Format(
            LocalizationManager.Instance.Get(key),
            count);

    private static string FormatAnimationScanStatus(
        string key,
        int count) => string.Format(
            LocalizationManager.Instance.Get(key),
            count);
}
