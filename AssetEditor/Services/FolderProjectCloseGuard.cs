using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using AssetEditor.Events;
using Shared.Core.Events;
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
    private readonly IEventHub _eventHub;
    private readonly Func<
        string,
        string,
        MessageBoxButton,
        MessageBoxResult> _showMessage;

    public FolderProjectCloseGuard(
        IFolderProjectVersionControlService versionControlService,
        IEventHub eventHub)
        : this(
            versionControlService,
            eventHub,
            MessageBox.Show)
    {
    }

    internal FolderProjectCloseGuard(
        IFolderProjectVersionControlService versionControlService,
        IEventHub eventHub,
        Func<
            string,
            string,
            MessageBoxButton,
            MessageBoxResult> showMessage)
    {
        _versionControlService = versionControlService;
        _eventHub = eventHub;
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

        _eventHub.Publish(new OpenFolderProjectGitPanelEvent());
        return false;
    }
}
