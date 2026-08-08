// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Search;
using Shared.Core.Settings;
using Shared.Ui.Editors.TextEditor;

namespace CommonControls.Editors.TextEditor
{
    public interface ITextEditor
    {
        void ClearUndoStack();
        void SetSyntaxHighlighting(string type);
        void ShowLineNumbers(bool value);
        void HightLightText(int lineNumber, int offset, int length);
    }

    /// <summary>
    /// Interaction logic for TextEditorView.xaml
    /// </summary>
    public partial class TextEditorView : UserControl, ITextEditor
    {
        FoldingManager _foldingManager;
        object _foldingStrategy;
        private readonly DispatcherTimer _foldingUpdateTimer;
        private ThemeAwareHighlightingColorizer? _themeHighlightingColorizer;

        internal bool IsFoldingTimerEnabled => _foldingUpdateTimer.IsEnabled;

        public TextEditorView()
        {
            InitializeComponent();

            DataContextChanged += TextEditorView_DataContextChanged;
            Loaded += TextEditorView_Loaded;
            Unloaded += TextEditorView_Unloaded;

            SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Display);
            SearchPanel.Install(textEditor);
            UseThemeAwareSyntaxColorizer(textEditor.SyntaxHighlighting);

            _foldingUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _foldingUpdateTimer.Tick += FoldingUpdateTimer_Tick;
        }

        private void TextEditorView_Loaded(object sender, RoutedEventArgs e)
        {
            _foldingUpdateTimer.Start();
            ThemesController.ThemeChanged -= TextEditorView_ThemeChanged;
            ThemesController.ThemeChanged += TextEditorView_ThemeChanged;
        }

        private void TextEditorView_Unloaded(object sender, RoutedEventArgs e)
        {
            _foldingUpdateTimer.Stop();
            ThemesController.ThemeChanged -= TextEditorView_ThemeChanged;
        }

        private void TextEditorView_ThemeChanged(ThemeType theme)
        {
            if (Dispatcher.CheckAccess())
                textEditor.TextArea.TextView.Redraw();
            else
                Dispatcher.BeginInvoke(
                    textEditor.TextArea.TextView.Redraw);
        }

        private void FoldingUpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateFoldings();
        }

        public void ClearUndoStack()
        {
            textEditor.Document.UndoStack.ClearAll();
        }

        public void SetSyntaxHighlighting(string type)
        {
            var xmlHightlight = HighlightingManager.Instance.HighlightingDefinitions.FirstOrDefault(x => x.Name == type);
            highlightingComboBox.SelectedItem = xmlHightlight;
        }

        public void ShowLineNumbers(bool value)
        {
            textEditor.ShowLineNumbers = value;
        }

        public void HightLightText(int lineNumber, int offset, int length)
        {
            var line = textEditor.Document.GetLineByNumber(lineNumber);
            textEditor.Select(line.Offset + offset, length);
            textEditor.ScrollTo(lineNumber, 0);
        }

        private void TextEditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is ITextEditorViewModel typedViewModel)
            {
                typedViewModel.SetEditor(this);
            }
        }

        void HighlightingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            textEditor.SyntaxHighlighting = highlightingComboBox.SelectedValue as IHighlightingDefinition;
            UseThemeAwareSyntaxColorizer(textEditor.SyntaxHighlighting);
            if (textEditor.SyntaxHighlighting == null)
            {
                _foldingStrategy = null;
            }
            else
            {
                switch (textEditor.SyntaxHighlighting.Name)
                {
                    case "XML":
                        _foldingStrategy = new XmlFoldingStrategy();
                        textEditor.TextArea.IndentationStrategy = new ICSharpCode.AvalonEdit.Indentation.DefaultIndentationStrategy();
                        break;
                    case "C#":
                    case "C++":
                    case "PHP":
                    case "Java":
                        textEditor.TextArea.IndentationStrategy = new ICSharpCode.AvalonEdit.Indentation.CSharp.CSharpIndentationStrategy(textEditor.Options);
                        _foldingStrategy = new BraceFoldingStrategy();
                        break;
                    default:
                        textEditor.TextArea.IndentationStrategy = new ICSharpCode.AvalonEdit.Indentation.DefaultIndentationStrategy();
                        _foldingStrategy = null;
                        break;
                }
            }
            if (_foldingStrategy != null)
            {
                if (_foldingManager == null)
                    _foldingManager = FoldingManager.Install(textEditor.TextArea);
                UpdateFoldings();
            }
            else
            {
                if (_foldingManager != null)
                {
                    FoldingManager.Uninstall(_foldingManager);
                    _foldingManager = null;
                }
            }
        }

        private void UseThemeAwareSyntaxColorizer(
            IHighlightingDefinition? definition)
        {
            var transformers = textEditor.TextArea.TextView.LineTransformers;
            foreach (var colorizer in transformers
                         .OfType<HighlightingColorizer>()
                         .ToArray())
            {
                transformers.Remove(colorizer);
            }

            _themeHighlightingColorizer = definition == null
                ? null
                : new ThemeAwareHighlightingColorizer(definition);
            if (_themeHighlightingColorizer != null)
                transformers.Add(_themeHighlightingColorizer);
        }

        void UpdateFoldings()
        {
            if (_foldingStrategy is BraceFoldingStrategy)
            {
                ((BraceFoldingStrategy)_foldingStrategy).UpdateFoldings(_foldingManager, textEditor.Document);
            }
            if (_foldingStrategy is XmlFoldingStrategy)
            {
                ((XmlFoldingStrategy)_foldingStrategy).UpdateFoldings(_foldingManager, textEditor.Document);
            }
        }

    }
}
