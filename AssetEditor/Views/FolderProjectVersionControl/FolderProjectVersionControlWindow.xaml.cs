using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using AssetEditor.ViewModels;
using Shared.Core.PackFiles.Models;
using WindowHandling;

namespace AssetEditor.Views.FolderProjectVersionControl;

public partial class FolderProjectVersionControlWindow : AssetEditorWindow
{
    public FolderProjectVersionControlWindow()
    {
        InitializeComponent();
    }

    private void MergeConflicts_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid &&
            DataContext is FolderProjectVersionControlViewModel viewModel)
        {
            viewModel.SelectedMergeConflicts = grid.SelectedItems
                .Cast<FolderProjectMergeConflictRow>()
                .ToList();
        }
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
