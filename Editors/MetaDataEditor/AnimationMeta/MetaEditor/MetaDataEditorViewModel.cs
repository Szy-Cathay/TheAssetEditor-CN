using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.MetaEditor.Commands;
using Shared.Core.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.Presentation
{
    public partial class MetaDataEditorViewModel : ObservableObject, IEditorInterface, ISaveableEditor, IFileEditor
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IEventHub _eventHub;
        private bool _hasStructuralChanges;
        public event Action<MetaDataEditorViewModel>? StructureChanged;
        public ParsedMetadataFile? ParsedFile { get; private set; }
        public ParsedMetadataAttribute? SelectedAttribute { get; private set; }

        [ObservableProperty] string _displayName = "Metadata Editor";
        [ObservableProperty] MetaDataEntry? _selectedTag;
        [ObservableProperty] ObservableCollection<MetaDataEntry> _tags = [];
        [ObservableProperty] int _metaDataFileVersion;

        public bool HasUnsavedChanges
        {
            get
            {
                if (_hasStructuralChanges)
                    return true;

                foreach (var tag in Tags)
                {
                    foreach (var variable in tag.Variables)
                    {
                        if (variable.IsModified)
                            return true;

                    }
                }
                return false;

            }
            set
            {
                _hasStructuralChanges = value;
                if (value == false)
                {
                    foreach (var tag in Tags)
                    {
                        foreach (var variable in tag.Variables)
                            variable.IsModified = false;
                    }
                }

                OnPropertyChanged();
            }

        }
        public PackFile CurrentFile { get; set; } = null!;

        public MetaDataEditorViewModel(IUiCommandFactory uiCommandFactory, MetaDataFileParser metaDataFileParser, IEventHub eventHub)
        {
            _uiCommandFactory = uiCommandFactory;
            _metaDataFileParser = metaDataFileParser;
            _eventHub = eventHub;
        }

        partial void OnSelectedTagChanged(MetaDataEntry? value)
        {
            if (value == null)

                SelectedAttribute = null;
            else
                SelectedAttribute = value._input;

            _eventHub.Publish(new SelecteMetaDataAttributeChangedEvent());
        }

        public bool Save() => _uiCommandFactory.Create<SaveCommand>().Execute(this);
        public bool CreateNewFile(string path) =>
            _uiCommandFactory.Create<CreateEmptyMetaDataFileCommand>()
                .Execute(this, path);
        public void Close() { }

        public void LoadFile(PackFile? file)
        {
            if (file == CurrentFile)
                return;

            CurrentFile = file!;
            DisplayName = file == null ? "Metadata Editor" : file.Name;

            if (file == null)
            {
                ParsedFile = null;
                SelectedAttribute = null;
                Tags.Clear();
                SelectedTag = null;
            }
            else
            {
                var fileContent = CurrentFile.DataSource.ReadData();
                ParsedFile = _metaDataFileParser.ParseFile(fileContent);
                MetaDataFileVersion = ParsedFile.Version;
            }

            UpdateView(false);
            HasUnsavedChanges = false;
        }

        public void UpdateView(bool trackStructureChange = true)
        {
            var structureChanged = trackStructureChange &&
                ParsedFile != null &&
                Tags.Select(tag => tag._input)
                    .SequenceEqual(ParsedFile.Attributes) == false;

            Tags.Clear();
            if (ParsedFile == null)
                return;

            foreach (var metadataEntry in ParsedFile.Attributes)
            {
                if (metadataEntry is ParsedUnknownMetadataAttribute uknMeta)
                {
                    var desc = LocalizeTagDescription(uknMeta.Name, _metaDataFileParser.GetDatabase().GetDescriptionSafe(uknMeta.Name));
                    Tags.Add(new MetaDataEntry(uknMeta, desc, _eventHub, false));
                }
                else if (metadataEntry is ParsedMetadataAttribute parsedKnownAttribute)
                {
                    var desc = LocalizeTagDescription(parsedKnownAttribute.Name, _metaDataFileParser.GetDatabase().GetDescriptionSafe(parsedKnownAttribute.Name));
                    Tags.Add(new MetaDataEntry(parsedKnownAttribute, desc, _eventHub, true));
                }
                else
                    throw new Exception($"{metadataEntry.GetType()} is not a known type for {nameof(MetaDataEditorViewModel)}");
            }

            if (structureChanged)
                MarkStructureChanged();
        }

        public void RefreshAttributeValues(ParsedMetadataAttribute attribute)
        {
            var entry = Tags.FirstOrDefault(tag =>
                ReferenceEquals(tag._input, attribute));
            if (entry == null)
                return;

            foreach (var variable in entry.Variables)
                variable.RefreshFromTarget();
        }

        internal void MarkStructureChanged()
        {
            StructureChanged?.Invoke(this);
            if (_hasStructuralChanges)
                return;

            _hasStructuralChanges = true;
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        [RelayCommand] void MoveUpAction() => _uiCommandFactory.Create<MoveEntryCommand>().ExecuteUp(this);
        [RelayCommand] void MoveDownAction() => _uiCommandFactory.Create<MoveEntryCommand>().ExecuteDown(this);
        [RelayCommand] void DeleteAction() => _uiCommandFactory.Create<DeleteEntryCommand>().Execute(this);
        [RelayCommand] void NewAction() => _uiCommandFactory.Create<NewEntryCommand>().Execute(this);
        [RelayCommand] void PasteAction() => _uiCommandFactory.Create<CopyPastCommand>().ExecutePaste(this);
        [RelayCommand] void CopyAction() => _uiCommandFactory.Create<CopyPastCommand>().ExecuteCopy(this);
        [RelayCommand] void SaveAction() => _uiCommandFactory.Create<SaveCommand>().Execute(this);

        static string LocalizeTagDescription(string tagName, string fallbackDescription)
        {
            try
            {
                if (LocalizationManager.Instance != null)
                {
                    var localized = LocalizationManager.Instance.Get($"MetaData.TagDesc.{tagName}");
                    if (localized != $"MetaData.TagDesc.{tagName}")
                        return localized;
                }
            }
            catch { }

            return fallbackDescription;
        }
    }
}

