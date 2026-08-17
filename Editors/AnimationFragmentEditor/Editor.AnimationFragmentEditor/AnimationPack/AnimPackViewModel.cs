using System.Windows;
using System.ComponentModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationFragmentEditor.AnimationPack.Commands;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinConverter;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationFragmentConverter;
using Editors.AnimationFragmentEditor.AnimationPack.ViewModels;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Ui.Common;
using Shared.Ui.Editors.TextEditor;

namespace CommonControls.Editors.AnimationPack
{
    public partial class AnimPackViewModel : NotifyPropertyChangedImpl, IEditorInterface, ISaveableEditor, IFileEditor
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IPackFileService _pfs;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private ITextConverter? _activeConverter;
        private readonly ApplicationSettingsService _appSettings;
        private readonly IFileSaveService _packFileSaveService;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IStandardDialogs _standardDialogs;

        public string DisplayName { get; set; } = "Not set";

        PackFile _packFile;

        public FilterCollection<IAnimationPackFile> AnimationPackItems { get; set; }

        private string _fileFilterText = string.Empty;
        public string FileFilterText
        {
            get => _fileFilterText;
            set
            {
                SetAndNotifyWhenChanged(ref _fileFilterText, value ?? string.Empty);
                RefreshFileFilter();
            }
        }

        private bool _useRegexFilter;
        public bool UseRegexFilter
        {
            get => _useRegexFilter;
            set
            {
                SetAndNotifyWhenChanged(ref _useRegexFilter, value);
                RefreshFileFilter();
            }
        }

        public bool IsFileFilterInvalid =>
            UseRegexFilter && !AnimationPackItems.FilterValid;

        public bool HasFilterResults => AnimationPackItems.Values.Count > 0;

        public string FilterSummary => LocalizationManager.Instance?.GetFormat(
            "AnimPack.FilterSummary",
            AnimationPackItems.Values.Count,
            AnimationPackItems.PossibleValues.Count) ?? string.Empty;

        public bool HasSelectedItem => AnimationPackItems.SelectedItem != null;

        SimpleTextEditorViewModel _selectedItemViewModel = null!;
        public SimpleTextEditorViewModel SelectedItemViewModel
        {
            get => _selectedItemViewModel;
            set
            {
                if (_selectedItemViewModel != null)
                    _selectedItemViewModel.PropertyChanged -= ChildEditor_PropertyChanged;
                _selectedItemViewModel = value;
                NotifyPropertyChanged(nameof(SelectedItemViewModel));
                if (_selectedItemViewModel != null)
                    _selectedItemViewModel.PropertyChanged += ChildEditor_PropertyChanged;
                NotifyEditState();
            }
        }

        AnimSetTableEditorViewModel _tableEditorVM = null!;
        public AnimSetTableEditorViewModel TableEditorVM
        {
            get => _tableEditorVM;
            set
            {
                if (_tableEditorVM != null)
                    _tableEditorVM.PropertyChanged -= ChildEditor_PropertyChanged;
                _tableEditorVM = value;
                NotifyPropertyChanged(nameof(TableEditorVM));
                if (_tableEditorVM != null)
                    _tableEditorVM.PropertyChanged += ChildEditor_PropertyChanged;
                NotifyEditState();
            }
        }

        bool _isTableView = true;
        public bool IsTableView { get => _isTableView; set => SetAndNotify(ref _isTableView, value); }

        public AnimPackViewModel(IUiCommandFactory uiCommandFactory, 
            IPackFileService pfs, 
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper, 
            ApplicationSettingsService appSettings, 
            IFileSaveService packFileSaveService,
            MetaDataFileParser metaDataFileParser,
            IStandardDialogs standardDialogs)
        {
            _uiCommandFactory = uiCommandFactory;
            _pfs = pfs;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _appSettings = appSettings;
            _packFileSaveService = packFileSaveService;
            _metaDataFileParser = metaDataFileParser;
            _standardDialogs = standardDialogs;
            AnimationPackItems = new FilterCollection<IAnimationPackFile>(new List<IAnimationPackFile>(), OnItemSelected, BeforeItemSelected)
            {
                SearchFilter = (value, rx) => { return rx.Match(value.FileName).Success; }
            };
        }

