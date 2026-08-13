using AssetEditor.ViewModels;
using AssetEditor.Views.FolderProjectHistory;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace AssetEditor.Services;

public interface IFolderProjectHistoryWindowService
{
    void ShowRecoveryDialog(string projectRoot, string projectName);
}

public sealed class FolderProjectHistoryWindowService(
    IServiceScopeFactory scopeFactory) : IFolderProjectHistoryWindowService
{
    public void ShowRecoveryDialog(
        string projectRoot,
        string projectName)
    {
        using var scope = scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<
            FolderProjectHistoryWindow>();
        var viewModel = scope.ServiceProvider.GetRequiredService<
            FolderProjectHistoryViewModel>();
        viewModel.OpenRecoveryProject(projectRoot, projectName);
        viewModel.RecoveryCompleted += OnRecoveryCompleted;
        window.DataContext = viewModel;
        viewModel.RefreshCommand.Execute(null);
        FolderProjectVersionControlWindowService.SetOwner(
            window,
            Application.Current?.MainWindow);
        try
        {
            window.ShowDialog();
        }
        finally
        {
            viewModel.RecoveryCompleted -= OnRecoveryCompleted;
        }

        void OnRecoveryCompleted(object? sender, EventArgs e) =>
            window.Close();
    }
}
