using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Shared.Core.PackFiles.Utility;

public interface IFolderProjectHistoryService
{
    FolderProjectHistoryStatus GetStatus(string projectRoot);

    FolderProjectHistoryStatus GetStatus(
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
}

public sealed class FolderProjectHistoryService : IFolderProjectHistoryService
{
    private static readonly FolderProjectGitIdentity s_localIdentity = new(
        "AssetEditor.CN 本地用户",
        "local@asseteditor.cn");
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
            var availability = !status.IsInitialized
                ? FolderProjectHistoryAvailability.NotInitialized
                : status.IsDetached ||
                  status.OperationState != FolderProjectRepositoryOperationState.None
                    ? FolderProjectHistoryAvailability.RecoveryRequired
                    : FolderProjectHistoryAvailability.Ready;
            return new FolderProjectHistoryStatus(
                availability,
                status.HeadCommitId,
                status.Changes.Select(MapUnrecordedChange).ToList());
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
                s_localIdentity,
                "master",
                progress => reportProgress(MapProgress(progress)));
            var changes = _versionControl.GetCommitChanges(
                projectRoot,
                summary.Id);
            return MapRestorePoint(summary, changes);
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
            var changes = _versionControl.GetCommitChanges(
                projectRoot,
                summary.Id);
            return MapRestorePoint(summary, changes);
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
            return history.Select(summary => MapRestorePoint(summary, null))
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

    private FolderProjectRestorePoint MapRestorePoint(
        FolderProjectCommitSummary summary,
        IReadOnlyList<FolderProjectCommitChange>? changes)
    {
        var initial = summary.ParentIds.Count == 0;
        return new FolderProjectRestorePoint(
            summary.Id,
            initial
                ? _localization.Get(
                    "FolderProject.History.InitialRestorePoint")
                : summary.Message,
            summary.CommittedAt,
            changes == null ? null : Summarize(changes),
            initial);
    }

    private static FolderProjectRestorePointChangeSummary Summarize(
        IReadOnlyList<FolderProjectCommitChange> changes) =>
        new(
            changes.Count(change =>
                change.Kind == FolderProjectCommitChangeKind.Added),
            changes.Count(change =>
                change.Kind == FolderProjectCommitChangeKind.Modified),
            changes.Count(change =>
                change.Kind == FolderProjectCommitChangeKind.Deleted),
            changes.Count(change =>
                change.Kind == FolderProjectCommitChangeKind.Renamed),
            changes.Count(change =>
                change.Kind == FolderProjectCommitChangeKind.TypeChanged));

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
                        .ProcessingUnrecordedChanges,
                FolderProjectVersionControlProgressStage.IndexingFiles =>
                    FolderProjectHistoryProgressStage
                        .ProcessingUnrecordedChanges,
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
                FolderProjectVersionControlError.MergeRecoveryRequired or
                FolderProjectVersionControlError.UnresolvedMergeConflicts =>
                    FolderProjectHistoryError.RecoveryRequired,
                _ => FolderProjectHistoryError.StorageFailure,
            },
            exception.Message,
            exception);
}