        public void RefreshFileFilter()
        {
            AnimationPackItems.Filter = UseRegexFilter
                ? FileFilterText
                : Regex.Escape(FileFilterText);
            NotifyPropertyChanged(nameof(IsFileFilterInvalid));
            NotifyPropertyChanged(nameof(HasFilterResults));
            NotifyPropertyChanged(nameof(FilterSummary));
        }

        private bool CanUseSelectedItem() => AnimationPackItems.SelectedItem != null;

        [RelayCommand(CanExecute = nameof(CanUseSelectedItem))]
        private void RenameAction() => _uiCommandFactory.Create<RenameSelectedFileCommand>().Execute(this);
        [RelayCommand(CanExecute = nameof(CanUseSelectedItem))]
        private void RemoveAction() => _uiCommandFactory.Create<RemoveSelectedFileCommand>().Execute(this);
        [RelayCommand(CanExecute = nameof(CanUseSelectedItem))]
        private void CopyFullPathAction()
        {
            if (AnimationPackItems.SelectedItem is { } selectedItem)
                Clipboard.SetText(selectedItem.FileName);
        }
        [RelayCommand] private void CreateEmptyWarhammer3AnimSetFileAction() => _uiCommandFactory.Create<CreateEmptyWarhammer3AnimSetFileCommand>().Execute(this);
        [RelayCommand] private void ExportAnimationSlotsWh3Action() => _uiCommandFactory.Create<ExportAnimationSlotCommand>().Warhammer3();
        [RelayCommand] private void ExportAnimationSlotsWh2Action() => _uiCommandFactory.Create<ExportAnimationSlotCommand>().Warhammer2();

        [RelayCommand] private void SaveAction() => Save();

        [RelayCommand]
        private void ToggleViewMode()
        {
            if (HasUnsavedChildChanges() && !SaveActiveFile())
                return;
            IsTableView = !IsTableView;
        }

        bool BeforeItemSelected(IAnimationPackFile item)
        {
            if (HasUnsavedChildChanges())
            {
                if (_standardDialogs.ShowYesNoBox(
                        GetLocalizedText("Msg.UnsavedChangesLost"),
                        GetLocalizedText("Msg.UnsavedChangesOnQuitTitle")) != ShowMessageBoxResult.OK)
                    return false;
            }

            return true;
        }

