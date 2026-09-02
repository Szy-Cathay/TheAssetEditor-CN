using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public partial class TrustedAnimationPreviewViewModel :
    ObservableObject,
    IEditorInterface,
    IFileEditor
{
    private readonly ITrustedAnimationPreviewViewport _viewport;
    private readonly TrustedAnimationPreviewFeatureSession _session;
    private readonly ITrustedAnimationModelDiscovery _modelDiscovery;
    private CancellationTokenSource? _modelScanCancellation;
    private int _modelScanGeneration;
    private bool _closed;

    [ObservableProperty]
    private bool _showModel = true;

    [ObservableProperty]
    private bool _showSkeleton = true;

    [ObservableProperty]
    private bool _isModelPickerOpen = true;

    [ObservableProperty]
    private bool _isModelScanRunning;

    [ObservableProperty]
    private string _modelSearchText = string.Empty;

    [ObservableProperty]
    private string _modelScanStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseSelectedModelCommand))]
    private TrustedAnimationModelCandidate? _selectedModelCandidate;

    public TrustedAnimationPreviewViewModel(
        ITrustedAnimationPreviewViewport viewport,
        IPackFileService packFileService,
        ITrustedAnimationModelDiscovery modelDiscovery)
    {
        _viewport = viewport;
        _modelDiscovery = modelDiscovery;
        _session = new TrustedAnimationPreviewFeatureSession(
            viewport,
            packFileService);
        _session.StateChanged += OnStateChanged;
        DisplayName = LocalizationManager.Instance.Get(
            "DisplayName.AnimationWorkbench");
        ModelScanStatus = LocalizationManager.Instance.Get(
            "AnimationWorkbench.ModelPicker.Ready");
        ModelCandidatesView = CollectionViewSource.GetDefaultView(
            ModelCandidates);
        ModelCandidatesView.GroupDescriptions.Add(
            new PropertyGroupDescription(
                nameof(TrustedAnimationModelCandidate.SourceGroup)));
        ModelCandidatesView.Filter = MatchesModelSearch;
    }

    public string DisplayName { get; set; }

    public PackFile CurrentFile { get; private set; } = null!;

    public IWpfGame GameWorld => _viewport.GameWorld;

    public TrustedAnimationPreviewResourceState Model =>
        _session.State.Model;

    public TrustedAnimationPreviewResourceState Skeleton =>
        _session.State.Skeleton;

    public TrustedAnimationPreviewResourceState Animation =>
        _session.State.Animation;

    public bool IsReady => _session.State.IsReady;

    public int MeshCount => _session.State.MeshCount;

    public ObservableCollection<TrustedAnimationModelCandidate>
        ModelCandidates { get; } = [];

    public ICollectionView ModelCandidatesView { get; }

    public bool HasModelDiagnostic =>
        !string.IsNullOrWhiteSpace(Model.Diagnostic);

    public bool HasSkeletonDiagnostic =>
        !string.IsNullOrWhiteSpace(Skeleton.Diagnostic);

    public bool HasAnimationDiagnostic =>
        !string.IsNullOrWhiteSpace(Animation.Diagnostic);

    public void LoadFile(PackFile file)
    {
        CancelModelDiscovery(false);
        IsModelPickerOpen = false;
        SelectedModelCandidate = null;
        CurrentFile = file;
        _session.LoadModel(file);
    }

    public void Close()
    {
        _closed = true;
        CancelModelDiscovery(false);
        _session.StateChanged -= OnStateChanged;
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

                using (ModelCandidatesView.DeferRefresh())
                {
                    foreach (var candidate in batch)
                        ModelCandidates.Add(candidate);
                }
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

    [RelayCommand]
    private void FocusModel() => _viewport.FocusModel();

    [RelayCommand]
    private void ShowFront() => _viewport.ShowFront();

    [RelayCommand]
    private void ResetCamera() => _viewport.ResetCamera();

    [RelayCommand]
    private async Task OpenModelPicker()
    {
        IsModelPickerOpen = true;
        await StartModelDiscoveryAsync();
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
    private void UseSelectedModel()
    {
        var candidate = SelectedModelCandidate;
        if (candidate == null)
            return;
        LoadFile(candidate.File);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Skeleton));
        OnPropertyChanged(nameof(Animation));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(MeshCount));
        OnPropertyChanged(nameof(HasModelDiagnostic));
        OnPropertyChanged(nameof(HasSkeletonDiagnostic));
        OnPropertyChanged(nameof(HasAnimationDiagnostic));
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

    private static string FormatModelScanStatus(
        string key,
        int count) => string.Format(
            LocalizationManager.Instance.Get(key),
            count);
}
