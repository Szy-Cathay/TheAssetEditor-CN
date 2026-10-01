using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml.Serialization;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack;
using Shared.Ui.Editors.TextEditor;

namespace Editors.AnimationFragmentEditor.CampaignAnimBin
{
    public partial class CampaignTableEditorViewModel : NotifyPropertyChangedImpl
    {
        private readonly IPackFileService _pfs;
        private readonly IStandardDialogs _dialogs;
        private CampaignAnimationBin _data = new();
        private readonly Stack<string> _undo = new();
        private readonly Stack<string> _redo = new();
        private string _savedXml = string.Empty;
        private string _fileName = string.Empty;
        private bool _isDirty;
        private bool _restoring;
        private string _rowFilter = string.Empty;
        private CampaignAnimationBin.StatusItem? _selectedState;
        private CampaignCategory? _selectedCategory;
        private CampaignEntryRowViewModel? _selectedRow;
        private readonly Dictionary<object, CampaignEntryRowViewModel> _rowCache = new();
        public ObservableCollection<CampaignAnimationBin.StatusItem> States { get; } = new();
        public ObservableCollection<CampaignCategory> Categories { get; } = new();
        public ObservableCollection<CampaignEntryRowViewModel> Rows { get; } = new();
        public ICollectionView FilteredRows => CollectionViewSource.GetDefaultView(Rows);
        public List<string> SkeletonNames { get; set; } = new();
        public List<string> AnimationFiles { get; set; } = new();
        public List<string> MetaFiles { get; set; } = new();
        public List<string> SoundFiles { get; set; } = new();
        public ICommand? SaveCommand { get; set; }
        public bool IsStandaloneFile { get; init; }
        public string SaveButtonText => Text(IsStandaloneFile ? "AnimPack.Campaign.SaveFile" : "AnimPack.ApplyToPack");
        public Action<string>? OpenResource { get; set; }
        public Action<string, string, string, string>? Preview { get; set; }
        public string Reference => _data.Reference;
        public string? PersistentMetadataPath => _data.Status.FirstOrDefault(s => s.Name == "global")?
            .PersitantMetaData?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.AnimationMeta))?.AnimationMeta;
        public int Version => _data.Version;
        public string SkeletonName
        {
            get => _data.SkeletonName;
            set { if (_data.SkeletonName == value) return; BeforeEdit(); _data.SkeletonName = value; NotifyPropertyChanged(); Changed(); }
        }
        public bool IsDirty { get => _isDirty; private set => SetAndNotifyWhenChanged(ref _isDirty, value); }
        public string RowFilter { get => _rowFilter; set => SetAndNotifyWhenChanged(ref _rowFilter, value, _ => RefreshFilter()); }
        public string ReplaceFrom { get; set; } = string.Empty;
        public string ReplaceTo { get; set; } = string.Empty;
        [RelayCommand]
        private void ReplacePaths()
        {
            if (string.IsNullOrEmpty(ReplaceFrom) || HasInvalidFields) return;
            BeforeEdit();
            foreach (var state in _data.Status)
            foreach (var collection in state.GetType().GetProperties())
            {
                if (collection.GetValue(state) is not IList list) continue;
                foreach (var entry in list)
                foreach (var property in entry.GetType().GetProperties().Where(p => p.PropertyType == typeof(string)))
                {
                    if (property.Name is "Animation" or "AnimationMeta" or "MetaFile" or "Meta" or "SoundMeta"
                        || entry is CampaignAnimationBin.PortholeEntry && property.Name is "Value0" or "Value2" or "Value3")
                        property.SetValue(entry, ((string?)property.GetValue(entry) ?? string.Empty).Replace(ReplaceFrom, ReplaceTo, StringComparison.OrdinalIgnoreCase));
                }
            }
            _rowCache.Clear(); LoadRows(); Changed();
        }
        public bool HasInvalidFields => _rowCache.Values.Any(row => row.Fields.Any(parameter => parameter.HasError));
        public string ValidationSummary { get; private set; } = string.Empty;
        public CampaignAnimationBin.StatusItem? SelectedState
        {
            get => _selectedState;
            set
            {
                if (ReferenceEquals(_selectedState, value)) return;
                SetAndNotifyWhenChanged(ref _selectedState, value);
                Categories.Clear();
                if (value != null)
                {
                    var names = value.Name == "global" ? new[] { "PersitantMetaData", "Poses", "Docks" }
                        : new[] { "Idle", "Porthole", "Selection", "Transitions", "Action", "Unk1", "Unknown", "Unk3", "Locomotion" };
                    foreach (var name in names.Where(n => Version >= 3 || n != "Transitions"))
                        Categories.Add(new(name));
                }
                SelectedCategory = Categories.FirstOrDefault();
                DeleteStateCommand.NotifyCanExecuteChanged();
                RenameStateCommand.NotifyCanExecuteChanged();
            }
        }
        public CampaignCategory? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (ReferenceEquals(_selectedCategory, value)) return;
                SetAndNotifyWhenChanged(ref _selectedCategory, value);
                LoadRows(); AddEntryCommand.NotifyCanExecuteChanged();
            }
        }
        public CampaignEntryRowViewModel? SelectedRow
        {
            get => _selectedRow;
            set
            {
                SetAndNotifyWhenChanged(ref _selectedRow, value);
                DeleteEntryCommand.NotifyCanExecuteChanged(); DuplicateEntryCommand.NotifyCanExecuteChanged();
                PreviewAnimationCommand.NotifyCanExecuteChanged(); OpenAnimationCommand.NotifyCanExecuteChanged();
                OpenMetaCommand.NotifyCanExecuteChanged(); OpenSoundCommand.NotifyCanExecuteChanged();
            }
        }
        public CampaignTableEditorViewModel(IPackFileService pfs, IStandardDialogs dialogs) { _pfs = pfs; _dialogs = dialogs; }
        public void LoadFromBinary(byte[] bytes, string fileName)
        {
            _fileName = fileName;
            LoadXml(new CampaignAnimBinToXmlConverter().GetText(bytes));
            _undo.Clear(); _redo.Clear();
            MarkSaved(); NotifyHistory();
        }
        public string BuildXmlString()
        {
            using var writer = new System.IO.StringWriter();
            new XmlSerializer(typeof(CampaignAnimationBin)).Serialize(writer, _data);
            return writer.ToString();
        }
        public byte[]? SaveToBinary(string fileName, out ITextConverter.SaveError? error)
        {
            if (HasInvalidFields)
            {
                error = new() { Text = Text("AnimPack.Campaign.InvalidField") };
                return null;
            }
            UpdateValidation();
            return new CampaignAnimBinToXmlConverter().ToBytes(BuildXmlString(), fileName, _pfs, out error);
        }
        public void MarkSaved() { _savedXml = BuildXmlString(); IsDirty = false; }
        private void BeforeEdit()
        {
            if (_restoring) return;
            var snapshot = BuildXmlString();
            if (_undo.Count == 0 || _undo.Peek() != snapshot) _undo.Push(snapshot);
            while (_undo.Count > 50)
            {
                var keep = _undo.Take(50).Reverse().ToArray(); _undo.Clear(); foreach (var item in keep) _undo.Push(item);
            }
            _redo.Clear(); NotifyHistory();
        }
        private void Changed()
        {
            if (_restoring) return;
            IsDirty = BuildXmlString() != _savedXml || HasInvalidFields;
            NotifyPropertyChanged(nameof(HasInvalidFields));
            NotifyResourceActions();
            RefreshFilter();
        }
        private void LoadXml(string xml)
        {
            var stateName = SelectedState?.Name;
            var categoryName = SelectedCategory?.Name;
            _restoring = true;
            try
            {
                using var reader = new System.IO.StringReader(xml);
                _data = (CampaignAnimationBin)new XmlSerializer(typeof(CampaignAnimationBin)).Deserialize(reader)!;
                _rowCache.Clear();
                States.Clear(); foreach (var state in _data.Status) States.Add(state);
                SelectedState = States.FirstOrDefault(s => s.Name == stateName) ?? States.FirstOrDefault(s => s.Name == "status_normal") ?? States.FirstOrDefault();
                SelectedCategory = Categories.FirstOrDefault(c => c.Name == categoryName) ?? Categories.FirstOrDefault();
                NotifyPropertyChanged(nameof(Reference)); NotifyPropertyChanged(nameof(Version)); NotifyPropertyChanged(nameof(SkeletonName));
                UpdateValidation();
            }
            finally { _restoring = false; }
        }
        private IList? EntryList(bool create)
        {
            if (SelectedState == null || SelectedCategory == null) return null;
            var property = typeof(CampaignAnimationBin.StatusItem).GetProperty(SelectedCategory.Name)!;
            var value = property.GetValue(SelectedState) as IList;
            if (value == null && create) { value = (IList)Activator.CreateInstance(property.PropertyType)!; property.SetValue(SelectedState, value); }
            return value;
        }
        private void LoadRows()
        {
            Rows.Clear(); SelectedRow = null;
            if (EntryList(false) is { } entries)
                foreach (var entry in entries) Rows.Add(GetRow(entry));
            RefreshFilter();
        }
        private CampaignEntryRowViewModel GetRow(object entry)
        {
            if (!_rowCache.TryGetValue(entry, out var row))
                _rowCache.Add(entry, row = new(entry, BeforeEdit, Changed));
            foreach (var field in row.Fields.Where(f => f.IsStateReference))
                field.SetStateOptions(States.Select(s => s.Name));
            return row;
        }
        private void RefreshFilter()
        {
            if (FilteredRows is IEditableCollectionView editing && (editing.IsEditingItem || editing.IsAddingNew)) return;
            FilteredRows.Filter = item => item is CampaignEntryRowViewModel row &&
                (string.IsNullOrWhiteSpace(RowFilter) || $"{row.Animation} {row.Meta} {row.Sound} {row.Type}".Contains(RowFilter, StringComparison.OrdinalIgnoreCase));
        }
        private void UpdateValidation()
        {
            var report = Validator.Check(_data, _pfs, _fileName);
            ValidationSummary = LocalizationManager.Instance?.GetFormat("AnimPack.Campaign.ValidationSummary", report.Errors.Count(e => e.IsError), report.Errors.Count(e => e.IsWarning)) ?? string.Empty;
            NotifyPropertyChanged(nameof(ValidationSummary));
        }
        [RelayCommand] private void Validate() { UpdateValidation(); _dialogs.ShowErrorViewDialog(Text("AnimPack.Validation.Title"), Validator.Check(_data, _pfs, _fileName)); }
        private bool CanUndo() => _undo.Count > 0;
        [RelayCommand(CanExecute = nameof(CanUndo))] private void Undo() { _redo.Push(BuildXmlString()); LoadXml(_undo.Pop()); Changed(); NotifyHistory(); }
        private bool CanRedo() => _redo.Count > 0;
        [RelayCommand(CanExecute = nameof(CanRedo))] private void Redo() { _undo.Push(BuildXmlString()); LoadXml(_redo.Pop()); Changed(); NotifyHistory(); }
        private void NotifyHistory() { UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged(); }
        [RelayCommand] private void AddState()
        {
            var result = _dialogs.ShowTextInputDialog(Text("AnimPack.Campaign.AddState"), "status_");
            if (!result.Result || string.IsNullOrWhiteSpace(result.Text)) return;
            var name = result.Text.Trim();
            if (_data.Status.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { _dialogs.ShowDialogBox(Text("AnimPack.Campaign.DuplicateState"), Text("Msg.GeneralError")); return; }
            BeforeEdit(); var state = new CampaignAnimationBin.StatusItem { Name = name }; _data.Status.Add(state); States.Add(state); SelectedState = state; Changed();
        }
        private bool CanUseState() => SelectedState != null;
        [RelayCommand(CanExecute = nameof(CanUseState))] private void RenameState()
        {
            var state = SelectedState!;
            var result = _dialogs.ShowTextInputDialog(Text("AnimPack.Campaign.RenameState"), state.Name);
            if (!result.Result || string.IsNullOrWhiteSpace(result.Text) || result.Text.Trim() == state.Name) return;
            var name = result.Text.Trim();
            if (state.Name == "global" || name == "global" || _data.Status.Any(s => s != state && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { _dialogs.ShowDialogBox(Text("AnimPack.Campaign.RenameStateInvalid"), Text("Msg.GeneralError")); return; }
            BeforeEdit(); var oldName = state.Name; state.Name = name;
            foreach (var transition in _data.Status.SelectMany(s => s.Transitions ?? [])) if (transition.TransitionTo == oldName) transition.TransitionTo = name;
            _rowCache.Clear();
            var index = States.IndexOf(state); States.RemoveAt(index); States.Insert(index, state); SelectedState = state; LoadRows(); Changed();
        }
        [RelayCommand(CanExecute = nameof(CanUseState))] private void DeleteState()
        {
            if (_dialogs.ShowYesNoBox(Text("AnimPack.Campaign.DeleteStateConfirm"), Text("AnimPack.Campaign.DeleteState")) != ShowMessageBoxResult.OK) return;
            BeforeEdit(); var state = SelectedState!;
            foreach (var property in state.GetType().GetProperties())
                if (property.GetValue(state) is IList list) foreach (var entry in list) _rowCache.Remove(entry);
            _data.Status.Remove(state); States.Remove(state); SelectedState = States.FirstOrDefault(); Changed();
        }
        private bool CanAddEntry() => SelectedCategory != null;
        [RelayCommand(CanExecute = nameof(CanAddEntry))] private void AddEntry()
        {
            BeforeEdit(); var list = EntryList(true)!;
            var entry = Activator.CreateInstance(list.GetType().GetGenericArguments()[0])!;
            foreach (var property in entry.GetType().GetProperties().Where(p => p.PropertyType == typeof(string))) property.SetValue(entry, string.Empty);
            list.Add(entry); var row = GetRow(entry); Rows.Add(row); SelectedRow = row; Changed();
        }
        private bool CanUseRow() => SelectedRow != null;
        [RelayCommand(CanExecute = nameof(CanUseRow))] private void DeleteEntry() { BeforeEdit(); EntryList(false)!.Remove(SelectedRow!.Entry); _rowCache.Remove(SelectedRow.Entry); Rows.Remove(SelectedRow); SelectedRow = Rows.FirstOrDefault(); Changed(); }
        [RelayCommand(CanExecute = nameof(CanUseRow))] private void DuplicateEntry()
        {
            BeforeEdit(); var original = SelectedRow!.Entry; var copy = Activator.CreateInstance(original.GetType())!;
            foreach (var property in original.GetType().GetProperties()) property.SetValue(copy, property.GetValue(original));
            EntryList(true)!.Add(copy); var row = GetRow(copy); Rows.Add(row); SelectedRow = row; Changed();
        }
        private bool CanOpenAnimation() => !string.IsNullOrWhiteSpace(SelectedRow?.Animation);
        private bool CanPreviewAnimation() => CanOpenAnimation() && !string.IsNullOrWhiteSpace(SkeletonName);
        private bool CanOpenMeta() => !string.IsNullOrWhiteSpace(SelectedRow?.Meta);
        private bool CanOpenSound() => !string.IsNullOrWhiteSpace(SelectedRow?.Sound);
        private void NotifyResourceActions()
        {
            PreviewAnimationCommand.NotifyCanExecuteChanged(); OpenAnimationCommand.NotifyCanExecuteChanged();
            OpenMetaCommand.NotifyCanExecuteChanged(); OpenSoundCommand.NotifyCanExecuteChanged();
        }
        [RelayCommand(CanExecute = nameof(CanPreviewAnimation))] private void PreviewAnimation() => Preview?.Invoke(SelectedRow!.Animation, SkeletonName, SelectedRow.Meta, SelectedRow.Sound);
        [RelayCommand(CanExecute = nameof(CanOpenAnimation))] private void OpenAnimation() => OpenResource?.Invoke(SelectedRow!.Animation);
        [RelayCommand(CanExecute = nameof(CanOpenMeta))] private void OpenMeta() => OpenResource?.Invoke(SelectedRow!.Meta);
        [RelayCommand(CanExecute = nameof(CanOpenSound))] private void OpenSound() => OpenResource?.Invoke(SelectedRow!.Sound);
        private static string Text(string key) => LocalizationManager.Instance?.Get(key) ?? key;
    }

    public record CampaignCategory(string Name)
    {
        public string Label => LocalizationManager.Instance?.Get($"AnimPack.Campaign.Category.{Name}") ?? Name;
    }

    public class CampaignEntryRowViewModel : NotifyPropertyChangedImpl
    {
        private readonly Action _beforeEdit;
        private readonly Action _changed;
        private readonly Dictionary<string, PropertyInfo?> _mapping;
        public object Entry { get; }
        public List<CampaignFieldViewModel> Fields { get; }
        public CampaignEntryRowViewModel(object entry, Action beforeEdit, Action changed)
        {
            Entry = entry; _beforeEdit = beforeEdit; _changed = changed;
            var properties = entry.GetType().GetProperties();
            _mapping = new()
            {
                [nameof(Animation)] = properties.FirstOrDefault(p => p.Name is "Animation" or "Value0"),
                [nameof(Type)] = properties.FirstOrDefault(p => p.Name == "Type" || entry is CampaignAnimationBin.PortholeEntry && p.Name == "Value1"),
                [nameof(Meta)] = properties.FirstOrDefault(p => p.Name is "AnimationMeta" or "MetaFile" or "Meta" || entry is CampaignAnimationBin.PortholeEntry && p.Name == "Value2"),
                [nameof(Sound)] = properties.FirstOrDefault(p => p.Name == "SoundMeta" || entry is CampaignAnimationBin.PortholeEntry && p.Name == "Value3"),
            };
            Fields = properties.Where(p => !_mapping.Values.Contains(p) && p.Name != nameof(CampaignAnimationBin.ActionEntry.HasExtraString)
                && (p.Name != nameof(CampaignAnimationBin.ActionEntry.ExtraString) || entry is CampaignAnimationBin.ActionEntry { HasExtraString: true }))
                .Select(p => new CampaignFieldViewModel(entry, p, beforeEdit, changed)).ToList();
        }
        private string Get(string name) => _mapping[name]?.GetValue(Entry) as string ?? string.Empty;
        private void Set(string name, string value)
        {
            var property = _mapping[name]; if (property == null || Get(name) == value) return;
            _beforeEdit(); property.SetValue(Entry, value ?? string.Empty); NotifyPropertyChanged(name); _changed();
        }
        public string Animation { get => Get(nameof(Animation)); set => Set(nameof(Animation), value); }
        public string Type { get => Get(nameof(Type)); set => Set(nameof(Type), value); }
        public bool CanEditType => _mapping[nameof(Type)] != null;
        public string Meta { get => Get(nameof(Meta)); set => Set(nameof(Meta), value); }
        public string Sound { get => Get(nameof(Sound)); set => Set(nameof(Sound), value); }
    }

    public class CampaignFieldViewModel : NotifyPropertyChangedImpl
    {
        private readonly object _entry;
        private readonly PropertyInfo _property;
        private readonly Action _beforeEdit, _changed;
        private string _value;
        private bool _hasError;
        public string Label => LocalizationManager.Instance?.Get($"AnimPack.Campaign.Field.{_property.Name}") ?? _property.Name;
        public bool IsBoolean => _property.PropertyType == typeof(bool);
        public bool IsStateReference => _property.Name == nameof(CampaignAnimationBin.TransitionEntry.TransitionTo);
        public bool IsText => !IsBoolean && !IsStateReference;
        public IReadOnlyList<string> StateOptions { get; private set; } = [];
        public void SetStateOptions(IEnumerable<string> names)
        {
            StateOptions = names.Prepend(string.Empty).Append(Value).Distinct(StringComparer.Ordinal).ToArray();
            NotifyPropertyChanged(nameof(StateOptions));
        }
        public bool HasError { get => _hasError; private set => SetAndNotifyWhenChanged(ref _hasError, value); }
        public bool BooleanValue
        {
            get => IsBoolean && _property.GetValue(_entry) is true;
            set
            {
                if (!IsBoolean || BooleanValue == value) return;
                Value = value.ToString();
            }
        }
        public string Value
        {
            get => _value;
            set
            {
                if (value == null || _value == value) return;
                _beforeEdit();
                SetAndNotifyWhenChanged(ref _value, value);
                try
                {
                    object converted = _property.PropertyType == typeof(string) ? value
                        : _property.PropertyType == typeof(float) ? float.Parse(value, CultureInfo.InvariantCulture)
                        : Convert.ChangeType(value, _property.PropertyType, CultureInfo.InvariantCulture);
                    if (converted is float number && (!float.IsFinite(number) || number < 0 && _property.Name is "BlendTime" or "Weight" or "ModelScale"))
                        throw new FormatException();
                    _property.SetValue(_entry, converted); HasError = false;
                    if (IsBoolean) NotifyPropertyChanged(nameof(BooleanValue));
                }
                catch (Exception e) when (e is FormatException or OverflowException) { HasError = true; }
                _changed();
            }
        }
        public CampaignFieldViewModel(object entry, PropertyInfo property, Action beforeEdit, Action changed)
        { _entry = entry; _property = property; _beforeEdit = beforeEdit; _changed = changed; _value = Convert.ToString(property.GetValue(entry), CultureInfo.InvariantCulture) ?? string.Empty; }
    }
}