        void OnItemSelected(IAnimationPackFile seletedFile)
        {
            _activeConverter = null;
            if (seletedFile is AnimationFragmentFile typedFragment)
                _activeConverter = new AnimationFragmentFileToXmlConverter(_skeletonAnimationLookUpHelper, _appSettings.CurrentSettings.CurrentGame);
            else if (seletedFile is AnimationBin typedBin)
                _activeConverter = new AnimationBinFileToXmlConverter();
            else if (seletedFile is AnimationBinWh3 wh3Bin)
                _activeConverter = new AnimationBinWh3FileToXmlConverter(_skeletonAnimationLookUpHelper, _metaDataFileParser, CurrentFile);

            if (seletedFile == null || _activeConverter == null || seletedFile.IsUnknownFile)
            {
                SelectedItemViewModel = new SimpleTextEditorViewModel();
                SelectedItemViewModel.SaveCommand = null;
                SelectedItemViewModel.TextEditor?.ShowLineNumbers(true);
                SelectedItemViewModel.TextEditor?.SetSyntaxHighlighting("XML");
                SelectedItemViewModel.Text = "";
                SelectedItemViewModel.ResetChangeLog();
                TableEditorVM = null!;
            }
            else
            {
                // Create text editor vm (for XML view fallback)
                SelectedItemViewModel = new SimpleTextEditorViewModel();
                SelectedItemViewModel.SaveCommand = new RelayCommand(() => SaveActiveFile());
                SelectedItemViewModel.TextEditor?.ShowLineNumbers(true);
                SelectedItemViewModel.TextEditor?.SetSyntaxHighlighting(_activeConverter.GetSyntaxType());
                SelectedItemViewModel.Text = _activeConverter.GetText(seletedFile.ToByteArray());
                SelectedItemViewModel.ResetChangeLog();

                // Create table editor vm
                var tableVM = new AnimSetTableEditorViewModel(
                    _pfs, _skeletonAnimationLookUpHelper, _metaDataFileParser,
                    CurrentFile, _appSettings.CurrentSettings.CurrentGame);
                tableVM.LoadFromBinary(seletedFile.ToByteArray(), seletedFile.FileName);
                tableVM.SaveCommand = new RelayCommand(() => SaveActiveFile());
                TableEditorVM = tableVM;
            }
            NotifySelectionState();
        }
        public void Close() { }
        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges || HasUnsavedChildChanges();
            set
            {
                SetAndNotify(ref _hasUnsavedChanges, value);
                NotifyEditState();
            }
        }

        public bool HasEditConflict =>
            TableEditorVM?.IsDirty == true &&
            SelectedItemViewModel?.HasUnsavedChanges() == true;

        public bool HasEditStatus => HasUnsavedChanges;

        public bool IsSelectedItemUnsupported =>
            AnimationPackItems.SelectedItem != null &&
            (_activeConverter == null || AnimationPackItems.SelectedItem.IsUnknownFile);

        public string EditStatusMessage
        {
            get
            {
                if (HasEditConflict)
                    return GetLocalizedText("AnimPack.Status.EditConflict");
                if (TableEditorVM?.IsDirty == true)
                    return GetLocalizedText("AnimPack.Status.TablePending");
                if (SelectedItemViewModel?.HasUnsavedChanges() == true)
                    return GetLocalizedText("AnimPack.Status.XmlPending");
                if (_hasUnsavedChanges)
                    return GetLocalizedText("AnimPack.Status.PackPending");
                return string.Empty;
            }
        }

        public PackFile CurrentFile => _packFile;

        private bool HasUnsavedChildChanges()
        {
            return TableEditorVM?.IsDirty == true ||
                SelectedItemViewModel?.HasUnsavedChanges() == true;
        }

        private static string GetLocalizedText(string key) =>
            LocalizationManager.Instance?.Get(key) ?? key;

        private void ChildEditor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender == TableEditorVM && e.PropertyName != nameof(AnimSetTableEditorViewModel.IsDirty))
                return;
            if (sender == SelectedItemViewModel && e.PropertyName != nameof(SimpleTextEditorViewModel.Text))
                return;
            NotifyEditState();
        }

        private void NotifyEditState()
        {
            NotifyPropertyChanged(nameof(HasUnsavedChanges));
            NotifyPropertyChanged(nameof(HasEditConflict));
            NotifyPropertyChanged(nameof(HasEditStatus));
            NotifyPropertyChanged(nameof(EditStatusMessage));
        }

        private void NotifySelectionState()
        {
            NotifyPropertyChanged(nameof(HasSelectedItem));
            NotifyPropertyChanged(nameof(IsSelectedItemUnsupported));
            RenameActionCommand.NotifyCanExecuteChanged();
            RemoveActionCommand.NotifyCanExecuteChanged();
            CopyFullPathActionCommand.NotifyCanExecuteChanged();
        }


        public bool SaveActiveFile()
        {
            if (_packFile == null)
            {
                _standardDialogs.ShowDialogBox(
                    GetLocalizedText("Msg.CannotSaveInThisMode"),
                    GetLocalizedText("Msg.GeneralError"));
                return false;
            }

            var selectedFile = AnimationPackItems.SelectedItem;
            var converter = _activeConverter;
            var textEditor = SelectedItemViewModel;
            if (selectedFile == null || converter == null || textEditor == null)
            {
                _standardDialogs.ShowDialogBox(
                    GetLocalizedText("Msg.CannotSaveInThisMode"),
                    GetLocalizedText("Msg.GeneralError"));
                return false;
            }

            var tableDirty = TableEditorVM?.IsDirty == true;
            var xmlDirty = SelectedItemViewModel?.HasUnsavedChanges() == true;
            if (tableDirty && xmlDirty)
            {
                NotifyEditState();
                _standardDialogs.ShowDialogBox(
                    EditStatusMessage,
                    GetLocalizedText("Msg.GeneralError"));
                return false;
            }

            var fileName = selectedFile.FileName;
            byte[]? bytes;
            ITextConverter.SaveError? error;
            var saveTable = (tableDirty && !xmlDirty) ||
                (tableDirty == xmlDirty && IsTableView);
            var tableEditorToSave = saveTable ? TableEditorVM : null;

            if (tableEditorToSave != null)
            {
                bytes = tableEditorToSave.SaveToBinary(fileName, out error);
            }
            else
            {
                bytes = converter.ToBytes(textEditor.Text, fileName, _pfs, out error);
            }

            if (bytes == null || error != null)
            {
                if (error != null && textEditor.TextEditor != null)
                    textEditor.TextEditor.HightLightText(error.ErrorLineNumber, error.ErrorPosition, error.ErrorLength);
                _standardDialogs.ShowDialogBox(
                    error?.Text ?? GetLocalizedText("Msg.UnknownError"),
                    GetLocalizedText("Msg.GeneralError"));
                return false;
            }

            selectedFile.CreateFromBytes(bytes);
            selectedFile.IsChanged.Value = true;

            if (tableEditorToSave != null)
            {
                tableEditorToSave.IsDirty = false;
                textEditor.Text = converter.GetText(bytes);
                textEditor.ResetChangeLog();
            }
            else
            {
                textEditor.ResetChangeLog();
                TableEditorVM?.LoadFromBinary(bytes, fileName);
            }
            HasUnsavedChanges = true;

            return true;
        }


        public bool Save()
        {
            if (_packFile == null)
            {
                _standardDialogs.ShowDialogBox(
                    GetLocalizedText("Msg.CannotSaveInThisMode"),
                    GetLocalizedText("Msg.GeneralError"));
                return false;
            }

            var tableDirty = TableEditorVM?.IsDirty == true;
            var xmlDirty = SelectedItemViewModel?.HasUnsavedChanges() == true;
            if (tableDirty && xmlDirty)
            {
                NotifyEditState();
                _standardDialogs.ShowDialogBox(
                    EditStatusMessage,
                    GetLocalizedText("Msg.GeneralError"));
                return false;
            }

            if (tableDirty || xmlDirty)
            {
                if (!SaveActiveFile() || HasUnsavedChildChanges())
                    return false;
            }

            var newAnimPack = new AnimationPackFileDatabase(_pfs.GetFullPath(_packFile));

            foreach (var file in AnimationPackItems.PossibleValues)
                newAnimPack.AddFile(file);

            var savePath = _pfs.GetFullPath(_packFile);

            var result = _packFileSaveService.Save(savePath, AnimationPackSerializer.ConvertToBytes(newAnimPack), false);
            if (result == null)
                return false;

            HasUnsavedChanges = false;
            foreach (var file in AnimationPackItems.PossibleValues)
                file.IsChanged.Value = false;
            return true;
        }


        public void LoadFile(PackFile file)
        {
            _packFile = file;
            var animPack = AnimationPackSerializer.Load(_packFile, _pfs);
            var itemNames = animPack.Files.ToList();
            AnimationPackItems.UpdatePossibleValues(itemNames);
            RefreshFileFilter();
            NotifySelectionState();
            DisplayName = animPack.FileName;
        }
    }
}
