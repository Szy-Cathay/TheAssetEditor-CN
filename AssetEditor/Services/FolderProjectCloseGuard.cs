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
    Task<bool> CanCloseAsync(
        FolderProjectContainer? project,
        Action<FolderProjectCloseProgress>? reportProgress = null,
        Func<Task>? completeProgressBeforePrompt = null);
}

public enum FolderProjectCloseProgressStage
{
    Preparing,
    ReadingRepositoryStatus,
    SummarizingChanges,
}

public sealed record FolderProjectCloseProgress(
    FolderProjectCloseProgressStage Stage,
    int CurrentStep,
    int TotalSteps,
    int? ChangeCount = null);

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
        FolderProjectContainer? project,
        Action<FolderProjectCloseProgress>? reportProgress = null,
        Func<Task>? completeProgressBeforePrompt = null)
    {
        reportProgress?.Invoke(
            new FolderProjectCloseProgress(
                FolderProjectCloseProgressStage.Preparing,
                1,
                3));
        if (project == null ||
            !Directory.Exists(Path.Combine(project.ProjectRoot, ".git")))
        {
            return true;
        }

        reportProgress?.Invoke(
            new FolderProjectCloseProgress(
                FolderProjectCloseProgressStage.ReadingRepositoryStatus,
                2,
                3));
        FolderProjectRepositoryStatus status;
        try
        {
            status = await Task.Run(
                () => _versionControlService.GetStatus(
                    project.ProjectRoot));
        }
        catch
        {
            await CompleteProgressBeforePromptAsync(
                completeProgressBeforePrompt);
            _showMessage(
                LocalizationManager.Instance.Get(
                    "FolderProject.Close.StatusCheckFailed"),
                LocalizationManager.Instance.Get(
                    "FolderProject.Close.Title"),
                MessageBoxButton.OK);
            return false;
        }

        reportProgress?.Invoke(
            new FolderProjectCloseProgress(
                FolderProjectCloseProgressStage.SummarizingChanges,
                3,
                3,
                status.Changes.Count));

        if (status.IsClean)
            return true;

        await CompleteProgressBeforePromptAsync(
            completeProgressBeforePrompt);
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

    private static Task CompleteProgressBeforePromptAsync(
        Func<Task>? completeProgressBeforePrompt) =>
        completeProgressBeforePrompt?.Invoke() ?? Task.CompletedTask;
}
