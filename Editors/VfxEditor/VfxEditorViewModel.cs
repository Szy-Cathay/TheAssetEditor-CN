using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using GameWorld.Core.Vfx;
using GameWorld.Core.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Xna.Framework;
using GameWorld.Core.Components.Gizmo;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Vfx;

namespace Editors.VfxEditor;

public sealed class VfxEditorViewModel : ObservableObject, IEditorInterface, IFileEditor, ISaveableEditor
{
    private readonly IPackFileService _files;
    private readonly IFileSaveService _saveService;
    private readonly IStandardDialogs _dialogs;
    private readonly IEditorCreator _editors;
    private readonly VfxScenePreview? _scenePreview;
    private readonly VfxPreviewDocumentStore? _livePreviews;
    private readonly GameWeatherStore? _weather;
    private string _weatherError = "";
    private readonly DispatcherTimer _scenePreviewTimer;
    private readonly object _scenePreviewDependencyGate = new();
    private readonly HashSet<object> _scenePreviewDependencies = new(ReferenceEqualityComparer.Instance);
    private bool _updatingPreviewSelection;
    private bool? _playbackBeforeValidationError;
    private string _scenePreviewStatus = "";
    private IReadOnlyList<string> _scenePreviewDiagnostics = [];
    private readonly Stack<Edit> _undo = new();
    private readonly Stack<Edit> _redo = new();
    private VfxDocument? _document;
    private byte[] _loadedBytes = [];
    private int _revision;
    private int _savedRevision;
    private int _nextRevision;
    private string _displayName;
    private string _status = "";
    private string _search = "";
    private string _category = "Common";
    private string _quality = "0";
    private string _previewQuality = "0";
    private VfxLayerViewModel? _selectedLayer;
    private VfxFieldViewModel? _selectedField;
    private bool _hasUnsavedChanges;

