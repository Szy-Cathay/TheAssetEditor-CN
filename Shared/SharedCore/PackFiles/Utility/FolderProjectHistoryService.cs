using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Shared.Core.PackFiles.Utility;

public interface IFolderProjectHistoryService
{
    FolderProjectHistoryStatus GetStatus(string projectRoot);

    FolderProjectHistoryStatus GetStatus(
        string projectRoot,
        Action<FolderProjectHistoryProgress> reportProgress);

    FolderProjectHistoryStatus RecoverToSafeState(
        string projectRoot,
        Action<FolderProjectHistoryProgress> reportProgress);

    FolderProjectRestorePoint Initialize(string projectRoot);

    FolderProjectRestorePoint Initialize(
        string projectRoot,
        Action<FolderProjectHistoryProgress> reportProgress);

    FolderProjectRestorePoint CreateRestorePoint(
        string projectRoot,
        string? description);

    FolderProjectRestorePoint CreateRestorePoint(
        string projectRoot,
        string? description,
        Action<FolderProjectHistoryProgress> reportProgress);

    IReadOnlyList<FolderProjectRestorePoint> GetRestorePoints(
        string projectRoot,
        int maxCount = 100);

    IReadOnlyList<FolderProjectRestorePoint> GetRestorePoints(
        string projectRoot,
        int maxCount,
        Action<FolderProjectHistoryProgress> reportProgress);

    IReadOnlyList<FolderProjectRestorePointChange> GetRestorePointChanges(
        string projectRoot,
        string restorePointId);

    IReadOnlyList<FolderProjectRestorePointChange> GetRestorePointChanges(
        string projectRoot,
        string restorePointId,
        Action<FolderProjectHistoryProgress> reportProgress);

    int GetRestoreImpactCount(
        string projectRoot,
        string restorePointId);

    FolderProjectRestoreResult RestoreProject(
        string projectRoot,
        FolderProjectRestorePoint restorePoint,
        Action<FolderProjectHistoryProgress> reportProgress);

    FolderProjectRestoreResult RestoreProject(
        string projectRoot,
        string restorePointId);

    void RollbackProjectRestore(
        string projectRoot,
        FolderProjectRestoreResult result);

    FolderProjectFileRestoreOperation BeginRestoreFile(
        string projectRoot,
        string restorePointId,
        string relativePath,
        bool overwriteUnrecordedChange = false);

    void CompleteRestoreFile(FolderProjectFileRestoreOperation operation);

    void RollbackRestoreFile(FolderProjectFileRestoreOperation operation);

    FolderProjectDiscardResult BeginDiscardChanges(
        string projectRoot,
        IReadOnlyList<string> relativePaths,
        Action<FolderProjectHistoryProgress> reportProgress);

    void CompleteDiscardChanges(FolderProjectDiscardResult result);

    void RollbackDiscardChanges(
        string projectRoot,
        FolderProjectDiscardResult result);
}

public sealed class FolderProjectHistoryService : IFolderProjectHistoryService
{
    private readonly IFolderProjectVersionControlService _versionControl;
    private readonly LocalizationManager _localization;

    public FolderProjectHistoryService(LocalizationManager localization)
        : this(new FolderProjectVersionControlService(), localization)
    {
    }

    public FolderProjectHistoryService(
        IFolderProjectVersionControlService versionControl,
        LocalizationManager localization)
    {
        _versionControl = versionControl;
        _localization = localization;
    }

    public FolderProjectHistoryStatus GetStatus(string projectRoot) =>
        GetStatus(projectRoot, _ => { });

