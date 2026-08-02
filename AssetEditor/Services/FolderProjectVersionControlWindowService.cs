using AssetEditor.ViewModels;
using AssetEditor.Views.FolderProjectVersionControl;
using Microsoft.Extensions.DependencyInjection;

using System.Windows;

namespace AssetEditor.Services;

public interface IFolderProjectVersionControlWindowService
{
    void ShowDialog(
        string projectRoot,
        string projectName,
        bool openWhenComplete);

    void ShowMergeDialog(
        string projectRoot,
        string projectName,
        string sourceBranch);
}

public sealed class FolderProjectVersionControlWindowService(
    IServiceScopeFactory scopeFactory) :
    IFolderProjectVersionControlWindowService
{
    public void ShowDialog(
        string projectRoot,
        string projectName,
        bool openWhenComplete)
    {
        ShowDialogCore(
            projectRoot,
            projectName,
            openWhenComplete,
            null);
    }

    public void ShowMergeDialog(
        string projectRoot,
        string projectName,
        string sourceBranch)
    {
        ShowDialogCore(
            projectRoot,
            projectName,
            false,
            sourceBranch);
    }

    private void ShowDialogCore(
        string projectRoot,
        string projectName,
        bool openWhenComplete,
        string? sourceBranch)
    {
        using var scope = scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<
            FolderProjectVersionControlWindow>();
        var viewModel = scope.ServiceProvider.GetRequiredService<
            FolderProjectVersionControlViewModel>();
        if (sourceBranch == null)
        {
            viewModel.OpenProject(
                projectRoot,
                projectName,
                openWhenComplete);
        }
        else
        {
            viewModel.OpenMergeProject(
                projectRoot,
                projectName,
                sourceBranch);
        }
        window.DataContext = viewModel;
        SetOwner(window, Application.Current?.MainWindow);
        window.ShowDialog();
    }

    internal static void SetOwner(Window window, Window? owner)
    {
        if (owner != null && !ReferenceEquals(window, owner))
            window.Owner = owner;
    }
}
