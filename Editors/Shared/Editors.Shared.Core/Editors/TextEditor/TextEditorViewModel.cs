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

    public class TextEditorViewModel<TextConverter> : NotifyPropertyChangedImpl, ITextEditorViewModel, IEditorInterface, IFileEditor, ISaveableEditor, IAssistantTextDocument
        where TextConverter : ITextConverter
    {
        public ICommand SaveCommand { get; set; }

        public string DisplayName { get; set; } = "Not set";

        string _text;
        string _savedText;
        public string Text
        {
            get => _text;
            set => SetAndNotifyWhenChanged(
                ref _text,
                value,
                _ => HasUnsavedChanges = _text != _savedText);
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
            _savedText = _text;
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
                    if (MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.SaveWithErrors", error.Text), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButton.YesNo) != MessageBoxResult.Yes)
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

        public bool AssistantHistoryAvailable => _textEditor?.SupportsAssistantHistory == true;
        public bool CanUndo => _textEditor?.CanUndo == true;
        public bool CanRedo => _textEditor?.CanRedo == true;
        public void Undo() => _textEditor.Undo();
        public void Redo() => _textEditor.Redo();
        public void Replace(int offset, int length, string replacement)
        {
            var next = Text.Remove(offset, length).Insert(offset, replacement);
            ValidateText(next);
            _textEditor.ReplaceText(offset, length, replacement);
            if (Text != next) throw new InvalidOperationException(Shared.Core.ModAssistant.AssistantJson.Text("ReadbackFailed"));
        }
        public void ValidateText(string text)
        {
            var path = _pf.GetFullPath(MainFile);
            if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wsmodel", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".variantmeshdefinition", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(text), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
                while (reader.Read()) { }
            }
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                using (System.Text.Json.JsonDocument.Parse(text)) { }
            var bytes = _converter.ToBytes(text, path, _pf, out var error);
            if (bytes == null || error != null) throw new ArgumentException(error?.Text ?? Shared.Core.ModAssistant.AssistantJson.Text("InvalidParameters"));
            if (_converter is DefaultTextConverter && text.Any(character => character > 127))
                throw new ArgumentException(Shared.Core.ModAssistant.AssistantJson.Text("TextEncoding"));
        }
    }


    public class SimpleTextEditorViewModel : NotifyPropertyChangedImpl, ITextEditorViewModel
    {
        ICommand _saveCommand;
        public ICommand SaveCommand { get => _saveCommand; set => SetAndNotify(ref _saveCommand, value); }

        string _text;
        string _savedText;
        public string Text
        {
            get => _text;
            set
            {
                _textChanged = value != _savedText;
                SetAndNotify(ref _text, value);
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
            _savedText = _text;
            _textChanged = false;
            TextEditor?.ClearUndoStack();
        }
    }
}
