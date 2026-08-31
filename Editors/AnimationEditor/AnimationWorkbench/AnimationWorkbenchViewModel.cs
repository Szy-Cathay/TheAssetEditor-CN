using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Shared.Core.Common.AnimationPlayer;
using GameWorld.Core.Animation;
using GameWorld.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum AnimationWorkbenchPanelKind
{
    Issues,
    Blend,
    Layer,
    Retarget,
    MetaData,
}

public sealed record AnimationWorkbenchSourceItem(
    string Slot,
    string Name,
    string Details,
    bool IsLoaded);

public sealed partial class AnimationWorkbenchViewModel :
    ObservableObject,
    IEditorInterface,
    IFileEditor,
    ISaveableEditor
{
    private readonly IAnimationWorkbenchViewport _viewport;
    private readonly IPackFileService _packFileService;
    private readonly ISkeletonAnimationLookUpHelper _skeletonLookup;
    private readonly IStandardDialogs _dialogs;
    private readonly AnimationWorkbenchDocument _document;
    private readonly List<string> _shellDiagnostics = [];
    private readonly string _gameBoundaryMessageKey;
    private AnimationWorkbenchSourceInput? _animationA;
    private AnimationWorkbenchSourceInput? _animationB;
    private GameSkeleton? _targetSkeleton;
    private AnimationWorkbenchTimelineController _timelineController;
    private AnimationWorkbenchMetaDataController _metaDataController;
    private AnimationWorkbenchBlendController? _blendController;
    private AnimationWorkbenchLayerController? _layerController;
    private AnimationWorkbenchRetargetController? _retargetController;
    private AnimationWorkbenchPanelKind _activePanel;
    private string _statusText;
    private string _saveUnavailableReason;
    private bool _hasUnsavedChanges;
    private bool _closed;

    public AnimationWorkbenchViewModel(
        IAnimationWorkbenchViewport viewport,
        IPackFileService packFileService,
        ISkeletonAnimationLookUpHelper skeletonLookup,
        IStandardDialogs dialogs,
        ApplicationSettingsService settingsService)
    {
        _viewport = viewport;
        _packFileService = packFileService;
        _skeletonLookup = skeletonLookup;
        _dialogs = dialogs;
        IsWarhammer3 = settingsService.CurrentSettings.CurrentGame ==
            GameTypeEnum.Warhammer3;
        DisplayName = Localize("DisplayName.AnimationWorkbench");
        _gameBoundaryMessageKey = settingsService.CurrentSettings.CurrentGame ==
            GameTypeEnum.ThreeKingdoms
                ? "AnimationWorkbench.Shell.ThreeKingdomsUnavailable"
                : "AnimationWorkbench.Shell.Warhammer3Only";
        _statusText = Localize(IsWarhammer3
            ? "AnimationWorkbench.Shell.EmptyStatus"
            : _gameBoundaryMessageKey);
        _saveUnavailableReason = Localize(
            "AnimationWorkbench.Shell.SaveUnavailable");
        _document = new AnimationWorkbenchDocument(_viewport);
        _timelineController = new AnimationWorkbenchTimelineController(
            _document);
        _metaDataController = new AnimationWorkbenchMetaDataController(
            _document);
        SubscribeControllers();
        RefreshState();
    }

    public string DisplayName { get; set; }

    public PackFile CurrentFile { get; private set; } = null!;

    public bool IsWarhammer3 { get; }

    public bool IsWorkbenchEnabled => IsWarhammer3;

    public bool CanEdit => IsWarhammer3 &&
        _animationA != null &&
        _targetSkeleton != null &&
        CanEditSource(_animationA) &&
        (_animationB == null || CanEditSource(_animationB));

    public bool CanBrowseAnimationB =>
        IsWarhammer3 && _animationA != null;

    public bool CanSelectAnimationB => _animationB != null;

    public bool CanSelectResult =>
        _document.GetState().Result != null;

    public bool CanSave => CanEdit &&
        !HasActiveEditPreview(_document.GetState()) &&
        _packFileService.GetEditablePack() is FolderProjectContainer;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SaveUnavailableReason
    {
        get => _saveUnavailableReason;
        private set => SetProperty(ref _saveUnavailableReason, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set => SetProperty(ref _hasUnsavedChanges, value);
    }

    public AnimationWorkbenchPanelKind ActivePanel
    {
        get => _activePanel;
        private set => SetProperty(ref _activePanel, value);
    }

    public IWpfGame GameWorld => _viewport.GameWorld;

    public AnimationPlayerViewModel Player => _viewport.Player;

    public ObservableCollection<AnimationWorkbenchSourceItem> Sources
    {
        get;
    } = [];

    public ObservableCollection<string> BoneNames { get; } = [];

    public ObservableCollection<string> Diagnostics { get; } = [];

    public AnimationWorkbenchTimelineController TimelineController
    {
        get => _timelineController;
        private set => SetProperty(ref _timelineController, value);
    }

    public AnimationWorkbenchMetaDataController MetaDataController
    {
        get => _metaDataController;
        private set => SetProperty(ref _metaDataController, value);
    }

    public AnimationWorkbenchBlendController? BlendController
    {
        get => _blendController;
        private set => SetProperty(ref _blendController, value);
    }

    public AnimationWorkbenchLayerController? LayerController
    {
        get => _layerController;
        private set => SetProperty(ref _layerController, value);
    }

    public AnimationWorkbenchRetargetController? RetargetController
    {
        get => _retargetController;
        private set => SetProperty(ref _retargetController, value);
    }

    public void LoadFile(PackFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        CurrentFile = file;
        if (!IsWarhammer3)
        {
            RefreshState();
            return;
        }

        LoadSource(file, AnimationWorkbenchSourceSlot.AnimationA);
    }

    public void ActivatePanel(AnimationWorkbenchPanelKind panel)
    {
        if (_closed || ActivePanel == panel)
            return;

        ReleaseActivePanelPreview();
        ActivePanel = panel;
        if (panel is AnimationWorkbenchPanelKind.Issues or
            AnimationWorkbenchPanelKind.MetaData)
        {
            RefreshState();
            return;
        }
        if (!CanEdit)
        {
            StatusText = Localize(
                "AnimationWorkbench.Shell.EditUnavailable");
            return;
        }

        switch (panel)
        {
            case AnimationWorkbenchPanelKind.Blend:
                BlendController = new AnimationWorkbenchBlendController(
                    _document);
                BlendController.Changed += Controller_Changed;
                break;
            case AnimationWorkbenchPanelKind.Layer:
                LayerController = new AnimationWorkbenchLayerController(
                    _document);
                LayerController.Changed += Controller_Changed;
                break;
            case AnimationWorkbenchPanelKind.Retarget:
                RetargetController =
                    new AnimationWorkbenchRetargetController(
                        _document,
                        AnimationWorkbenchSourceSlot.AnimationA);
                RetargetController.Changed += Controller_Changed;
                break;
        }
        RefreshState();
    }

    [RelayCommand]
    private void BrowseAnimationA()
    {
        var result = _dialogs.DisplayBrowseDialog([".anim"]);
        if (result.Result && result.File != null)
        {
            CurrentFile = result.File;
            LoadSource(
                result.File,
                AnimationWorkbenchSourceSlot.AnimationA);
        }
    }

    [RelayCommand]
    private void BrowseAnimationB()
    {
        if (!CanBrowseAnimationB)
            return;
        var result = _dialogs.DisplayBrowseDialog([".anim"]);
        if (result.Result && result.File != null)
        {
            LoadSource(
                result.File,
                AnimationWorkbenchSourceSlot.AnimationB);
        }
    }

    [RelayCommand]
    private void BrowseTargetSkeleton()
    {
        if (!IsWarhammer3)
            return;
        var result = _dialogs.DisplayBrowseDialog([".anim"]);
        if (!result.Result || result.File == null)
            return;

        try
        {
            _targetSkeleton = new GameSkeleton(
                AnimationFile.Create(result.File),
                new AnimationPlayer());
            _shellDiagnostics.Clear();
            ReloadDocument();
        }
        catch (Exception)
        {
            SetShellFailure(
                "AnimationWorkbench.Shell.TargetSkeletonInvalid");
        }
    }

    [RelayCommand]
    private void SelectAnimationA() => SelectPreview(
        AnimationWorkbenchPreviewKind.AnimationA);

    [RelayCommand]
    private void SelectAnimationB() => SelectPreview(
        AnimationWorkbenchPreviewKind.AnimationB);

    [RelayCommand]
    private void SelectResult() => SelectPreview(
        AnimationWorkbenchPreviewKind.Result);

    [RelayCommand]
    private void SaveAsNewResource() => Save();

    public bool Save()
    {
        if (!CanSave ||
            _packFileService.GetEditablePack() is not
                FolderProjectContainer project)
        {
            StatusText = SaveUnavailableReason;
            return false;
        }

        var dialog = _dialogs.DisplaySaveDialog(
            _packFileService,
            [".anim"]);
        if (!dialog.Result ||
            string.IsNullOrWhiteSpace(dialog.SelectedFilePath))
        {
            return false;
        }

        var path = dialog.SelectedFilePath;
        if (!path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            path += ".anim";
        var result = _document.SaveAsNewProjectResource(
            _packFileService,
            project,
            path);
        RefreshState();
        StatusText = result.Succeeded
            ? Localize("AnimationWorkbench.Shell.SaveSucceeded")
            : Localize(result.Diagnostics[0].ReasonKey);
        return result.Succeeded;
    }

    public void Close()
    {
        if (_closed)
            return;
        _closed = true;
        ReleaseActivePanelPreview();
        _document.Dispose();
    }

    private void LoadSource(
        PackFile file,
        AnimationWorkbenchSourceSlot slot)
    {
        try
        {
            var parsed = AnimationFile.Create(file);
            var skeletonFile = _skeletonLookup.GetSkeletonFileFromName(
                parsed.Header.SkeletonName);
            if (skeletonFile == null)
            {
                SetShellFailure(
                    "AnimationWorkbench.Shell.SourceSkeletonMissing");
                return;
            }

            var skeleton = new GameSkeleton(
                skeletonFile,
                new AnimationPlayer());
            var source = new AnimationWorkbenchSourceInput(
                _packFileService.GetFullPath(file),
                new AnimationClip(parsed, skeleton),
                skeleton,
                AnimationWorkbenchSourceFormat.FromFile(parsed));
            if (slot == AnimationWorkbenchSourceSlot.AnimationA)
            {
                _animationA = source;
                _targetSkeleton = skeleton.Clone(new AnimationPlayer());
            }
            else
            {
                _animationB = source;
            }
            _shellDiagnostics.Clear();
            ReloadDocument();
        }
        catch (Exception)
        {
            SetShellFailure("AnimationWorkbench.Shell.AnimationLoadFailed");
        }
    }

    private void ReloadDocument()
    {
        var selectedPanel = ActivePanel;
        ReleaseActivePanelPreview();
        _document.Load(new AnimationWorkbenchLoadRequest(
            _animationA,
            _animationB,
            GameTypeEnum.Warhammer3,
            _targetSkeleton));
        TimelineController.Changed -= Controller_Changed;
        MetaDataController.Changed -= Controller_Changed;
        TimelineController = new AnimationWorkbenchTimelineController(
            _document);
        MetaDataController = new AnimationWorkbenchMetaDataController(
            _document);
        SubscribeControllers();
        ActivePanel = AnimationWorkbenchPanelKind.Issues;
        BlendController = null;
        LayerController = null;
        RetargetController = null;
        RefreshState();
        if (selectedPanel != AnimationWorkbenchPanelKind.Issues)
            ActivatePanel(selectedPanel);
    }

    private void SelectPreview(AnimationWorkbenchPreviewKind kind)
    {
        if (_closed)
            return;
        _document.SelectPreview(kind);
        RefreshState();
    }

    private void SubscribeControllers()
    {
        TimelineController.Changed += Controller_Changed;
        MetaDataController.Changed += Controller_Changed;
    }

    private void Controller_Changed(object? sender, EventArgs e) =>
        RefreshState();

    private void ReleaseActivePanelPreview()
    {
        BlendController?.ReleasePreview();
        LayerController?.ReleasePreview();
        RetargetController?.ReleasePreview();
    }

    private void SetShellFailure(string key)
    {
        _shellDiagnostics.Clear();
        _shellDiagnostics.Add(Localize(key));
        RefreshState();
    }

    private void RefreshState()
    {
        var state = _document.GetState();
        Sources.Clear();
        Sources.Add(CreateSourceItem(
            Localize("AnimationWorkbench.Shell.SourceSlotA"),
            state.AnimationA,
            "AnimationWorkbench.Shell.AnimationAMissing"));
        Sources.Add(CreateSourceItem(
            Localize("AnimationWorkbench.Shell.SourceSlotB"),
            state.AnimationB,
            "AnimationWorkbench.Shell.AnimationBOptional"));

        BoneNames.Clear();
        if (_targetSkeleton != null)
        {
            foreach (var boneName in _targetSkeleton.BoneNames)
                BoneNames.Add(boneName);
        }

        Diagnostics.Clear();
        foreach (var diagnostic in _shellDiagnostics)
            Diagnostics.Add(diagnostic);
        if (!IsWarhammer3)
        {
            Diagnostics.Add(Localize(_gameBoundaryMessageKey));
        }
        foreach (var diagnostic in state.Diagnostics)
            Diagnostics.Add(Localize(diagnostic.ReasonKey));
        AddFormatDiagnostics(_animationA);
        AddFormatDiagnostics(_animationB);

        HasUnsavedChanges = state.IsDirty;
        SaveUnavailableReason = GetSaveUnavailableReason(state);
        if (Diagnostics.Count != 0)
            StatusText = Diagnostics[0];
        else if (!state.IsDirty)
            StatusText = Localize("AnimationWorkbench.Shell.ReadyStatus");

        OnPropertyChanged(nameof(IsWorkbenchEnabled));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanBrowseAnimationB));
        OnPropertyChanged(nameof(CanSelectAnimationB));
        OnPropertyChanged(nameof(CanSelectResult));
        OnPropertyChanged(nameof(CanSave));
    }

    private void AddFormatDiagnostics(AnimationWorkbenchSourceInput? source)
    {
        if (source?.Format == null)
            return;
        var capabilities = AnimationFormatCapabilities.Evaluate(
            source.Format.Version,
            source.Format.PartCount);
        foreach (var reason in capabilities.BlockingReasons)
        {
            Diagnostics.Add(Localize(reason switch
            {
                AnimationFormatBlockReason.UnsupportedVersion =>
                    "AnimationWorkbench.Diagnostic.SourceFormatUnsupported",
                AnimationFormatBlockReason.VersionEightIsReadOnly =>
                    "AnimationWorkbench.Diagnostic.SourceVersionEightReadOnly",
                AnimationFormatBlockReason.MultiplePartsAreReadOnly =>
                    "AnimationWorkbench.Diagnostic.SourceMultiplePartsReadOnly",
                _ => throw new ArgumentOutOfRangeException(),
            }));
        }
        if (source.Format.HasStaticFrame)
        {
            Diagnostics.Add(Localize(
                "AnimationWorkbench.Diagnostic.SourceStaticFrameReadOnly"));
        }
    }

    private string GetSaveUnavailableReason(
        AnimationWorkbenchDocumentState state)
    {
        if (!IsWarhammer3)
            return Localize("AnimationWorkbench.Shell.Warhammer3Only");
        if (_animationA == null)
        {
            return Localize(
                "AnimationWorkbench.Shell.AnimationAMissing");
        }
        if (_targetSkeleton == null)
        {
            return Localize(
                "AnimationWorkbench.Diagnostic.TargetSkeletonMissing");
        }
        if (!CanEdit)
            return Localize("AnimationWorkbench.Shell.SaveFormatReadOnly");
        if (HasActiveEditPreview(state))
        {
            return Localize(
                "AnimationWorkbench.Shell.ApplyOrCancelPreview");
        }
        if (_packFileService.GetEditablePack() is not FolderProjectContainer)
        {
            return Localize(
                "AnimationWorkbench.Shell.FolderProjectRequired");
        }
        return Localize("AnimationWorkbench.Shell.SaveReady");
    }

    private static bool CanEditSource(
        AnimationWorkbenchSourceInput source)
    {
        if (source.Format == null || source.Format.HasStaticFrame)
            return false;
        return AnimationFormatCapabilities.Evaluate(
            source.Format.Version,
            source.Format.PartCount).CanEdit;
    }

    private static bool HasActiveEditPreview(
        AnimationWorkbenchDocumentState state) =>
        state.HasActivePosePreview ||
        state.HasActiveTimelinePreview ||
        state.HasActiveBlendPreview ||
        state.HasActiveLayerPreview ||
        state.HasActiveRetargetPreview;

    private static AnimationWorkbenchSourceItem CreateSourceItem(
        string slot,
        AnimationWorkbenchSourceSummary? summary,
        string emptyKey) => summary == null
            ? new AnimationWorkbenchSourceItem(
                slot,
                Localize(emptyKey),
                "",
                false)
            : new AnimationWorkbenchSourceItem(
                slot,
                summary.Name,
                LocalizationManager.Instance.GetFormat(
                    "AnimationWorkbench.Shell.SourceDetailsFormat",
                    summary.FrameCount,
                    summary.Duration.TotalSeconds,
                    summary.SkeletonName),
                true);

    private static string Localize(string key) =>
        LocalizationManager.Instance.Get(key);
}
