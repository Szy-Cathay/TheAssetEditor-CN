using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Editors.VfxEditor;

public partial class VfxEditorView : UserControl
{
    public VfxEditorView() => InitializeComponent();

    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (TryExecuteShortcut(e.Key, Keyboard.Modifiers)) e.Handled = true;
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

    private void SliderMouseUp(object sender, MouseButtonEventArgs e) => CommitSlider(sender);
    private void SliderKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            CommitSlider(sender);
    }
    private static void CommitSlider(object sender)
    {
        if (sender is Slider { DataContext: VfxValueViewModel value } slider)
            value.SetNumber(slider.Value);
    }
}
