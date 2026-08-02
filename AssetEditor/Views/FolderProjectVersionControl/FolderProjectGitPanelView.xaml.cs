using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AssetEditor.Views.FolderProjectVersionControl;

public partial class FolderProjectGitPanelView : UserControl
{
    public FolderProjectGitPanelView()
    {
        InitializeComponent();
    }

    private void CommitOptionsButton_Click(
        object sender,
        System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
