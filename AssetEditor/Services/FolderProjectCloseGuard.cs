using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace AssetEditor.Services;

public interface IFolderProjectCloseGuard
{
    Task<bool> CanCloseAsync(FolderProjectContainer? project);
}

public sealed class FolderProjectCloseGuard :
    IFolderProjectCloseGuard
{
    private readonly IFolderProjectVersionControlService
        _versionControlService;
    private readonly IFolderProjectVersionControlWindowService
        _windowService;
    private readonly Func<
        string,
        string,
        MessageBoxButton,
        MessageBoxResult> _showMessage;

    public FolderProjectCloseGuard(
        IFolderProjectVersionControlService versionControlService,
        IFolderProjectVersionControlWindowService windowService)
        : this(
            versionControlService,
            windowService,
            MessageBox.Show)
    {
    }

    internal FolderProjectCloseGuard(
        IFolderProjectVersionControlService versionControlService,
        IFolderProjectVersionControlWindowService windowService,
        Func<
            string,
            string,
            MessageBoxButton,
            MessageBoxResult> showMessage)
    {
        _versionControlService = versionControlService;
        _windowService = windowService;
        _showMessage = showMessage;
    }

    public async Task<bool> CanCloseAsync(
        FolderProjectContainer? project)
    {
        if (project == null ||
            !Directory.Exists(Path.Combine(project.ProjectRoot, ".git")))
        {
            return true;
        }

        FolderProjectRepositoryStatus status;
        try
        {
            status = await Task.Run(
                () => _versionControlService.GetStatus(
                    project.ProjectRoot));
        }
        catch
        {
            _showMessage(
                LocalizationManager.Instance.Get(
                    "FolderProject.Close.StatusCheckFailed"),
                LocalizationManager.Instance.Get(
                    "FolderProject.Close.Title"),
                MessageBoxButton.OK);
            return false;
        }

        if (status.IsClean)
            return true;

        var result = _showMessage(
            LocalizationManager.Instance.GetFormat(
                "FolderProject.Close.UncommittedChanges",
                project.ProjectSettings.Name,
                status.Changes.Count),
            LocalizationManager.Instance.Get(
                "FolderProject.Close.Title"),
            MessageBoxButton.YesNoCancel);
        if (result == MessageBoxResult.Yes)
            return true;
        if (result != MessageBoxResult.No)
            return false;

        _windowService.ShowDialog(
            project.ProjectRoot,
            project.ProjectSettings.Name,
            false);
        return false;
    }
}