    public LocalizationManager Localization { get; }
    public ObservableCollection<VfxLayerViewModel> Layers { get; } = [];
    public ObservableCollection<VfxFieldViewModel> VisibleFields { get; } = [];
    public IReadOnlyList<VfxChoice> Categories { get; }
    public ObservableCollection<VfxChoice> Qualities { get; } = [];
    public IReadOnlyList<VfxChoice> PreviewQualities { get; }
    public PackFile CurrentFile { get; private set; } = null!;
    public string FilePath => CurrentFile == null ? "" : _files.GetFullPath(CurrentFile);
    public bool HasDocument => _document != null;
    public bool IsEmpty => !HasDocument;
    public bool HasSelection => SelectedField != null;
    public bool HasNoFields => HasDocument && VisibleFields.Count == 0;
    public bool HasReferences => Layers.Any(x => x.Model.Kind == "Reference");
    public bool CanChangeLayer => !HasErrors && SelectedLayer?.Model.Kind is "Emitter" or "Reference";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public bool HasUnsavedChanges { get => _hasUnsavedChanges; set => SetProperty(ref _hasUnsavedChanges, value); }
    public bool HasErrors => Layers.Any(x => x.Fields.Any(f => f.HasErrors));
    public string Summary => Localization.GetFormat("Vfx.Summary", Layers.Count(x => x.Model.Kind == "Emitter"), Layers.Count(x => x.Model.Kind == "Reference"));
    public string FieldCount => Localization.GetFormat("Vfx.FieldCount", VisibleFields.Count, SelectedLayer?.Fields.Count ?? 0);
    public string CategoryHint => Localization.Get(string.IsNullOrWhiteSpace(Search) ? "Vfx.GoalHint." + Category : "Vfx.SearchAllHint");
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) FilterFields(); } }
    public string Category { get => _category; set { if (SetProperty(ref _category, value)) FilterFields(); } }
    public string Quality { get => _quality; set { if (SetProperty(ref _quality, value)) FilterFields(); } }
    public string PreviewQuality
    {
        get => _previewQuality;
        set
        {
            if (PreviewQualities.Any(choice => choice.Key == value) && SetProperty(ref _previewQuality, value))
                ScheduleScenePreview();
        }
    }
    public IWpfGame? PreviewWorld => _scenePreview?.Game;
    public VfxPlaybackViewModel Playback { get; }
    public bool HasPreviewOptions => Layers.Any(layer => layer.CanPreview);
    public bool HasPreviewSelection => Layers.Any(layer => layer.CanPreview && layer.IsIncludedInPreview);
    public string PreviewSelectionSummary => Localization.GetFormat("Vfx.Preview.SelectionSummary",
        Layers.Count(layer => layer.CanPreview && layer.IsIncludedInPreview), Layers.Count(layer => layer.CanPreview));
    public string ScenePreviewStatus { get => _scenePreviewStatus; private set => SetProperty(ref _scenePreviewStatus, value); }
    public IReadOnlyList<string> ScenePreviewDiagnostics { get => _scenePreviewDiagnostics; private set => SetProperty(ref _scenePreviewDiagnostics, value); }
    public RelayCommand PreviewAllCommand { get; }
    public RelayCommand PreviewNoneCommand { get; }
    public RelayCommand FrameSceneCommand { get; }
    public RelayCommand SelectEmitterCommand { get; }
    public RelayCommand MoveEmitterCommand { get; }
    public RelayCommand RotateEmitterCommand { get; }
    public RelayCommand ScaleEmitterCommand { get; }
    public RelayCommand ResetEmitterCommand { get; }
    public RelayCommand SelectWeatherCommand { get; }
    public RelayCommand ClearWeatherCommand { get; }
    public string WeatherStatus => _weatherError.Length > 0 ? _weatherError
        : _weather?.Current is { } weather ? Localization.GetFormat("Vfx.Preview.WeatherSource", weather.SourcePath)
        : Localization.Get("Vfx.Preview.WeatherNone");
    public VfxLayerViewModel? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (!SetProperty(ref _selectedLayer, value)) return;
            Qualities.Clear();
            Qualities.Add(new("All", Localization.Get("Vfx.Quality.All")));
            foreach (var quality in value?.Fields.Select(x => x.Model.Quality).Where(x => x.Length > 0).Distinct().Order().ToArray() ?? [])
                Qualities.Add(new(quality, Localization.GetFormat("Vfx.Quality", quality)));
            _quality = Qualities.Any(x => x.Key == "0") ? "0" : "All";
            OnPropertyChanged(nameof(Quality));
            FilterFields();
            OpenReferenceCommand.NotifyCanExecuteChanged();
            DuplicateLayerCommand.NotifyCanExecuteChanged();
            RemoveLayerCommand.NotifyCanExecuteChanged();
        }
    }
    public VfxFieldViewModel? SelectedField
    {
        get => _selectedField;
        set
        {
            if (SetProperty(ref _selectedField, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public RelayCommand OpenCommand { get; }
    public RelayCommand NewCompositionCommand { get; }
    public RelayCommand AddReferenceCommand { get; }
    public RelayCommand DuplicateLayerCommand { get; }
    public RelayCommand RemoveLayerCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand OpenReferenceCommand { get; }

    public VfxEditorViewModel(IPackFileService files, IFileSaveService saveService,
        IStandardDialogs dialogs, IEditorCreator editors, LocalizationManager localization,
        VfxScenePreview? scenePreview = null, VfxPreviewDocumentStore? livePreviews = null, GameWeatherStore? weather = null)
    {
        _files = files;
        _saveService = saveService;
        _dialogs = dialogs;
        _editors = editors;
        _scenePreview = scenePreview;
        _livePreviews = livePreviews;
        _weather = weather;
        if (_weather != null) _weather.Changed += WeatherChanged;
        SelectWeatherCommand = new RelayCommand(SelectWeather, () => _weather != null);
        ClearWeatherCommand = new RelayCommand(() => _weather?.Set(null), () => _weather != null);
        if (_livePreviews != null) _livePreviews.Changed += OnSharedPreviewChanged;
        // Continuous viewport rendering must not starve debounced preview updates.
        _scenePreviewTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(300) };
        _scenePreviewTimer.Tick += RefreshScenePreview;
        if (_scenePreview != null)
        {
            _scenePreview.Preview.Reloaded += ScenePreviewReloaded;
            _scenePreview.TransformCommitted += PreviewTransformCommitted;
        }
        Playback = new VfxPlaybackViewModel(_scenePreview?.Preview);
        PreviewAllCommand = new RelayCommand(() => SetPreviewSelection(layer => true), () => HasPreviewOptions);
        PreviewNoneCommand = new RelayCommand(() => SetPreviewSelection(layer => false), () => HasPreviewSelection);
        FrameSceneCommand = new RelayCommand(() => _scenePreview?.FrameContent(), () => _scenePreview?.Preview.PreviewBounds != null);
        SelectEmitterCommand = new RelayCommand(() => _scenePreview?.SelectEffect(), () => _scenePreview?.Preview.IsLoaded == true);
        MoveEmitterCommand = new RelayCommand(() => _scenePreview?.SetGizmoMode(GizmoMode.Translate), () => _scenePreview?.Preview.IsLoaded == true);
        RotateEmitterCommand = new RelayCommand(() => _scenePreview?.SetGizmoMode(GizmoMode.Rotate), () => _scenePreview?.Preview.IsLoaded == true);
        ScaleEmitterCommand = new RelayCommand(() => _scenePreview?.SetGizmoMode(GizmoMode.UniformScale), () => _scenePreview?.Preview.IsLoaded == true);
        ResetEmitterCommand = new RelayCommand(() => _scenePreview?.ResetTransform(), () => _scenePreview?.Preview.IsLoaded == true);
        Localization = localization;
        _displayName = localization.Get("Vfx.Title");
        Categories = new[] { "Common", "Appearance", "Size", "Emission", "Lifetime", "Motion", "Resources", "Advanced", "All" }
            .Select(x => new VfxChoice(x, localization.Get("Vfx.Category." + x))).ToArray();
        PreviewQualities = new[] { "0", "1", "2" }
            .Select(x => new VfxChoice(x, localization.GetFormat("Vfx.Quality", x))).ToArray();
        OpenCommand = new RelayCommand(Open);
        NewCompositionCommand = new RelayCommand(() =>
        {
            if (IsEmpty) NewComposition();
            else _editors.Create(EditorEnums.Vfx_Editor, editor => ((VfxEditorViewModel)editor).NewComposition());
        });
        AddReferenceCommand = new RelayCommand(AddReference, () => HasDocument && !HasErrors);
        DuplicateLayerCommand = new RelayCommand(() => ApplyStructure(_document!.DuplicateLayer(SelectedLayer!.Model)), () => CanChangeLayer);
        RemoveLayerCommand = new RelayCommand(() => ApplyStructure(_document!.RemoveLayer(SelectedLayer!.Model)), () => CanChangeLayer);
        SaveAsCommand = new RelayCommand(() => Save(true), () => HasDocument && !HasErrors);
        SaveCommand = new RelayCommand(() => Save(), () => HasDocument && !HasErrors);
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0 || HasErrors || _scenePreview?.HasActiveInteraction == true);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0 && !HasErrors && _scenePreview?.HasActiveInteraction != true);
        OpenReferenceCommand = new RelayCommand(OpenReference, () => !string.IsNullOrEmpty(SelectedLayer?.Model.Reference));
        try { _scenePreview?.Start(); }
        catch (Exception exception)
        {
            global::Shared.Core.ErrorHandling.Logging.Create<VfxEditorViewModel>().Error(exception, "VFX scene initialization failed");
            _scenePreview?.Stop();
            ScenePreviewStatus = Localization.Get("Vfx.Preview.RuntimeUnavailable");
        }
    }

    public void NewComposition()
    {
        // A new composition always opens in its own tab when another document is loaded.
        if (HasDocument) throw new InvalidOperationException();
        _document = VfxDocument.CreateComposition();
        DisplayName = Localization.Get("Vfx.Composition.Untitled");
        _revision = _nextRevision = 1;
        RebuildLayers();
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(IsEmpty));
        Status = Localization.Get("Vfx.Composition.StartHint");
        Changed();
    }

    private void AddReference()
    {
        var selected = _dialogs.DisplayBrowseDialog([".xml"], CreateVfxFilter("Vfx.Composition.Add"));
        if (!selected.Result) return;
        try
        {
            var source = VfxDocument.Read(selected.File.DataSource.ReadData());
            var path = ReferencePath(_files.GetFullPath(selected.File));
            CheckReferenceCycle(source, path, FilePath);
            ApplyStructure(_document!.AddReference(path[4..^4], source));
            Search = "";
            Category = "Common";
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException or IOException)
        {
            _dialogs.ShowDialogBox(Localization.GetFormat("Vfx.Composition.AddFailed", exception.Message),
                Localization.Get("Vfx.Title"), UiMessageBoxIcon.Warning);
        }
    }

    private void ApplyStructure(VfxStructureEdit edit)
    {
        Apply(edit.Apply, edit.Restore);
        RebuildLayers(true);
        SelectedLayer = Layers.FirstOrDefault(layer => layer.Model.Element == edit.Selection) ?? SelectedLayer;
        Changed();
    }

    private static string ReferencePath(string reference)
    {
        var path = reference.Replace('/', '\\');
        if (!path.StartsWith("vfx\\", StringComparison.OrdinalIgnoreCase)) path = "vfx\\" + path;
        if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) path += ".xml";
        return path;
    }

    private void CheckReferenceCycle(VfxDocument document, string path, string target)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destination = string.IsNullOrEmpty(target) ? "" : ReferencePath(target);
        Visit(document, path, 0);

        void Visit(VfxDocument current, string currentPath, int depth)
        {
            if (currentPath.Equals(destination, StringComparison.OrdinalIgnoreCase) || !active.Add(currentPath) || depth > 64)
                throw new InvalidDataException(Localization.Get("Vfx.Composition.Cycle"));
            foreach (var reference in current.Layers.Where(layer => layer.Kind == "Reference"))
            {
                var childPath = ReferencePath(reference.Reference);
                if (childPath.Equals(destination, StringComparison.OrdinalIgnoreCase) || active.Contains(childPath))
                    throw new InvalidDataException(Localization.Get("Vfx.Composition.Cycle"));
                if (visited.Contains(childPath)) continue;
                var file = _files.FindFile(childPath);
                if (file != null) Visit(VfxDocument.Read(file.DataSource.ReadData()), childPath, depth + 1);
            }
            active.Remove(currentPath);
            visited.Add(currentPath);
        }
    }

    public void LoadFile(PackFile file)
    {
        try
        {
            var bytes = file.DataSource.ReadData();
            var document = VfxDocument.Read(bytes);
            _scenePreview?.CancelActiveInteraction();
            CurrentFile = file;
            _loadedBytes = bytes;
            _document = document;
            DisplayName = file.Name;
            _undo.Clear();
            _redo.Clear();
            _revision = _savedRevision = _nextRevision = 0;
            RebuildLayers();
            Status = Localization.Get("Vfx.Status.Ready");
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(HasDocument));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Summary));
            Changed();
        }
        catch (System.Xml.XmlException exception)
        {
            Status = Localization.GetFormat("Vfx.InvalidXml", file.Name, exception.LineNumber, exception.LinePosition, exception.Message);
            _dialogs.ShowDialogBox(Status, Localization.Get("Vfx.Title"), UiMessageBoxIcon.Warning);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or System.Text.DecoderFallbackException)
        {
            Status = Localization.Get("Vfx.UnsupportedDocument");
            _dialogs.ShowDialogBox(Status, Localization.Get("Vfx.Title"), UiMessageBoxIcon.Warning);
        }
    }

    private void Open()
    {
        var selected = _dialogs.DisplayBrowseDialog([".xml"], CreateVfxFilter("Vfx.Browse"));
        if (!selected.Result) return;
        // Opening a different file uses a new editor, preserving this tab's pending edits.
        _editors.CreateFromFile(selected.File, EditorEnums.Vfx_Editor);
    }

    private BrowseDialogFilter CreateVfxFilter(string label)
    {
        // Resolve paths once instead of searching every container for each candidate.
        var files = _files.GetAllPackfileContainers().SelectMany(_files.GetFileEntriesSnapshot)
            .Where(entry => entry.Key.Replace('/', '\\').StartsWith("vfx\\", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Value).ToHashSet();
        return new BrowseDialogFilter(Localization.Get(label), Localization.Get("Vfx.BrowseHint"), files.Contains);
    }

    private void OpenReference()
    {
        var reference = SelectedLayer?.Model.Reference;
        if (string.IsNullOrWhiteSpace(reference)) return;
        var path = ReferencePath(reference);
        var file = _files.FindFile(path);
        if (file == null)
        {
            _dialogs.ShowDialogBox(Localization.GetFormat("Vfx.MissingReference", path), Localization.Get("Vfx.Title"));
            return;
        }
        _editors.CreateFromFile(file, EditorEnums.Vfx_Editor);
    }

    public bool Save() => Save(false);

    private bool Save(bool saveAs)
    {
        if (_document == null || HasErrors)
        {
            Status = Localization.Get("Vfx.Status.Invalid");
            return false;
        }
        try
        {
            if (_files.GetEditablePack() == null)
            {
                _dialogs.ShowDialogBox(Localization.Get("Vfx.NoProject"), Localization.Get("Vfx.Title"));
                return false;
            }
            var owner = CurrentFile == null ? null : _files.GetPackFileContainer(CurrentFile);
            if (CurrentFile != null && owner == null && !saveAs)
                throw new InvalidOperationException(Localization.Get("Vfx.FileDetached"));
            if (CurrentFile != null && !saveAs && !CurrentFile.DataSource.ReadData().SequenceEqual(_loadedBytes))
            {
                _dialogs.ShowDialogBox(Localization.Get("Vfx.FileChanged"), Localization.Get("Vfx.Title"), UiMessageBoxIcon.Warning);
                return false;
            }
            var bytes = _document.Write();
            var path = owner == null ? "" : FilePath;
            if (saveAs || CurrentFile == null || !ReferenceEquals(owner, _files.GetEditablePack()))
            {
                var result = _dialogs.DisplaySaveDialog(_files, [".xml"]);
                if (!result.Result || string.IsNullOrWhiteSpace(result.SelectedFilePath)) return false;
                path = result.SelectedFilePath.Replace('/', '\\');
                if (!path.StartsWith("vfx\\", StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    _dialogs.ShowDialogBox(Localization.Get("Vfx.Composition.SavePath"), Localization.Get("Vfx.Title"));
                    return false;
                }
            }
            if (saveAs && CurrentFile != null && ReferenceEquals(owner, _files.GetEditablePack())
                && path.Equals(FilePath, StringComparison.OrdinalIgnoreCase)
                && !CurrentFile.DataSource.ReadData().SequenceEqual(_loadedBytes))
            {
                _dialogs.ShowDialogBox(Localization.Get("Vfx.FileChanged"), Localization.Get("Vfx.Title"), UiMessageBoxIcon.Warning);
                return false;
            }
            CheckReferenceCycle(_document, "", path);
            var saved = _saveService.Save(path, bytes, false);
            if (saved == null) return false;
            // Folder projects may materialize a different PackFile instance on disk.
            CurrentFile = _files.FindFile(path, _files.GetEditablePack()) ?? saved;
            _loadedBytes = bytes;
            _savedRevision = _revision;
            DisplayName = saved.Name;
            Status = Localization.Get("Vfx.Status.Saved");
            OnPropertyChanged(nameof(FilePath));
            Changed();
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowExceptionWindow(exception, Localization.Get("Vfx.SaveFailed"));
            return false;
        }
    }

    internal string? BrowseResource(List<string> extensions)
    {
        var result = _dialogs.DisplayBrowseDialog(extensions);
        return result.Result ? _files.GetFullPath(result.File) : null;
    }

    private void SelectWeather()
    {
        var selected = _dialogs.DisplayBrowseDialog([".environment"]);
        if (!selected.Result) return;
        try { _weather!.Set(GameEnvironmentLighting.Read(selected.File.DataSource.ReadData(), _files.GetFullPath(selected.File))); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _weatherError = Localization.GetFormat("Vfx.Preview.WeatherInvalid", exception.Message);
            OnPropertyChanged(nameof(WeatherStatus));
        }
    }

    private void WeatherChanged()
    {
        _weatherError = "";
        OnPropertyChanged(nameof(WeatherStatus));
    }

    internal void Apply(Action apply, Action restore)
    {
        apply();
        var after = ++_nextRevision;
        _undo.Push(new Edit(apply, restore, _revision, after));
        _redo.Clear();
        _revision = after;
        Status = Localization.Get("Vfx.Status.Modified");
        Changed();
    }

    internal void Changed()
    {
        HasUnsavedChanges = _revision != _savedRevision || HasErrors;
        OnPropertyChanged(nameof(HasErrors));
        SaveCommand.NotifyCanExecuteChanged();
        HistoryChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        AddReferenceCommand.NotifyCanExecuteChanged();
        DuplicateLayerCommand.NotifyCanExecuteChanged();
        RemoveLayerCommand.NotifyCanExecuteChanged();
        ScheduleScenePreview();
    }

    private void Undo()
    {
        if (_scenePreview?.CancelActiveInteraction() == true) { HistoryChanged(); return; }
        if (!HasErrors && _undo.TryPop(out var edit))
        {
            edit.Restore();
            _revision = edit.Before;
            _redo.Push(edit);
            if (!edit.ChangesDocument) { HistoryChanged(); return; }
        }
        RebuildLayers(true);
        Changed();
        Status = Localization.Get(HasUnsavedChanges ? "Vfx.Status.Modified" : "Vfx.Status.Ready");
    }

    private void Redo()
    {
        if (_scenePreview?.HasActiveInteraction == true) return;
        if (!_redo.TryPop(out var edit)) return;
        edit.Apply();
        _revision = edit.After;
        _undo.Push(edit);
        if (!edit.ChangesDocument) { HistoryChanged(); return; }
        RebuildLayers(true);
        Changed();
        Status = Localization.Get(HasUnsavedChanges ? "Vfx.Status.Modified" : "Vfx.Status.Ready");
    }

    private void PreviewTransformCommitted(Matrix before, Matrix after)
    {
        if (_document == null || _scenePreview == null || before == after) return;
        _undo.Push(new Edit(() => _scenePreview.SetTransform(after), () => _scenePreview.SetTransform(before),
            _revision, _revision, ChangesDocument: false));
        _redo.Clear();
        HistoryChanged();
    }

    private void HistoryChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void RebuildLayers(bool retainSelection = false)
    {
        var included = Layers.Where(layer => layer.IsIncludedInPreview).Select(layer => (layer.Model.Kind, layer.Model.Name)).ToHashSet();
        var previous = Layers.Where(layer => layer.CanPreview).Select(layer => (layer.Model.Kind, layer.Model.Name)).ToHashSet();
        var layerIndex = retainSelection && SelectedLayer != null ? Layers.IndexOf(SelectedLayer) : 0;
        var fieldIndex = retainSelection && SelectedField != null ? SelectedLayer!.Fields.IndexOf(SelectedField) : 0;
        var quality = _quality;
        Layers.Clear();
        var models = _document!.Layers;
        var complete = (string?)_document.Root.Element("vfx")?.Attribute("construction_type") == "2"
            || models.Count(layer => layer.Kind is "Emitter" or "Reference") <= 1;
        var extendCompleteSelection = previous.Count == 0 ? complete : previous.SetEquals(included);
        foreach (var layer in models)
            Layers.Add(new VfxLayerViewModel(layer, this, retainSelection
                ? included.Contains((layer.Kind, layer.Name)) || extendCompleteSelection && !previous.Contains((layer.Kind, layer.Name))
                : complete));
        SelectedLayer = Layers.ElementAtOrDefault(Math.Clamp(layerIndex, 0, Layers.Count - 1));
        if (retainSelection && Qualities.Any(x => x.Key == quality)) Quality = quality;
        var field = SelectedLayer?.Fields.ElementAtOrDefault(Math.Max(0, fieldIndex));
        if (field != null && VisibleFields.Contains(field)) SelectedField = field;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasReferences));
        PreviewSelectionChanged();
    }

    internal void PreviewOnly(VfxLayerViewModel selected) => SetPreviewSelection(layer => ReferenceEquals(layer, selected));

    private void SetPreviewSelection(Func<VfxLayerViewModel, bool> include)
    {
        _updatingPreviewSelection = true;
        try { foreach (var layer in Layers.Where(layer => layer.CanPreview)) layer.IsIncludedInPreview = include(layer); }
        finally { _updatingPreviewSelection = false; }
        PreviewSelectionChanged();
    }

    internal void PreviewSelectionChanged()
    {
        if (_updatingPreviewSelection) return;
        OnPropertyChanged(nameof(HasPreviewOptions));
        OnPropertyChanged(nameof(HasPreviewSelection));
        OnPropertyChanged(nameof(PreviewSelectionSummary));
        PreviewAllCommand.NotifyCanExecuteChanged();
        PreviewNoneCommand.NotifyCanExecuteChanged();
        ScheduleScenePreview();
    }

    private void FilterFields()
    {
        var selected = SelectedField;
        VisibleFields.Clear();
        foreach (var field in SelectedLayer?.Fields ?? [])
        {
            if (_quality != "All" && field.Model.Quality.Length > 0 && field.Model.Quality != _quality) continue;
            if (string.IsNullOrWhiteSpace(_search))
            {
                if (_category == "Common" && !field.IsCommon) continue;
                if (_category is not ("All" or "Common") && field.Category != _category) continue;
            }
            else if (!field.SearchText.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            VisibleFields.Add(field);
        }
        SelectedField = selected != null && VisibleFields.Contains(selected) ? selected : VisibleFields.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoFields));
        OnPropertyChanged(nameof(FieldCount));
        OnPropertyChanged(nameof(CategoryHint));
    }

    private void ScheduleScenePreview()
    {
        if (_scenePreview == null && _livePreviews == null) return;
        _scenePreviewTimer.Stop();
        _scenePreviewTimer.Start();
    }

    private void OnSharedPreviewChanged(object file)
    {
        if (_scenePreview == null || ReferenceEquals(file, CurrentFile)) return;
        lock (_scenePreviewDependencyGate)
            if (!_scenePreviewDependencies.Contains(file)) return;
        ScheduleScenePreview();
    }

    private void RefreshScenePreview(object? sender, EventArgs args)
    {
        _scenePreviewTimer.Stop();
        if (_document == null) return;
        if (HasErrors)
        {
            if (CurrentFile != null) _livePreviews?.Publish(this, CurrentFile, null);
            if (_scenePreview != null) _playbackBeforeValidationError ??= Playback.PlayingIntent;
            _scenePreview?.Stop(); ScenePreviewStatus = Localization.Get("Vfx.Status.Invalid");
            return;
        }
        var containers = new[] { CurrentFile == null ? null : _files.GetPackFileContainer(CurrentFile), _files.GetEditablePack() }
            .Concat(_files.GetAllPackfileContainers().AsEnumerable().Reverse()).Where(x => x != null).Cast<PackFileContainer>().Distinct().ToArray();
        lock (_scenePreviewDependencyGate) _scenePreviewDependencies.Clear();
        var snapshot = new VfxPreviewSnapshot(_document.Write(), FilePath, path =>
        {
            foreach (var container in containers)
            {
                var file = _files.FindFile(path, container);
                if (file == null) continue;
                lock (_scenePreviewDependencyGate) _scenePreviewDependencies.Add(file);
                return _livePreviews?.ReadResource(file) ?? file.DataSource.ReadData();
            }
            return null;
        }, PreviewQuality);
        if (CurrentFile != null) _livePreviews?.Publish(this, CurrentFile, snapshot);
        else _livePreviews?.Remove(this);
        if (_scenePreview == null) { ScenePreviewStatus = Localization.Get("Vfx.Preview.RuntimeUnavailable"); return; }
        if (_playbackBeforeValidationError is { } resume)
        {
            Playback.RestorePlayingIntent(resume);
            _playbackBeforeValidationError = null;
        }
        var selectable = Layers.Where(layer => layer.CanPreview).ToArray();
        var selected = selectable.Where(layer => layer.IsIncludedInPreview).ToArray();
        var selection = selected.Length == selectable.Length ? null : new VfxPreviewRootSelection(
            selected.Where(layer => layer.Model.Kind == "Emitter").Select(layer => layer.Model.Name).ToArray(),
            selected.Where(layer => layer.Model.Kind == "Reference").Select(layer => layer.Model.Name).ToArray());
        _scenePreview.Preview.Reload(snapshot with { RootSelection = selection });
    }

    private void ScenePreviewReloaded()
    {
        if (_scenePreview == null) return;
        FrameSceneCommand.NotifyCanExecuteChanged();
        SelectEmitterCommand.NotifyCanExecuteChanged();
        MoveEmitterCommand.NotifyCanExecuteChanged();
        RotateEmitterCommand.NotifyCanExecuteChanged();
        ScaleEmitterCommand.NotifyCanExecuteChanged();
        ResetEmitterCommand.NotifyCanExecuteChanged();
        ScenePreviewDiagnostics = _scenePreview.Preview.Diagnostics.Select(x =>
            Localization.GetFormat("Vfx.Preview.Diagnostic", x.Layer, x.Field, Localization.Get(x.ReasonKey))).ToArray();
        ScenePreviewStatus = !HasPreviewSelection && HasPreviewOptions ? Localization.Get("Vfx.Preview.SelectScope")
            : _scenePreview.Preview.EmitterCount == 0 ? Localization.Get("Vfx.Preview.None")
            : Localization.GetFormat("Vfx.Preview.Summary", _scenePreview.Preview.EmitterCount, _scenePreview.Preview.TotalEmitterCount, _scenePreview.Preview.DisabledEmitterCount);
    }

    public void Close()
    {
        Playback.Dispose();
        _scenePreviewTimer.Stop();
        _scenePreviewTimer.Tick -= RefreshScenePreview;
        if (_livePreviews != null) _livePreviews.Changed -= OnSharedPreviewChanged;
        if (_weather != null) _weather.Changed -= WeatherChanged;
        lock (_scenePreviewDependencyGate) _scenePreviewDependencies.Clear();
        _livePreviews?.Remove(this);
        if (_scenePreview != null)
        {
            _scenePreview.Preview.Reloaded -= ScenePreviewReloaded;
            _scenePreview.TransformCommitted -= PreviewTransformCommitted;
            _scenePreview.Stop();
        }
    }
    private sealed record Edit(Action Apply, Action Restore, int Before, int After, bool ChangesDocument = true);
}

