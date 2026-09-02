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

    [ObservableProperty]
    private bool _showModel = true;

    [ObservableProperty]
    private bool _showSkeleton = true;

    public TrustedAnimationPreviewViewModel(
        ITrustedAnimationPreviewViewport viewport,
        IPackFileService packFileService)
    {
        _viewport = viewport;
        _session = new TrustedAnimationPreviewFeatureSession(
            viewport,
            packFileService);
        _session.StateChanged += OnStateChanged;
        DisplayName = LocalizationManager.Instance.Get(
            "DisplayName.AnimationWorkbench");
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

    public bool HasModelDiagnostic =>
        !string.IsNullOrWhiteSpace(Model.Diagnostic);

    public bool HasSkeletonDiagnostic =>
        !string.IsNullOrWhiteSpace(Skeleton.Diagnostic);

    public bool HasAnimationDiagnostic =>
        !string.IsNullOrWhiteSpace(Animation.Diagnostic);

    public void LoadFile(PackFile file)
    {
        CurrentFile = file;
        _session.LoadModel(file);
    }

    public void Close()
    {
        _session.StateChanged -= OnStateChanged;
        _viewport.Dispose();
    }

    partial void OnShowModelChanged(bool value) =>
        _viewport.SetModelVisible(value);

    partial void OnShowSkeletonChanged(bool value) =>
        _viewport.SetSkeletonVisible(value);

    [RelayCommand]
    private void FocusModel() => _viewport.FocusModel();

    [RelayCommand]
    private void ShowFront() => _viewport.ShowFront();

    [RelayCommand]
    private void ResetCamera() => _viewport.ResetCamera();

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
}
