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
        using var scope = scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<
            FolderProjectVersionControlWindow>();
        var viewModel = scope.ServiceProvider.GetRequiredService<
            FolderProjectVersionControlViewModel>();
        viewModel.OpenProject(
            projectRoot,
            projectName,
            openWhenComplete);
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
