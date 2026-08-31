using System.Windows.Controls;
using System.Windows.Input;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public partial class AnimationWorkbenchView : UserControl
{
    public AnimationWorkbenchView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ActivateSelectedPanel();
    }

    private void ToolTabs_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ToolTabs))
            return;
        ActivateSelectedPanel();
    }

    private void ActivateSelectedPanel()
    {
        if (DataContext is not AnimationWorkbenchViewModel viewModel ||
            ToolTabs.SelectedItem is not TabItem { Tag: string tag } ||
            !Enum.TryParse<AnimationWorkbenchPanelKind>(tag, out var panel))
        {
            return;
        }
        viewModel.ActivatePanel(panel);
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not AnimationWorkbenchViewModel viewModel ||
            e.Key != Key.S ||
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            !viewModel.CanSave)
        {
            return;
        }

        viewModel.Save();
        e.Handled = true;
    }
}
