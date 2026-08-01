using System.Windows;
using System.Windows.Input;
using CommonControls.Editors.TextEditor;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;

namespace Shared.Ui.Editors.TextEditor
{
    public interface ITextEditorViewModel
    {
        void SetEditor(ITextEditor theEditor);
    }

    public class TextEditorViewModel<TextConverter> : NotifyPropertyChangedImpl, ITextEditorViewModel, IEditorInterface, IFileEditor, ISaveableEditor
        where TextConverter : ITextConverter
    {
        public ICommand SaveCommand { get; set; }

        public string DisplayName { get; set; } = "Not set";

        string _text;
        public string Text
        {
            get => _text;
            set => SetAndNotifyWhenChanged(
                ref _text,
                value,
                _ => HasUnsavedChanges = true);
        }

        bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetAndNotifyWhenChanged(
                ref _hasUnsavedChanges,
                value);
        }

        PackFile _packFile;
        private readonly IFileSaveService _packFileSaveService;
        private readonly IPackFileService _pf;

        ITextEditor _textEditor;
        TextConverter _converter;

        public TextEditorViewModel(IFileSaveService packFileSaveService, IPackFileService pf, TextConverter converter)
        {
            _converter = converter;
      
            _packFileSaveService = packFileSaveService;
            _pf = pf;
            SaveCommand = new RelayCommand(() => Save());
        }

        public void SetEditor(ITextEditor theEditor)
        {
            _textEditor = theEditor;
            _textEditor.ClearUndoStack();
            _textEditor.ShowLineNumbers(_converter.ShouldShowLineNumbers());
            _textEditor.SetSyntaxHighlighting(_converter.GetSyntaxType());
        }

        public PackFile MainFile
        {
            get => _packFile;
            set
            {
                _packFile = value;
                SetCurrentPackFile(_packFile);
            }
        }

        public void LoadFile(PackFile file)
        {
            _packFile = file;
            SetCurrentPackFile(_packFile);
        }
        public PackFile CurrentFile => _packFile;


        void SetCurrentPackFile(PackFile packedFile)
        {
            var file = packedFile;
            DisplayName = file.Name;

            var data = file.DataSource.ReadData();
            _text = _converter.GetText(data);
            NotifyPropertyChanged(nameof(Text));
            HasUnsavedChanges = false;
        }

        public bool Save()
        {
            var path = _pf.GetFullPath(MainFile);

            var bytes = _converter.ToBytes(Text, path, _pf, out var error);
            if (bytes == null || error != null)
            {
                _textEditor.HightLightText(error.ErrorLineNumber, error.ErrorPosition, error.ErrorLength);

                if (_converter.CanSaveOnError())
                {
                    if (MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.SaveWithErrors", error.Text), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButton.YesNo) == MessageBoxResult.No)
                        return false;
                }
                else
                {
                    MessageBox.Show(error.Text, LocalizationManager.Instance.Get("Msg.GeneralError"));
                    return false;
                }
            }

            var res = _packFileSaveService.Save(path, bytes, false);
            if (res == null)
                return false;

            MainFile = res;
            return true;
        }

        public void Close()
        {
        }
    }


    public class SimpleTextEditorViewModel : NotifyPropertyChangedImpl, ITextEditorViewModel
    {
        ICommand _saveCommand;
        public ICommand SaveCommand { get => _saveCommand; set => SetAndNotify(ref _saveCommand, value); }

        string _text;
        public string Text
        {
            get => _text;
            set
            {
                SetAndNotify(ref _text, value);
                _textChanged = true;
            }
        }

        bool _textChanged = false;
        public ITextEditor TextEditor { get; private set; }

        public SimpleTextEditorViewModel()
        {
            SaveCommand = new RelayCommand(() => Save());
        }

        public bool Save() { return true; }

        public void SetEditor(ITextEditor theEditor)
        {
            TextEditor = theEditor;
            TextEditor.ClearUndoStack();
        }

        public bool HasUnsavedChanges()
        {
            return _textChanged;
        }

        public void ResetChangeLog()
        {
            _textChanged = false;
            TextEditor?.ClearUndoStack();
        }
    }
}
