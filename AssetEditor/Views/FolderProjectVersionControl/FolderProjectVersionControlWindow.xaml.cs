using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using AssetEditor.ViewModels;
using WindowHandling;

namespace AssetEditor.Views.FolderProjectVersionControl;

public partial class FolderProjectVersionControlWindow : AssetEditorWindow
{
    public FolderProjectVersionControlWindow()
    {
        InitializeComponent();
    }

    private void DataGridRow_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow row || row.IsSelected)
            return;

        if (ItemsControl.ItemsControlFromItemContainer(row) is DataGrid grid)
            grid.SelectedItems.Clear();
        row.IsSelected = true;
        row.Focus();
    }

    private void UnstagedChanges_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid &&
            DataContext is FolderProjectVersionControlViewModel viewModel)
        {
            viewModel.SelectedUnstagedChanges = grid.SelectedItems
                .Cast<FolderProjectWorkingChangeRow>()
                .ToList();
        }
    }

    private void StagedChanges_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid &&
            DataContext is FolderProjectVersionControlViewModel viewModel)
        {
            viewModel.SelectedStagedChanges = grid.SelectedItems
                .Cast<FolderProjectWorkingChangeRow>()
                .ToList();
        }
    }

    private void CommitChanges_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid &&
            DataContext is FolderProjectVersionControlViewModel viewModel)
        {
            viewModel.SelectedCommitChanges = grid.SelectedItems
                .Cast<FolderProjectCommitChangeRow>()
                .ToList();
        }
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
