namespace Shared.Core.PackFiles.Models;

public enum FolderProjectHistoryAvailability
{
    NotInitialized,
    Ready,
    RecoveryRequired,
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
    FolderProjectRestorePointChangeSummary? ChangeSummary,
    bool IsInitial);

public enum FolderProjectHistoryProgressStage
{
    PreparingHistory,
    ScanningUnrecordedChanges,
    ProcessingUnrecordedChanges,
    CreatingInitialRestorePoint,
    CreatingRestorePoint,
    ReadingHistory,
    ReadingRestorePointChanges,
}

public sealed record FolderProjectHistoryProgress(
    FolderProjectHistoryProgressStage Stage,
    string? Detail = null,
    long Completed = 0,
    long Total = 0);

public enum FolderProjectHistoryError
{
    NotInitialized,
    UnsupportedProject,
    RecoveryRequired,
    NoUnrecordedChanges,
    RestorePointNotFound,
    StorageFailure,
}

public sealed class FolderProjectHistoryException : InvalidOperationException
{
    public FolderProjectHistoryError Code { get; }

    public FolderProjectHistoryException(
        FolderProjectHistoryError code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}
