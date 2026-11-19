using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shared.Core.PackFiles.Models;

namespace AssetEditor.Services;

public static class FolderProjectFileOperation
{
    public static async Task<T> ExecuteAsync<T>(IFolderProjectGitOperationCoordinator coordinator,
        IFolderProjectUnsavedChangesService unsavedChanges, FolderProjectContainer project,
        IReadOnlyCollection<string> paths, Func<T> operation, Action<T> complete, Action<T> rollback,
        Action? refreshed = null, Action<FolderProjectHistoryProgress>? progress = null,
        SynchronizationContext? synchronizationContext = null)
    {
        synchronizationContext ??= SynchronizationContext.Current;
        using var reloadEditors = unsavedChanges.PrepareFileReload(project.ProjectRoot, paths);
        try
        {
            return await coordinator.ExecuteInPlaceTransactionalAsync(project.ProjectRoot,
                () =>
                {
                    var result = operation();
                    progress?.Invoke(new(FolderProjectHistoryProgressStage.ReconcilingProject, project.ProjectRoot));
                    return result;
                }, result =>
                {
                    project.RefreshFromDiskAndNotify();
                    progress?.Invoke(new(FolderProjectHistoryProgressStage.RefreshingInterface, project.ProjectRoot));
                    refreshed?.Invoke();
                    ReloadEditors();
                    complete(result);
                }, rollback);
        }
        catch { ReloadEditors(); throw; }

        void ReloadEditors()
        {
            if (reloadEditors == null) return;
            if (synchronizationContext == null || ReferenceEquals(SynchronizationContext.Current, synchronizationContext)) reloadEditors.Reload();
            else synchronizationContext.Send(_ => reloadEditors.Reload(), null);
        }
    }
}