public sealed record VfxChoice(string Key, string Label);

public sealed class VfxLayerViewModel : ObservableObject
{
    private readonly VfxEditorViewModel _editor;
    private bool _isIncludedInPreview;
    public bool CanPreview => Model.Kind is "Emitter" or "Reference";
    public bool IsIncludedInPreview
    {
        get => _isIncludedInPreview;
        set { if (CanPreview && SetProperty(ref _isIncludedInPreview, value)) _editor.PreviewSelectionChanged(); }
    }
    public RelayCommand PreviewOnlyCommand { get; }
    public VfxLayer Model { get; }
    public string Label { get; }
    public string Subtitle { get; }
    public List<VfxFieldViewModel> Fields { get; }

    public VfxLayerViewModel(VfxLayer model, VfxEditorViewModel editor, bool includedInPreview = false)
    {
        _editor = editor;
        Model = model;
        _isIncludedInPreview = CanPreview && includedInPreview;
        PreviewOnlyCommand = new RelayCommand(() => editor.PreviewOnly(this), () => CanPreview);
        Label = model.Kind == "Settings" ? editor.Localization.Get("Vfx.Layer.Settings")
            : model.Kind == "Reference" && model.Name.StartsWith("vfxref_", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(model.Reference.Replace('/', '\\')) : model.Name;
        Subtitle = editor.Localization.Get("Vfx.Layer." + model.Kind)
            + (model.Kind == "Emitter" && model.Type.Length > 0 ? " · " + editor.Localization.GetOrDefault("Vfx.EmitterType." + model.Type, model.Type) : "");
        Fields = model.Fields.Select(x => new VfxFieldViewModel(x, editor)).OrderBy(x => VfxFieldGuidance.CommonOrder(x.Model.Name)).ToList();
    }
}
