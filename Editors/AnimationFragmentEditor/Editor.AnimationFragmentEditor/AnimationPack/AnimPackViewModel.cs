using System.Windows;
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

        public string DisplayName { get; set; } = "Not set";

        PackFile _packFile;

        public FilterCollection<IAnimationPackFile> AnimationPackItems { get; set; }

        SimpleTextEditorViewModel _selectedItemViewModel;
        public SimpleTextEditorViewModel SelectedItemViewModel { get => _selectedItemViewModel; set => SetAndNotify(ref _selectedItemViewModel, value); }

        AnimSetTableEditorViewModel _tableEditorVM;
        public AnimSetTableEditorViewModel TableEditorVM { get => _tableEditorVM; set => SetAndNotify(ref _tableEditorVM, value); }

        bool _isTableView = true;
        public bool IsTableView { get => _isTableView; set => SetAndNotify(ref _isTableView, value); }

        public AnimPackViewModel(IUiCommandFactory uiCommandFactory, 
            IPackFileService pfs, 
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper, 
            ApplicationSettingsService appSettings, 
            IFileSaveService packFileSaveService,
            MetaDataFileParser metaDataFileParser)
        {
            _uiCommandFactory = uiCommandFactory;
            _pfs = pfs;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _appSettings = appSettings;
            _packFileSaveService = packFileSaveService;
            _metaDataFileParser = metaDataFileParser;
            AnimationPackItems = new FilterCollection<IAnimationPackFile>(new List<IAnimationPackFile>(), OnItemSelected, BeforeItemSelected)
            {
                SearchFilter = (value, rx) => { return rx.Match(value.FileName).Success; }
            };
        }

        [RelayCommand] private void RenameAction() => _uiCommandFactory.Create<RenameSelectedFileCommand>().Execute(this);
        [RelayCommand] private void RemoveAction() => _uiCommandFactory.Create<RemoveSelectedFileCommand>().Execute(this);
        [RelayCommand] private void CopyFullPathAction() => Clipboard.SetText(AnimationPackItems.SelectedItem.FileName);
        [RelayCommand] private void CreateEmptyWarhammer3AnimSetFileAction() => _uiCommandFactory.Create<CreateEmptyWarhammer3AnimSetFileCommand>().Execute(this);
        [RelayCommand] private void ExportAnimationSlotsWh3Action() => _uiCommandFactory.Create<ExportAnimationSlotCommand>().Warhammer3();
        [RelayCommand] private void ExportAnimationSlotsWh2Action() => _uiCommandFactory.Create<ExportAnimationSlotCommand>().Warhammer2();

        [RelayCommand] private void SaveAction() => Save();
        [RelayCommand] private void ToggleViewMode() => IsTableView = !IsTableView;

        bool BeforeItemSelected(IAnimationPackFile item)
        {
            if (HasUnsavedChildChanges())
            {
                if (MessageBox.Show(LocalizationManager.Instance.Get("Msg.UnsavedChangesLost"), "", MessageBoxButton.YesNo) == MessageBoxResult.No)
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
                TableEditorVM = null;
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

        }
        public void Close() { }
        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges || HasUnsavedChildChanges();
            set => SetAndNotify(ref _hasUnsavedChanges, value);
        }

        public PackFile CurrentFile => _packFile;

        private bool HasUnsavedChildChanges()
        {
            return TableEditorVM?.IsDirty == true ||
                SelectedItemViewModel?.HasUnsavedChanges() == true;
        }


        public bool SaveActiveFile()
        {
            if (_packFile == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.CannotSaveInThisMode"));
                return false;
            }

            var tableDirty = TableEditorVM?.IsDirty == true;
            var xmlDirty = SelectedItemViewModel?.HasUnsavedChanges() == true;
            if (tableDirty && xmlDirty)
                return false;

            var fileName = AnimationPackItems.SelectedItem.FileName;
            byte[] bytes;
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
                bytes = _activeConverter.ToBytes(SelectedItemViewModel.Text, fileName, _pfs, out error);
            }

            if (bytes == null || error != null)
            {
                if (error != null && SelectedItemViewModel?.TextEditor != null)
                    SelectedItemViewModel.TextEditor.HightLightText(error.ErrorLineNumber, error.ErrorPosition, error.ErrorLength);
                MessageBox.Show(error?.Text ?? "Unknown error", LocalizationManager.Instance.Get("Msg.GeneralError"));
                return false;
            }

            var seletedFile = AnimationPackItems.SelectedItem;
            seletedFile.CreateFromBytes(bytes);
            seletedFile.IsChanged.Value = true;

            if (tableEditorToSave != null)
            {
                tableEditorToSave.IsDirty = false;
                SelectedItemViewModel.Text = _activeConverter.GetText(bytes);
                SelectedItemViewModel.ResetChangeLog();
            }
            else
            {
                SelectedItemViewModel.ResetChangeLog();
                TableEditorVM?.LoadFromBinary(bytes, fileName);
            }
            HasUnsavedChanges = true;

            return true;
        }


        public bool Save()
        {
            if (_packFile == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.CannotSaveInThisMode"));
                return false;
            }

            var tableDirty = TableEditorVM?.IsDirty == true;
            var xmlDirty = SelectedItemViewModel?.HasUnsavedChanges() == true;
            if (tableDirty && xmlDirty)
                return false;

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
            DisplayName = animPack.FileName;
        }
    }
}