    public FolderProjectHistoryStatus GetStatus(
        string projectRoot,
        Action<FolderProjectHistoryProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        try
        {
            var status = _versionControl.GetStatus(
                projectRoot,
                progress => reportProgress(MapProgress(progress)),
                scanUnreadableEntries: true);
            return MapStatus(status);
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public FolderProjectHistoryStatus RecoverToSafeState(
        string projectRoot,
        Action<FolderProjectHistoryProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        reportProgress(new FolderProjectHistoryProgress(
            FolderProjectHistoryProgressStage.RecoveringHistory));
        try
        {
            return MapStatus(
                _versionControl.RecoverToSafeState(projectRoot));
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public FolderProjectRestorePoint Initialize(string projectRoot) =>
        Initialize(projectRoot, _ => { });

    public FolderProjectRestorePoint Initialize(
        string projectRoot,
        Action<FolderProjectHistoryProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        try
        {
            var summary = _versionControl.Initialize(
                projectRoot,
                new FolderProjectGitIdentity(
                    _localization.Get(
                        "FolderProject.History.LocalAuthorName"),
                    "local@asseteditor.cn"),
                "master",
                progress => reportProgress(MapProgress(progress)));
            var changeSummary = _versionControl.GetCommitChangeSummary(
                projectRoot,
                summary.Id);
            return MapRestorePoint(summary, MapSummary(changeSummary));
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public FolderProjectRestorePoint CreateRestorePoint(
        string projectRoot,
        string? description) =>
        CreateRestorePoint(projectRoot, description, _ => { });

    public FolderProjectRestorePoint CreateRestorePoint(
        string projectRoot,
        string? description,
        Action<FolderProjectHistoryProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? _localization.Get(
                "FolderProject.History.DefaultDescription")
            : description.Trim();
        reportProgress(new FolderProjectHistoryProgress(
            FolderProjectHistoryProgressStage.CreatingRestorePoint));
        try
        {
            var summary = _versionControl.CommitAll(
                projectRoot,
                normalizedDescription);
            var changeSummary = _versionControl.GetCommitChangeSummary(
                projectRoot,
                summary.Id);
            return MapRestorePoint(summary, MapSummary(changeSummary));
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public IReadOnlyList<FolderProjectRestorePoint> GetRestorePoints(
        string projectRoot,
        int maxCount = 100) =>
        GetRestorePoints(projectRoot, maxCount, _ => { });

    public IReadOnlyList<FolderProjectRestorePoint> GetRestorePoints(
        string projectRoot,
        int maxCount,
        Action<FolderProjectHistoryProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        try
        {
            reportProgress(new FolderProjectHistoryProgress(
                FolderProjectHistoryProgressStage.ReadingHistory));
            var history = _versionControl.GetHistory(projectRoot, maxCount);
            return history.Select(summary => MapRestorePoint(
                    summary,
                    MapSummary(_versionControl.GetCommitChangeSummary(
                        projectRoot,
                        summary.Id))))
                .ToList();
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public IReadOnlyList<FolderProjectRestorePointChange>
        GetRestorePointChanges(
            string projectRoot,
            string restorePointId) =>
        GetRestorePointChanges(projectRoot, restorePointId, _ => { });

    public IReadOnlyList<FolderProjectRestorePointChange>
        GetRestorePointChanges(
            string projectRoot,
            string restorePointId,
            Action<FolderProjectHistoryProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        try
        {
            return _versionControl.GetCommitChanges(
                    projectRoot,
                    restorePointId,
                    progress => reportProgress(MapProgress(progress)))
                .Select(MapRestorePointChange)
                .ToList();
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public int GetRestoreImpactCount(
        string projectRoot,
        string restorePointId)
    {
        try
        {
            return _versionControl.GetRestoreImpactCount(
                projectRoot,
                restorePointId);
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public FolderProjectRestoreResult RestoreProject(
        string projectRoot,
        FolderProjectRestorePoint restorePoint,
        Action<FolderProjectHistoryProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(restorePoint);
        ArgumentNullException.ThrowIfNull(reportProgress);
        try
        {
            var result = _versionControl.RestoreProject(
                projectRoot,
                restorePoint.Id,
                _localization.GetFormat(
                    "FolderProject.History.Restore.SafetyDescription",
                    restorePoint.Description),
                _localization.GetFormat(
                    "FolderProject.History.Restore.Description",
                    restorePoint.Description),
                progress => reportProgress(MapProgress(progress)));
            return new FolderProjectRestoreResult(
                MapRestorePoint(
                    result.RestoreCommit,
                    MapSummary(_versionControl.GetCommitChangeSummary(
                        projectRoot,
                        result.RestoreCommit.Id))),
                result.SafetyCommit == null
                    ? null
                    : MapRestorePoint(
                        result.SafetyCommit,
                        MapSummary(_versionControl.GetCommitChangeSummary(
                            projectRoot,
                            result.SafetyCommit.Id))))
            {
                Rollback = result.Rollback,
            };
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public FolderProjectRestoreResult RestoreProject(
        string projectRoot,
        string restorePointId)
    {
        var restorePoint = GetRestorePoints(projectRoot)
            .FirstOrDefault(point => point.Id == restorePointId) ??
            throw new FolderProjectHistoryException(
                FolderProjectHistoryError.RestorePointNotFound,
                "The requested restore point does not exist.");
        return RestoreProject(projectRoot, restorePoint, _ => { });
    }

    public void RollbackProjectRestore(
        string projectRoot,
        FolderProjectRestoreResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Rollback == null)
        {
            throw new InvalidOperationException(
                "The restore result has no rollback receipt.");
        }

        try
        {
            _versionControl.RollbackProjectRestore(
                projectRoot,
                result.Rollback);
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public FolderProjectFileRestoreOperation BeginRestoreFile(
        string projectRoot,
        string restorePointId,
        string relativePath,
        bool overwriteUnrecordedChange = false)
    {
        try
        {
            var transaction = _versionControl.BeginRestoreFile(
                projectRoot,
                restorePointId,
                relativePath,
                overwriteUnrecordedChange);
            return new FolderProjectFileRestoreOperation(
                transaction.Result,
                transaction);
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public void CompleteRestoreFile(FolderProjectFileRestoreOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _versionControl.CompleteRestoreFile(operation.Transaction);
    }

    public void RollbackRestoreFile(FolderProjectFileRestoreOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            _versionControl.RollbackRestoreFile(operation.Transaction);
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public FolderProjectDiscardResult BeginDiscardChanges(
        string projectRoot,
        IReadOnlyList<string> relativePaths,
        Action<FolderProjectHistoryProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        try
        {
            var rollback = _versionControl.BeginDiscardChanges(
                projectRoot,
                relativePaths,
                progress => reportProgress(MapProgress(progress)));
            return new FolderProjectDiscardResult(rollback);
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    public void CompleteDiscardChanges(FolderProjectDiscardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _versionControl.CompleteDiscardChanges(result.Rollback);
    }

    public void RollbackDiscardChanges(
        string projectRoot,
        FolderProjectDiscardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        try
        {
            _versionControl.RollbackDiscardChanges(
                projectRoot,
                result.Rollback);
        }
        catch (FolderProjectVersionControlException exception)
        {
            throw MapException(exception);
        }
    }

    private static FolderProjectUnrecordedChange MapUnrecordedChange(
        FolderProjectWorkingChange change)
    {
        var kind = FolderProjectUnrecordedChangeKind.None;
        if (change.Kind.HasFlag(FolderProjectWorkingChangeKind.Added) ||
            change.Kind.HasFlag(FolderProjectWorkingChangeKind.Untracked))
        {
            kind |= FolderProjectUnrecordedChangeKind.Added;
        }
        if (change.Kind.HasFlag(FolderProjectWorkingChangeKind.Modified))
            kind |= FolderProjectUnrecordedChangeKind.Modified;
        if (change.Kind.HasFlag(FolderProjectWorkingChangeKind.Deleted))
            kind |= FolderProjectUnrecordedChangeKind.Deleted;
        if (change.Kind.HasFlag(FolderProjectWorkingChangeKind.Renamed))
            kind |= FolderProjectUnrecordedChangeKind.Renamed;
        if (change.Kind.HasFlag(FolderProjectWorkingChangeKind.TypeChanged))
            kind |= FolderProjectUnrecordedChangeKind.TypeChanged;
        if (change.Kind.HasFlag(FolderProjectWorkingChangeKind.Conflicted))
            kind |= FolderProjectUnrecordedChangeKind.Conflicted;
        if (change.Kind.HasFlag(FolderProjectWorkingChangeKind.Unreadable))
            kind |= FolderProjectUnrecordedChangeKind.Unreadable;

        return new FolderProjectUnrecordedChange(
            change.RepositoryPath,
            kind,
            change.PreviousRepositoryPath);
    }

    private static FolderProjectHistoryStatus MapStatus(
        FolderProjectRepositoryStatus status)
    {
        var hasConflicts = status.Changes.Any(change =>
            change.Kind.HasFlag(FolderProjectWorkingChangeKind.Conflicted));
        var hasUnreadableFiles = status.Changes.Any(change =>
            change.Kind.HasFlag(FolderProjectWorkingChangeKind.Unreadable));
        var recoveryReason = status.IsBusy
            ? FolderProjectHistoryRecoveryReason.RepositoryBusy
            : hasUnreadableFiles
                ? FolderProjectHistoryRecoveryReason.UnreadableFiles
                : status.OperationState !=
                      FolderProjectRepositoryOperationState.None ||
                  hasConflicts ||
                  status.HasPendingEditorOperation
                    ? FolderProjectHistoryRecoveryReason.UnfinishedOperation
                    : status.IsDetached
                        ? FolderProjectHistoryRecoveryReason.DetachedHistory
                        : FolderProjectHistoryRecoveryReason.None;
        var availability = !status.IsInitialized
            ? FolderProjectHistoryAvailability.NotInitialized
            : recoveryReason != FolderProjectHistoryRecoveryReason.None
                ? FolderProjectHistoryAvailability.RecoveryRequired
                : FolderProjectHistoryAvailability.Ready;
        return new FolderProjectHistoryStatus(
            availability,
            status.HeadCommitId,
            status.Changes.Select(MapUnrecordedChange).ToList())
        {
            RecoveryReason = recoveryReason,
            CanRecover = availability ==
                         FolderProjectHistoryAvailability.RecoveryRequired &&
                         !status.IsBusy &&
                         !hasUnreadableFiles &&
                         status.HeadCommitId != null,
        };
    }

    private FolderProjectRestorePoint MapRestorePoint(
        FolderProjectCommitSummary summary,
        FolderProjectRestorePointChangeSummary changeSummary)
    {
        var initial = summary.ParentIds.Count == 0;
        return new FolderProjectRestorePoint(
            summary.Id,
            initial
                ? _localization.Get(
                    "FolderProject.History.InitialRestorePoint")
                : summary.Message,
            summary.CommittedAt,
            changeSummary,
            initial)
        {
            PreviousRestorePointId = summary.ParentIds.FirstOrDefault(),
        };
    }

    private static FolderProjectRestorePointChangeSummary MapSummary(
        FolderProjectCommitChangeSummary summary) =>
        new(
            summary.Added,
            summary.Modified,
            summary.Deleted,
            summary.Renamed,
            summary.TypeChanged);

    private static FolderProjectRestorePointChange MapRestorePointChange(
        FolderProjectCommitChange change) =>
        new(
            change.RepositoryPath,
            change.PreviousRepositoryPath,
            change.Kind switch
            {
                FolderProjectCommitChangeKind.Added =>
                    FolderProjectRestorePointChangeKind.Added,
                FolderProjectCommitChangeKind.Modified =>
                    FolderProjectRestorePointChangeKind.Modified,
                FolderProjectCommitChangeKind.Deleted =>
                    FolderProjectRestorePointChangeKind.Deleted,
                FolderProjectCommitChangeKind.Renamed =>
                    FolderProjectRestorePointChangeKind.Renamed,
                FolderProjectCommitChangeKind.TypeChanged =>
                    FolderProjectRestorePointChangeKind.TypeChanged,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(change),
                    change.Kind,
                    "Unsupported restore-point change kind."),
            },
            change.IsBinary);

    private static FolderProjectHistoryProgress MapProgress(
        FolderProjectVersionControlProgress progress) =>
        new(
            progress.Stage switch
            {
                FolderProjectVersionControlProgressStage.PreparingRepository =>
                    FolderProjectHistoryProgressStage.PreparingHistory,
                FolderProjectVersionControlProgressStage.ScanningWorkingTree =>
                    FolderProjectHistoryProgressStage
                        .ScanningUnrecordedChanges,
                FolderProjectVersionControlProgressStage
                    .ProcessingWorkingChanges =>
                    FolderProjectHistoryProgressStage
                        .WritingProjectFiles,
                FolderProjectVersionControlProgressStage.IndexingFiles =>
                    FolderProjectHistoryProgressStage
                        .UpdatingHistory,
                FolderProjectVersionControlProgressStage
                    .CreatingInitialCommit =>
                    FolderProjectHistoryProgressStage
                        .CreatingInitialRestorePoint,
                FolderProjectVersionControlProgressStage.ReadingHistory =>
                    FolderProjectHistoryProgressStage.ReadingHistory,
                _ => FolderProjectHistoryProgressStage
                    .ReadingRestorePointChanges,
            },
            progress.Detail,
            progress.Completed,
            progress.Total);

    private static FolderProjectHistoryException MapException(
        FolderProjectVersionControlException exception) =>
        new(
            exception.Code switch
            {
                FolderProjectVersionControlError.RepositoryNotInitialized =>
                    FolderProjectHistoryError.NotInitialized,
                FolderProjectVersionControlError.UnsupportedRepository =>
                    FolderProjectHistoryError.UnsupportedProject,
                FolderProjectVersionControlError.NothingToCommit =>
                    FolderProjectHistoryError.NoUnrecordedChanges,
                FolderProjectVersionControlError.CommitNotFound or
                FolderProjectVersionControlError.InvalidCommitId =>
                    FolderProjectHistoryError.RestorePointNotFound,
                FolderProjectVersionControlError.RepositoryBusy or
                FolderProjectVersionControlError.UnsupportedOperationState or
                FolderProjectVersionControlError.WorkingTreeNotClean or
                FolderProjectVersionControlError.MergeRecoveryRequired or
                FolderProjectVersionControlError.UnresolvedMergeConflicts =>
                    FolderProjectHistoryError.RecoveryRequired,
                _ => FolderProjectHistoryError.StorageFailure,
            },
            exception.Message,
            exception,
            exception.IsRollbackIncomplete);
}
