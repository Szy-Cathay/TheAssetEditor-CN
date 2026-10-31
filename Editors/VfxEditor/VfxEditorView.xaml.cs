using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Editors.VfxEditor;

public partial class VfxEditorView : UserControl
{
    private Button? _identificationButton;
    private VfxEditorViewModel? _identificationEditor;
    public VfxEditorView() => InitializeComponent();

    private void CopyDiagnosticsCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = sender is ListBox { SelectedItems.Count: > 0 };
        e.Handled = true;
    }

    private void CopyDiagnosticsExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is ListBox list && SelectedDiagnosticText(list) is { Length: > 0 } text) Clipboard.SetText(text);
        e.Handled = true;
    }

    private static string SelectedDiagnosticText(ListBox list) => string.Join(Environment.NewLine, list.SelectedItems.Cast<string>());

    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _identificationEditor != null) { CancelIdentification(); e.Handled = true; return; }
        if (TryExecuteShortcut(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    private void IdentifyMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: VfxLayerViewModel layer } button || DataContext is not VfxEditorViewModel editor) return;
        CancelIdentification();
        if (!button.CaptureMouse()) return;
        _identificationButton = button;
        _identificationEditor = editor;
        editor.BeginIdentification(layer);
        e.Handled = true;
    }

    private void IdentifyMouseUp(object sender, MouseButtonEventArgs e) { CancelIdentification(); e.Handled = true; }
    private void IdentifyMouseLeave(object sender, MouseEventArgs e) => CancelIdentification();
    private void IdentifyLostMouseCapture(object sender, MouseEventArgs e) => CancelIdentification();
    private void IdentifyLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CancelIdentification();
    private void EditorUnloaded(object sender, RoutedEventArgs e) => CancelIdentification();
    private void EditorDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => CancelIdentification();

    private void IdentifyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter) || Keyboard.Modifiers != ModifierKeys.None) return;
        if (!e.IsRepeat && sender is Button { DataContext: VfxLayerViewModel layer } button && DataContext is VfxEditorViewModel editor)
        {
            CancelIdentification();
            _identificationButton = button;
            _identificationEditor = editor;
            editor.BeginIdentification(layer);
        }
        e.Handled = true;
    }

    private void IdentifyKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return;
        CancelIdentification();
        e.Handled = true;
    }

    private void CancelIdentification()
    {
        var button = _identificationButton;
        var editor = _identificationEditor;
        _identificationButton = null;
        _identificationEditor = null;
        editor?.EndIdentification();
        if (button?.IsMouseCaptured == true) button.ReleaseMouseCapture();
    }

    private bool TryExecuteShortcut(Key key, ModifierKeys modifiers)
    {
        if (DataContext is not VfxEditorViewModel editor) return false;
        var command = modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Z ? editor.RedoCommand
            : modifiers == ModifierKeys.Control ? key switch
            { Key.S => editor.SaveCommand, Key.Z => editor.UndoCommand, Key.Y => editor.RedoCommand, _ => null } : null;
        if (command == null) return false;
        if (command.CanExecute(null)) command.Execute(null);
        return true;
    }

}
