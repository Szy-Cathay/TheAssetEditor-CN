using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly ITerryPreviewService _terry;
    private string _previewStatus = "";
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
    private VfxLayerViewModel? _selectedLayer;
    private VfxFieldViewModel? _selectedField;
    private bool _hasUnsavedChanges;

    public LocalizationManager Localization { get; }
    public ObservableCollection<VfxLayerViewModel> Layers { get; } = [];
    public ObservableCollection<VfxFieldViewModel> VisibleFields { get; } = [];
    public IReadOnlyList<VfxChoice> Categories { get; }
    public ObservableCollection<VfxChoice> Qualities { get; } = [];
    public PackFile CurrentFile { get; private set; } = null!;
    public string FilePath => CurrentFile == null ? "" : _files.GetFullPath(CurrentFile);
    public bool HasDocument => _document != null;
    public bool IsEmpty => !HasDocument;
    public bool HasSelection => SelectedField != null;
    public bool HasNoFields => HasDocument && VisibleFields.Count == 0;
    public bool HasReferences => Layers.Any(x => x.Model.Kind == "Reference");
    public bool CanChangeLayer => !HasErrors && SelectedLayer?.Model.Kind is "Emitter" or "Reference";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PreviewStatus { get => _previewStatus; private set { if (SetProperty(ref _previewStatus, value)) OnPropertyChanged(nameof(HasPreviewStatus)); } }
    public bool HasPreviewStatus => PreviewStatus.Length > 0;
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public bool HasUnsavedChanges { get => _hasUnsavedChanges; set => SetProperty(ref _hasUnsavedChanges, value); }
    public bool HasErrors => Layers.Any(x => x.Fields.Any(f => f.HasErrors));
    public string Summary => Localization.GetFormat("Vfx.Summary", Layers.Count(x => x.Model.Kind == "Emitter"), Layers.Count(x => x.Model.Kind == "Reference"));
    public string FieldCount => Localization.GetFormat("Vfx.FieldCount", VisibleFields.Count, SelectedLayer?.Fields.Count ?? 0);
    public string CategoryHint => Localization.Get(string.IsNullOrWhiteSpace(Search) ? "Vfx.GoalHint." + Category : "Vfx.SearchAllHint");
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) FilterFields(); } }
    public string Category { get => _category; set { if (SetProperty(ref _category, value)) FilterFields(); } }
    public string Quality { get => _quality; set { if (SetProperty(ref _quality, value)) FilterFields(); } }
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
    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand EndPreviewCommand { get; }

    public VfxEditorViewModel(IPackFileService files, IFileSaveService saveService,
        IStandardDialogs dialogs, IEditorCreator editors, LocalizationManager localization, ITerryPreviewService terry)
    {
        _files = files;
        _saveService = saveService;
        _dialogs = dialogs;
        _editors = editors;
        _terry = terry;
        Localization = localization;
        _displayName = localization.Get("Vfx.Title");
        Categories = new[] { "Common", "Appearance", "Size", "Emission", "Lifetime", "Motion", "Resources", "Advanced", "All" }
            .Select(x => new VfxChoice(x, localization.Get("Vfx.Category." + x))).ToArray();
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
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0 || HasErrors);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0 && !HasErrors);
        OpenReferenceCommand = new RelayCommand(OpenReference, () => !string.IsNullOrEmpty(SelectedLayer?.Model.Reference));
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => HasDocument && !HasErrors && !EndPreviewCommand!.IsRunning);
        EndPreviewCommand = new AsyncRelayCommand(async () =>
        {
            try
            {
                await _terry.DisableAsync();
                PreviewStatus = Localization.Get("Vfx.Terry.Stopped");
            }
            catch (Exception exception) { PreviewStatus = Localization.GetFormat("Vfx.Terry.Failed", exception.Message); }
        }, () => !PreviewCommand.IsRunning);
        PreviewCommand.PropertyChanged += (_, _) => EndPreviewCommand.NotifyCanExecuteChanged();
        EndPreviewCommand.PropertyChanged += (_, _) => PreviewCommand.NotifyCanExecuteChanged();
    }

    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        PreviewStatus = Localization.Get("Vfx.Terry.Preparing");
        try
        {
            var result = await _terry.PreviewAsync(_document!.Write(), FilePath,
                CurrentFile == null ? null : _files.GetPackFileContainer(CurrentFile), cancellationToken);
            PreviewStatus = Localization.GetFormat("Vfx.Terry.Ready", result.SourceName, result.EffectCount, result.FileCount);
        }
        catch (OperationCanceledException) { PreviewStatus = Localization.Get("Vfx.Terry.Cancelled"); }
        catch (Exception exception)
        {
            Shared.Core.ErrorHandling.Logging.Create<VfxEditorViewModel>().Error(exception, "Terry preview failed");
            PreviewStatus = Localization.GetFormat("Vfx.Terry.Failed", exception.Message);
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
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        AddReferenceCommand.NotifyCanExecuteChanged();
        DuplicateLayerCommand.NotifyCanExecuteChanged();
        RemoveLayerCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
    }

    private void Undo()
    {
        if (!HasErrors && _undo.TryPop(out var edit))
        {
            edit.Restore();
            _revision = edit.Before;
            _redo.Push(edit);
        }
        RebuildLayers(true);
        Changed();
        Status = Localization.Get(HasUnsavedChanges ? "Vfx.Status.Modified" : "Vfx.Status.Ready");
    }

    private void Redo()
    {
        if (!_redo.TryPop(out var edit)) return;
        edit.Apply();
        _revision = edit.After;
        _undo.Push(edit);
        RebuildLayers(true);
        Changed();
        Status = Localization.Get(HasUnsavedChanges ? "Vfx.Status.Modified" : "Vfx.Status.Ready");
    }

    private void RebuildLayers(bool retainSelection = false)
    {
        var layerIndex = retainSelection && SelectedLayer != null ? Layers.IndexOf(SelectedLayer) : 0;
        var fieldIndex = retainSelection && SelectedField != null ? SelectedLayer!.Fields.IndexOf(SelectedField) : 0;
        var quality = _quality;
        Layers.Clear();
        foreach (var layer in _document!.Layers)
            Layers.Add(new VfxLayerViewModel(layer, this));
        SelectedLayer = Layers.ElementAtOrDefault(Math.Clamp(layerIndex, 0, Layers.Count - 1));
        if (retainSelection && Qualities.Any(x => x.Key == quality)) Quality = quality;
        var field = SelectedLayer?.Fields.ElementAtOrDefault(Math.Max(0, fieldIndex));
        if (field != null && VisibleFields.Contains(field)) SelectedField = field;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasReferences));
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

    public void Close() { }
    private sealed record Edit(Action Apply, Action Restore, int Before, int After);
}

public sealed record VfxChoice(string Key, string Label);

public sealed class VfxLayerViewModel
{
    public VfxLayer Model { get; }
    public string Label { get; }
    public string Subtitle { get; }
    public List<VfxFieldViewModel> Fields { get; }

    public VfxLayerViewModel(VfxLayer model, VfxEditorViewModel editor)
    {
        Model = model;
        Label = model.Kind == "Settings" ? editor.Localization.Get("Vfx.Layer.Settings")
            : model.Kind == "Reference" && model.Name.StartsWith("vfxref_", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(model.Reference.Replace('/', '\\')) : model.Name;
        Subtitle = editor.Localization.Get("Vfx.Layer." + model.Kind)
            + (model.Kind == "Emitter" && model.Type.Length > 0 ? " · " + editor.Localization.GetOrDefault("Vfx.EmitterType." + model.Type, model.Type) : "");
        Fields = model.Fields.Select(x => new VfxFieldViewModel(x, editor)).OrderBy(x => VfxFieldGuidance.CommonOrder(x.Model.Name)).ToList();
    }
}
