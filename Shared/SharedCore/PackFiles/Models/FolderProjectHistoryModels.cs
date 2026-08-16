namespace Shared.Core.PackFiles.Models;

public enum FolderProjectHistoryAvailability
{
    NotInitialized,
    Ready,
    RecoveryRequired,
}

public enum FolderProjectHistoryRecoveryReason
{
    None,
    DetachedHistory,
    UnfinishedOperation,
    RepositoryBusy,
    UnreadableFiles,
}

[Flags]
public enum FolderProjectUnrecordedChangeKind
{
    None = 0,
    Added = 1 << 0,
    Modified = 1 << 1,
    Deleted = 1 << 2,
    Renamed = 1 << 3,
    TypeChanged = 1 << 4,
    Conflicted = 1 << 5,
    Unreadable = 1 << 6,
}

public sealed record FolderProjectUnrecordedChange(
    string Path,
    FolderProjectUnrecordedChangeKind Kind,
    string? PreviousPath = null);

public sealed record FolderProjectHistoryStatus(
    FolderProjectHistoryAvailability Availability,
    string? CurrentRestorePointId,
    IReadOnlyList<FolderProjectUnrecordedChange> UnrecordedChanges)
{
    public bool IsClean => UnrecordedChanges.Count == 0;
    public FolderProjectHistoryRecoveryReason RecoveryReason { get; init; }
    public bool CanRecover { get; init; }
}

public enum FolderProjectRestorePointChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    TypeChanged,
}

public sealed record FolderProjectRestorePointChange(
    string Path,
    string? PreviousPath,
    FolderProjectRestorePointChangeKind Kind,
    bool IsBinary);

public sealed record FolderProjectRestorePointChangeSummary(
    int Added,
    int Modified,
    int Deleted,
    int Renamed,
    int TypeChanged)
{
    public int Total => Added + Modified + Deleted + Renamed + TypeChanged;
}

public sealed record FolderProjectRestorePoint(
    string Id,
    string Description,
    DateTimeOffset CreatedAt,
    FolderProjectRestorePointChangeSummary ChangeSummary,
    bool IsInitial)
{
    public string? PreviousRestorePointId { get; init; }
}

public enum FolderProjectHistoryProgressStage
{
    PreparingHistory,
    ScanningUnrecordedChanges,
    ProcessingUnrecordedChanges,
    CreatingInitialRestorePoint,
    CreatingRestorePoint,
    ReadingHistory,
    ReadingRestorePointChanges,
    PreparingEditors,
    UpdatingHistory,
    WritingProjectFiles,
    ReconcilingProject,
    RefreshingInterface,
    RecoveringHistory,
}

public sealed record FolderProjectHistoryProgress(
    FolderProjectHistoryProgressStage Stage,
    string? Detail = null,
    long Completed = 0,
    long Total = 0);

public sealed class FolderProjectRestoreResult
{
    public FolderProjectRestorePoint RestorePoint { get; }
    public FolderProjectRestorePoint? SafetyRestorePoint { get; }
    internal FolderProjectProjectRestoreRollback? Rollback { get; init; }

    public FolderProjectRestoreResult(
        FolderProjectRestorePoint restorePoint,
        FolderProjectRestorePoint? safetyRestorePoint)
    {
        RestorePoint = restorePoint;
        SafetyRestorePoint = safetyRestorePoint;
    }
}

public sealed class FolderProjectDiscardResult
{
    internal FolderProjectDiscardRollback Rollback { get; }

    internal FolderProjectDiscardResult(FolderProjectDiscardRollback rollback)
    {
        Rollback = rollback;
    }
}

public sealed class FolderProjectRestorePointDeleteOperation
{
    internal FolderProjectRestorePointDeleteRollback Rollback { get; }

    internal FolderProjectRestorePointDeleteOperation(
        FolderProjectRestorePointDeleteRollback rollback)
    {
        Rollback = rollback;
    }
}

public sealed class FolderProjectRecoveryOperation
{
    public FolderProjectHistoryStatus Status { get; }
    internal FolderProjectRecoveryTransaction Transaction { get; }

    internal FolderProjectRecoveryOperation(
        FolderProjectHistoryStatus status,
        FolderProjectRecoveryTransaction transaction)
    {
        Status = status;
        Transaction = transaction;
    }
}

public sealed class FolderProjectFileRestoreOperation
{
    internal FolderProjectFileRestoreResult Result { get; }
    internal FolderProjectFileRestoreTransaction Transaction { get; }

    internal FolderProjectFileRestoreOperation(
        FolderProjectFileRestoreResult result,
        FolderProjectFileRestoreTransaction transaction)
    {
        Result = result;
        Transaction = transaction;
    }
}

public enum FolderProjectHistoryError
{
    NotInitialized,
    UnsupportedProject,
    RecoveryRequired,
    NoUnrecordedChanges,
    RestorePointNotFound,
    RestorePointCannotBeDeleted,
    StorageFailure,
}

public sealed class FolderProjectHistoryException : InvalidOperationException
{
    public FolderProjectHistoryError Code { get; }
    public bool IsRollbackIncomplete { get; }

    public FolderProjectHistoryException(
        FolderProjectHistoryError code,
        string message,
        Exception? innerException = null,
        bool isRollbackIncomplete = false)
        : base(message, innerException)
    {
        Code = code;
        IsRollbackIncomplete = isRollbackIncomplete;
    }
}
