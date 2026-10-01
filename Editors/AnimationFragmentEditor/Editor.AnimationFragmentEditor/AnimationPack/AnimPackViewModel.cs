using System.Windows;
using System.ComponentModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationFragmentEditor.AnimationPack.Commands;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinConverter;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationFragmentConverter;
using Editors.AnimationFragmentEditor.AnimationPack.ViewModels;
using Editors.AnimationFragmentEditor.CampaignAnimBin;
using Shared.ByteParsing;
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
        private bool _isSelectingFile;

        public string DisplayName { get; set; } = "Not set";

        PackFile _packFile;

        public FilterCollection<IAnimationPackFile> AnimationPackItems { get; set; }

        private string _fileFilterText = string.Empty;
        public string FileFilterText
        {
            get => _fileFilterText;
            set
            {
                SetAndNotifyWhenChanged(ref _fileFilterText, value ?? string.Empty, _ => RefreshFileFilter());
            }
        }

        private bool _useRegexFilter;
        public bool UseRegexFilter
        {
            get => _useRegexFilter;
            set
            {
                SetAndNotifyWhenChanged(ref _useRegexFilter, value, _ => RefreshFileFilter());
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
        public bool IsTableView
        {
            get => _isTableView;
            set
            {
                SetAndNotifyWhenChanged(ref _isTableView, value);
                NotifyPropertyChanged(nameof(ShowBattleTable));
                NotifyPropertyChanged(nameof(ShowCampaignTable));
            }
        }
        public bool IsStandaloneCampaign { get; private set; }
        public string SaveTitle => GetLocalizedText(IsStandaloneCampaign ? "AnimPack.Campaign.SaveFile" : "AnimPack.SavePack");
        public bool IsCampaignSelected => CampaignEditorVM != null;
        public bool ShowBattleTable => IsTableView && TableEditorVM != null;
        public bool ShowCampaignTable => IsTableView && CampaignEditorVM != null;
        public bool CanToggleView => TableEditorVM != null || CampaignEditorVM != null;
        private CampaignTableEditorViewModel? _campaignEditorVM;
        public CampaignTableEditorViewModel? CampaignEditorVM
        {
            get => _campaignEditorVM;
            set
            {
                if (_campaignEditorVM != null) _campaignEditorVM.PropertyChanged -= ChildEditor_PropertyChanged;
                _campaignEditorVM = value;
                if (_campaignEditorVM != null) _campaignEditorVM.PropertyChanged += ChildEditor_PropertyChanged;
                NotifyPropertyChanged(); NotifyEditState();
            }
        }

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
                SearchFilter = (value, rx) => (!OnlyEditableFiles || IsEditable(value)) && rx.Match(value.FileName).Success
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

        private bool CanUseSelectedItem() => AnimationPackItems.SelectedItem != null && !IsStandaloneCampaign;

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
        [RelayCommand(CanExecute = nameof(CanCreateFile))] private void CreateEmptyWarhammer3AnimSetFileAction() => _uiCommandFactory.Create<CreateEmptyWarhammer3AnimSetFileCommand>().Execute(this);
        [RelayCommand] private void ExportAnimationSlotsWh3Action() => _uiCommandFactory.Create<ExportAnimationSlotCommand>().Warhammer3();
        [RelayCommand] private void ExportAnimationSlotsWh2Action() => _uiCommandFactory.Create<ExportAnimationSlotCommand>().Warhammer2();

        [RelayCommand] private void SaveAction() => Save();

        [RelayCommand(CanExecute = nameof(CanToggleView))]
        private void ToggleViewMode()
        {
            if (!CommitInputs()) return;
            if (HasUnsavedChildChanges() && !SaveActiveFile())
                return;
            IsTableView = !IsTableView;
            NotifySelectionState();
        }

        bool BeforeItemSelected(IAnimationPackFile item)
        {
            if (_isCommittingInputs || _isSelectingFile || ReferenceEquals(item, AnimationPackItems.SelectedItem)) return false;
            _isSelectingFile = true;
            try
            {
                if (!CommitInputs()) return false;
                if (HasUnsavedChildChanges())
                {
                    if (_standardDialogs.ShowYesNoBox(
                            GetLocalizedText("AnimPack.ApplyBeforeSwitch"),
                            GetLocalizedText("AnimPack.ApplyBeforeSwitch.Title")) != ShowMessageBoxResult.OK)
                        return false;
                    return SaveActiveFile();
                }

                return true;
            }
            finally { _isSelectingFile = false; }
        }

        void OnItemSelected(IAnimationPackFile seletedFile)
        {
            CampaignEditorVM = null;
            _activeConverter = null;
            if (seletedFile is AnimationFragmentFile typedFragment)
                _activeConverter = new AnimationFragmentFileToXmlConverter(_skeletonAnimationLookUpHelper, _appSettings.CurrentSettings.CurrentGame);
            else if (seletedFile is AnimationBin typedBin)
                _activeConverter = new AnimationBinFileToXmlConverter();
            else if (seletedFile is AnimationBinWh3 wh3Bin)
                _activeConverter = new AnimationBinWh3FileToXmlConverter(_skeletonAnimationLookUpHelper, _metaDataFileParser, CurrentFile);
            else if (seletedFile is CampaignAnimationPackFile)
                _activeConverter = new CampaignAnimBinToXmlConverter();

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

                TableEditorVM = null!;
                if (seletedFile is CampaignAnimationPackFile)
                {
                    var campaign = new CampaignTableEditorViewModel(_pfs, _standardDialogs)
                    {
                        IsStandaloneFile = IsStandaloneCampaign,
                        SaveCommand = new RelayCommand(() => { if (IsStandaloneCampaign) Save(); else SaveActiveFile(); }),
                        OpenResource = OpenReferencedResource,
                        Preview = PreviewAnimation,
                    };
                    campaign.LoadFromBinary(seletedFile.ToByteArray(), seletedFile.FileName);
                    var resources = ResourcePaths;
                    campaign.SkeletonNames = resources.Where(p => p.Replace('\\', '/').Contains("/skeletons/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)).Select(p => System.IO.Path.GetFileNameWithoutExtension(p.Replace('\\', '/'))).Distinct().ToList();
                    campaign.AnimationFiles = resources.Where(p => p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)).ToList();
                    campaign.MetaFiles = resources.Where(p => p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".snd.meta", StringComparison.OrdinalIgnoreCase)).ToList();
                    campaign.SoundFiles = resources.Where(p => p.EndsWith(".snd.meta", StringComparison.OrdinalIgnoreCase)).ToList();
                    CampaignEditorVM = campaign;
                }
                else if (seletedFile is AnimationBin)
                    IsTableView = false;
                else
                {
                    var tableVM = new AnimSetTableEditorViewModel(
                        _pfs, _skeletonAnimationLookUpHelper, _metaDataFileParser,
                        CurrentFile, _appSettings.CurrentSettings.CurrentGame);
                    tableVM.LoadFromBinary(seletedFile.ToByteArray(), seletedFile.FileName);
                    tableVM.SetResourceNames(ResourcePaths);
                    tableVM.SaveCommand = new RelayCommand(() => SaveActiveFile());
                    tableVM.ShowValidation = report => _standardDialogs.ShowErrorViewDialog(GetLocalizedText("AnimPack.Validation.Title"), report);
                    tableVM.OpenResource = OpenReferencedResource;
                    tableVM.PreviewRow = row => PreviewAnimation(row.AnimationFile, tableVM.IsWh3 ? tableVM.SkeletonName : tableVM.Skeleton, row.MetaFile, row.SoundFile);
                    TableEditorVM = tableVM;
                }
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
            (TableEditorVM?.IsDirty == true || CampaignEditorVM?.IsDirty == true) &&
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
                if (IsStandaloneCampaign && HasUnsavedChanges)
                    return GetLocalizedText("AnimPack.Campaign.UnsavedFile");
                if (TableEditorVM?.IsDirty == true || CampaignEditorVM?.IsDirty == true)
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
                CampaignEditorVM?.IsDirty == true ||
                SelectedItemViewModel?.HasUnsavedChanges() == true;
        }

        private static string GetLocalizedText(string key) =>
            LocalizationManager.Instance?.Get(key) ?? key;

        private void ChildEditor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender == TableEditorVM && e.PropertyName != nameof(AnimSetTableEditorViewModel.IsDirty))
                return;
            if (sender == CampaignEditorVM && e.PropertyName != nameof(CampaignTableEditorViewModel.IsDirty))
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
            NotifyPropertyChanged(nameof(IsStandaloneCampaign));
            NotifyPropertyChanged(nameof(SaveTitle));
            NotifyPropertyChanged(nameof(IsCampaignSelected));
            NotifyPropertyChanged(nameof(ShowBattleTable));
            NotifyPropertyChanged(nameof(ShowCampaignTable));
            NotifyPropertyChanged(nameof(CanToggleView));
            ToggleViewModeCommand.NotifyCanExecuteChanged();
            RenameActionCommand.NotifyCanExecuteChanged();
            RemoveActionCommand.NotifyCanExecuteChanged();
            CopyFullPathActionCommand.NotifyCanExecuteChanged();
            CreateAnimationSetCommand.NotifyCanExecuteChanged();
            CreateCampaignFileCommand.NotifyCanExecuteChanged();
            CreateEmptyWarhammer3AnimSetFileActionCommand.NotifyCanExecuteChanged();
            CloneSelectedFileCommand.NotifyCanExecuteChanged();
        }


        public bool SaveActiveFile()
        {
            if (!CommitInputs()) return false;
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

            var tableDirty = TableEditorVM?.IsDirty == true || CampaignEditorVM?.IsDirty == true;
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

            try
            {
                if (saveTable && CampaignEditorVM != null)
                    bytes = CampaignEditorVM.SaveToBinary(fileName, out error);
                else if (tableEditorToSave != null)
                    bytes = tableEditorToSave.SaveToBinary(fileName, out error);
                else
                    bytes = converter.ToBytes(textEditor.Text, fileName, _pfs, out error);
            }
            catch (Exception e)
            {
                _standardDialogs.ShowExceptionWindow(e, GetLocalizedText("Msg.GeneralError"));
                return false;
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

            try
            {
                _ = selectedFile switch
                {
                    CampaignAnimationPackFile => (IAnimationPackFile)new CampaignAnimationPackFile(fileName, bytes),
                    AnimationBinWh3 => new AnimationBinWh3(fileName, bytes),
                    AnimationFragmentFile => new AnimationFragmentFile(fileName, bytes, _appSettings.CurrentSettings.CurrentGame),
                    AnimationBin => new AnimationBin(fileName, bytes),
                    _ => new UnknownAnimFile(fileName, bytes),
                };
                selectedFile.CreateFromBytes(bytes);
            }
            catch (Exception e)
            {
                _standardDialogs.ShowExceptionWindow(e, GetLocalizedText("Msg.GeneralError"));
                return false;
            }
            selectedFile.IsChanged.Value = true;

            if (saveTable && CampaignEditorVM != null)
            {
                CampaignEditorVM.MarkSaved();
                textEditor.Text = converter.GetText(bytes);
                textEditor.ResetChangeLog();
            }
            else if (tableEditorToSave != null)
            {
                tableEditorToSave.IsDirty = false;
                textEditor.Text = converter.GetText(bytes);
                textEditor.ResetChangeLog();
            }
            else
            {
                textEditor.ResetChangeLog();
                TableEditorVM?.LoadFromBinary(bytes, fileName);
                CampaignEditorVM?.LoadFromBinary(bytes, fileName);
            }
            HasUnsavedChanges = true;

            return true;
        }


        public bool Save()
        {
            if (!CommitInputs()) return false;
            if (_packFile == null)
            {
                _standardDialogs.ShowDialogBox(
                    GetLocalizedText("Msg.CannotSaveInThisMode"),
                    GetLocalizedText("Msg.GeneralError"));
                return false;
            }

            var tableDirty = TableEditorVM?.IsDirty == true || CampaignEditorVM?.IsDirty == true;
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

            var outputBytes = IsStandaloneCampaign ? AnimationPackItems.PossibleValues.Single().ToByteArray() : AnimationPackSerializer.ConvertToBytes(newAnimPack);
            var result = _packFileSaveService.Save(savePath, outputBytes, false);
            if (result == null)
                return false;
            _packFile = result;

            HasUnsavedChanges = false;
            foreach (var file in AnimationPackItems.PossibleValues)
                file.IsChanged.Value = false;
            return true;
        }


        public void LoadFile(PackFile file)
        {
            _packFile = file;
            var path = _pfs.GetFullPath(file);
            IsStandaloneCampaign = path.Replace('\\', '/').Contains("animations/campaign/database/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);
            var animPack = IsStandaloneCampaign ? new AnimationPackFileDatabase(path) : AnimationPackSerializer.Load(_packFile, _pfs, _appSettings.CurrentSettings.CurrentGame);
            if (IsStandaloneCampaign) animPack.AddFile(new CampaignAnimationPackFile(path, file.DataSource.ReadData()));
            var itemNames = animPack.Files.ToList();
            AnimationPackItems.UpdatePossibleValues(itemNames);
            _resourcePaths = null;
            OnlyEditableFiles = true;
            RefreshFileFilter();
            NotifySelectionState();
            DisplayName = animPack.FileName;
            if (IsStandaloneCampaign) AnimationPackItems.SelectedItem = itemNames.Single();
        }
    }
}
