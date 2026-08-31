using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed class AnimationWorkbenchBoneTreeNode : INotifyPropertyChanged
{
    private readonly Action<AnimationWorkbenchBoneTreeNode, bool>
        _selectionChanged;
    private bool _isSelected;
    private bool _isVisible = true;
    private bool _isExpanded;

    internal AnimationWorkbenchBoneTreeNode(
        int index,
        string name,
        Action<AnimationWorkbenchBoneTreeNode, bool> selectionChanged)
    {
        Index = index;
        Name = name;
        _selectionChanged = selectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public string Name { get; }

    public ObservableCollection<AnimationWorkbenchBoneTreeNode> Children
    {
        get;
    } = [];

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _selectionChanged(this, value);
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetField(ref _isVisible, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        internal set => SetField(ref _isExpanded, value);
    }

    internal void SetSelected(bool value) =>
        SetField(ref _isSelected, value, nameof(IsSelected));

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AnimationWorkbenchLayerController : INotifyPropertyChanged
{
    private readonly AnimationWorkbenchDocument _document;
    private readonly AnimationWorkbenchBoneMaskStore _maskStore;
    private readonly AnimationWorkbenchBoneHierarchy _hierarchy;
    private readonly IReadOnlyList<AnimationWorkbenchBoneTreeNode> _allBones;
    private readonly long _documentGeneration;
    private AnimationWorkbenchLayerMode _mode;
    private AnimationWorkbenchAdditiveReferencePose _referencePose;
    private double _weight = 1;
    private string _searchText = "";
    private AnimationWorkbenchLayerResult? _lastResult;
    private AnimationWorkbenchBoneMaskStoreResult? _lastMaskStoreResult;
    private IReadOnlyList<string> _savedMaskNames = [];
    private long _ownedPreviewVersion;
    private bool _isChangingSelection;

    public AnimationWorkbenchLayerController(
        AnimationWorkbenchDocument document,
        AnimationWorkbenchBoneMaskStore? maskStore = null)
    {
        _document = document ?? throw new ArgumentNullException(
            nameof(document));
        _maskStore = maskStore ?? new AnimationWorkbenchBoneMaskStore();
        _documentGeneration = document.GetState().DocumentGeneration;
        _hierarchy = document.GetBoneHierarchy();
        var nodes = _hierarchy.Bones.ToDictionary(
            item => item.Index,
            item => new AnimationWorkbenchBoneTreeNode(
                item.Index,
                item.Name,
                SetSubtreeSelection));
        foreach (var item in _hierarchy.Bones)
        {
            if (item.ParentIndex >= 0 &&
                item.ParentIndex != item.Index &&
                nodes.TryGetValue(item.ParentIndex, out var parent))
            {
                parent.Children.Add(nodes[item.Index]);
            }
            else
            {
                RootBones.Add(nodes[item.Index]);
            }
        }
        _allBones = _hierarchy.Bones
            .OrderBy(item => item.Index)
            .Select(item => nodes[item.Index])
            .ToArray();
        foreach (var node in _allBones)
            node.SetSelected(true);

        var listed = _maskStore.List(_hierarchy);
        _savedMaskNames = listed.MaskNames;
        RefreshPreview();
        if (listed.Succeeded == false)
        {
            _lastMaskStoreResult = listed;
            RaiseStateChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Changed;

    public ObservableCollection<AnimationWorkbenchBoneTreeNode> RootBones
    {
        get;
    } = [];

    public AnimationWorkbenchLayerMode Mode
    {
        get => _mode;
        set => SetAndRefresh(ref _mode, value);
    }

    public AnimationWorkbenchAdditiveReferencePose ReferencePose
    {
        get => _referencePose;
        set => SetAndRefresh(ref _referencePose, value);
    }

    public double Weight
    {
        get => _weight;
        set => SetAndRefresh(ref _weight, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= "";
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
                return;
            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public int SelectedBoneCount => _allBones.Count(item => item.IsSelected);

    public int TotalBoneCount => _allBones.Count;

    public IReadOnlyList<string> SavedMaskNames => _savedMaskNames;

    public AnimationWorkbenchLayerResult? LastResult => _lastResult;

    public AnimationWorkbenchLayerImpact? Impact => _lastResult?.Impact;

    public IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics =>
        _lastMaskStoreResult?.Succeeded == false
            ? _lastMaskStoreResult.Diagnostics
            : _lastResult?.Diagnostics ??
              Array.Empty<AnimationWorkbenchDiagnostic>();

    public bool HasActivePreview =>
        _document.IsClosed == false &&
        IsCurrentDocumentGeneration &&
        _ownedPreviewVersion != 0 &&
        _document.ActiveLayerPreviewVersion == _ownedPreviewVersion;

    public bool CanCommit => _lastResult?.Succeeded == true && HasActivePreview;

    public AnimationWorkbenchLayerResult RefreshPreview()
    {
        if (IsCurrentDocumentGeneration == false)
            return CreateDocumentChangedResult();
        _lastMaskStoreResult = null;
        _document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        var mask = new AnimationWorkbenchBoneMask(
            _hierarchy.SkeletonName,
            _allBones
                .Where(item => item.IsSelected)
                .Select(item => item.Name)
                .ToArray());
        _lastResult = _document.PreviewLayer(
            new AnimationWorkbenchLayerRequest(
                mask,
                _mode,
                _weight,
                _referencePose));
        _ownedPreviewVersion = _lastResult.Succeeded &&
            _lastResult.State.HasActiveLayerPreview
            ? _document.ActiveLayerPreviewVersion
            : 0;
        RaiseStateChanged();
        return _lastResult;
    }

    public AnimationWorkbenchBoneMaskStoreResult SaveMask(string name)
    {
        if (IsCurrentDocumentGeneration == false)
            return CreateDocumentChangedStoreResult();
        var mask = new AnimationWorkbenchBoneMask(
            _hierarchy.SkeletonName,
            _allBones
                .Where(item => item.IsSelected)
                .Select(item => item.Name)
                .ToArray());
        _lastMaskStoreResult = _maskStore.Save(
            name,
            _hierarchy,
            mask);
        if (_lastMaskStoreResult.Succeeded)
            _savedMaskNames = _lastMaskStoreResult.MaskNames;
        RaiseStateChanged();
        return _lastMaskStoreResult;
    }

    public AnimationWorkbenchBoneMaskStoreResult LoadMask(string name)
    {
        if (IsCurrentDocumentGeneration == false)
            return CreateDocumentChangedStoreResult();
        var loaded = _maskStore.Load(name, _hierarchy);
        _lastMaskStoreResult = loaded;
        if (loaded.Succeeded && loaded.Mask != null)
        {
            _savedMaskNames = loaded.MaskNames;
            var selectedNames = loaded.Mask.BoneNames
                .ToHashSet(StringComparer.Ordinal);
            _isChangingSelection = true;
            try
            {
                foreach (var node in _allBones)
                    node.SetSelected(selectedNames.Contains(node.Name));
            }
            finally
            {
                _isChangingSelection = false;
            }
            RefreshPreview();
        }
        else
        {
            RaiseStateChanged();
        }
        return loaded;
    }

    public AnimationWorkbenchLayerResult CommitPreview()
    {
        if (IsCurrentDocumentGeneration == false)
            return CreateDocumentChangedResult();
        if (HasActivePreview == false)
            return CreateMissingPreviewResult();
        _lastResult = _document.CommitLayerPreview();
        _ownedPreviewVersion = 0;
        RaiseStateChanged();
        return _lastResult;
    }

    public AnimationWorkbenchLayerResult CancelPreview()
    {
        if (IsCurrentDocumentGeneration == false)
            return CreateDocumentChangedResult();
        if (HasActivePreview == false)
            return CreateMissingPreviewResult();
        _lastResult = _document.CancelLayerPreview();
        _ownedPreviewVersion = 0;
        RaiseStateChanged();
        return _lastResult;
    }

    public AnimationWorkbenchLayerResult? ReleasePreview()
    {
        if (_document.IsClosed ||
            IsCurrentDocumentGeneration == false ||
            HasActivePreview == false)
            return null;
        _lastResult = _document.CancelLayerPreview();
        _ownedPreviewVersion = 0;
        RaiseStateChanged();
        return _lastResult;
    }

    private void SetSubtreeSelection(
        AnimationWorkbenchBoneTreeNode node,
        bool value)
    {
        if (_isChangingSelection)
            return;
        _isChangingSelection = true;
        try
        {
            SetSubtreeSelectionCore(node, value);
        }
        finally
        {
            _isChangingSelection = false;
        }
        OnPropertyChanged(nameof(SelectedBoneCount));
        RefreshPreview();
    }

    private static void SetSubtreeSelectionCore(
        AnimationWorkbenchBoneTreeNode node,
        bool value)
    {
        node.SetSelected(value);
        foreach (var child in node.Children)
            SetSubtreeSelectionCore(child, value);
    }

    private void ApplyFilter()
    {
        foreach (var root in RootBones)
            ApplyFilter(root, _searchText.Trim());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool ApplyFilter(
        AnimationWorkbenchBoneTreeNode node,
        string query)
    {
        var childVisible = false;
        foreach (var child in node.Children)
            childVisible |= ApplyFilter(child, query);
        var matches = query.Length == 0 ||
            node.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        node.IsVisible = matches || childVisible;
        node.IsExpanded = query.Length != 0 && childVisible;
        return node.IsVisible;
    }

    private AnimationWorkbenchLayerResult CreateMissingPreviewResult()
    {
        _ownedPreviewVersion = 0;
        _lastResult = new AnimationWorkbenchLayerResult(
            false,
            _document.GetState(),
            null,
            [new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerPreviewMissing,
                AnimationWorkbenchDiagnosticSeverity.Error)]);
        RaiseStateChanged();
        return _lastResult;
    }

    private bool IsCurrentDocumentGeneration =>
        _document.GetState().DocumentGeneration == _documentGeneration;

    private AnimationWorkbenchLayerResult CreateDocumentChangedResult()
    {
        _ownedPreviewVersion = 0;
        _lastMaskStoreResult = null;
        _lastResult = new AnimationWorkbenchLayerResult(
            false,
            _document.GetState(),
            null,
            [new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerDocumentChanged,
                AnimationWorkbenchDiagnosticSeverity.Error)]);
        RaiseStateChanged();
        return _lastResult;
    }

    private AnimationWorkbenchBoneMaskStoreResult
        CreateDocumentChangedStoreResult()
    {
        _ownedPreviewVersion = 0;
        _lastMaskStoreResult = new AnimationWorkbenchBoneMaskStoreResult(
            false,
            null,
            [],
            [new AnimationWorkbenchDiagnostic(
                AnimationWorkbenchDiagnosticCode.LayerDocumentChanged,
                AnimationWorkbenchDiagnosticSeverity.Error)]);
        RaiseStateChanged();
        return _lastMaskStoreResult;
    }

    private void SetAndRefresh<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        OnPropertyChanged(propertyName);
        RefreshPreview();
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(LastResult));
        OnPropertyChanged(nameof(Impact));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(HasActivePreview));
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(SelectedBoneCount));
        OnPropertyChanged(nameof(SavedMaskNames));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
