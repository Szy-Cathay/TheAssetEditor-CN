using System.ComponentModel;
using AssetEditor.ViewModels;
using WindowHandling;

namespace AssetEditor.Views.FolderProjectVersionControl;

public partial class FolderProjectVersionControlWindow : AssetEditorWindow
{
    public FolderProjectVersionControlWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is FolderProjectVersionControlViewModel viewModel &&
            !viewModel.CanCloseWindow())
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(System.EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }
}
