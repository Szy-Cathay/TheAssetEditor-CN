namespace Shared.Core.PackFiles.Models;

internal enum FolderProjectVersionControlError
{
    RepositoryNotInitialized,
    UnsupportedRepository,
    RepositoryBusy,
    UnsupportedOperationState,
    IdentityMissing,
    InvalidIdentity,
    EmptyCommitMessage,
    NothingToCommit,
    CommitNotFound,
    CommitCannotBeDeleted,
    CommitCannotBeUndone,
    CommitIsNotLatest,
    InvalidCommitId,
    InvalidResourcePath,
    CommitPathNotFound,
    UnsupportedCommitPath,
    WorkingTreeNotClean,
    InvalidBranchName,
    BranchNotFound,
    BranchAlreadyExists,
    CurrentBranchProtected,
    PrimaryBranchProtected,
    BranchNotMerged,
    MergeAlreadyActive,
    MergeNotActive,
    MergeSourceIsCurrent,
    MergeConflictNotFound,
    UnresolvedMergeConflicts,
    UnrelatedHistories,
    MergeRecoveryRequired,
    RepositoryFailure,
}

internal sealed class FolderProjectVersionControlException :
    InvalidOperationException
{
    public FolderProjectVersionControlError Code { get; }
    public bool IsRollbackIncomplete { get; }

    public FolderProjectVersionControlException(
        FolderProjectVersionControlError code,
        string message,
        Exception? innerException = null,
        bool isRollbackIncomplete = false)
        : base(message, innerException)
    {
        Code = code;
        IsRollbackIncomplete = isRollbackIncomplete;
    }
}

internal sealed record FolderProjectGitIdentity(
    string Name,
    string Email);

internal enum FolderProjectRepositoryOperationState
{
    None,
    Merge,
    Other,
}

[Flags]
internal enum FolderProjectWorkingChangeKind
{
    None = 0,
    Added = 1 << 0,
    Modified = 1 << 1,
    Deleted = 1 << 2,
    Renamed = 1 << 3,
    TypeChanged = 1 << 4,
    Conflicted = 1 << 5,
    Untracked = 1 << 6,
    Staged = 1 << 7,
    Unstaged = 1 << 8,
    Unreadable = 1 << 9,
}

internal sealed record FolderProjectWorkingChange(
    string RepositoryPath,
    FolderProjectWorkingChangeKind Kind,
    string? PreviousRepositoryPath = null);

internal enum FolderProjectVersionControlProgressStage
{
    PreparingRepository,
    ScanningWorkingTree,
    CatalogingWorkingChanges,
    ProcessingWorkingChanges,
    IndexingFiles,
    CreatingInitialCommit,
    ReadingIdentity,
    ReadingBranches,
    ReadingStashes,
    ReadingHistory,
    ReadingCommitChanges,
    ProcessingCommitChanges,
    ReadingMergeState,
    PreparingMerge,
    MergingFiles,
    VerifyingMerge,
}

internal sealed record FolderProjectVersionControlProgress(
    FolderProjectVersionControlProgressStage Stage,
    string? Detail = null,
    long Completed = 0,
    long Total = 0);

internal enum FolderProjectCommitUndoMode
{
    KeepChanges,
    DiscardChanges,
}

internal enum FolderProjectCommitChangeEditMode
{
    Discard,
    StageForEdit,
    KeepChanges,
}

internal sealed record FolderProjectCommitSummary(
    string Id,
    string Message,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset CommittedAt,
    IReadOnlyList<string> ParentIds)
{
    public string ShortId => Id[..Math.Min(7, Id.Length)];
    public string Title => Message;
    public string Description { get; init; } = "";
    public FolderProjectCommitMergeStatus MergeStatus { get; init; }
}

internal enum FolderProjectCommitMergeStatus
{
    Unknown,
    NotMerged,
    Merged,
}

internal enum FolderProjectCommitChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    TypeChanged,
}

internal sealed record FolderProjectCommitChange(
    string RepositoryPath,
    string? PreviousRepositoryPath,
    FolderProjectCommitChangeKind Kind,
    bool IsBinary);

internal sealed record FolderProjectCommitChangeSummary(
    int Added,
    int Modified,
    int Deleted,
    int Renamed,
    int TypeChanged);

internal sealed record FolderProjectFileRestoreResult(
    string CommitId,
    string RepositoryPath,
    long Size);

internal sealed record FolderProjectFileRestoreTransaction(
    FolderProjectFileRestoreResult Result,
    string StagingPath,
    string TargetPath,
    string? BackupPath);

internal sealed record FolderProjectProjectRestoreResult(
    FolderProjectCommitSummary RestoreCommit,
    FolderProjectCommitSummary? SafetyCommit,
    FolderProjectProjectRestoreRollback Rollback);

internal sealed record FolderProjectProjectRestoreRollback(
    string OriginalCommitId,
    string RestoreCommitId,
    string? SafetyCommitId,
    FolderProjectIndexSnapshot Index);

internal sealed record FolderProjectRestorePointDeleteRollback(
    string OriginalCommitId,
    string RewrittenCommitId,
    FolderProjectIndexSnapshot? Index);

internal sealed record FolderProjectDiscardRollback(
    string StagingPath,
    IReadOnlyList<FolderProjectDiscardBackup> Backups,
    IReadOnlyList<string> AffectedPaths,
    IReadOnlyList<string> CreatedDirectories,
    FolderProjectIndexSnapshot Index);

internal sealed record FolderProjectIndexSnapshot(
    bool Existed,
    byte[] Bytes,
    FileAttributes Attributes);

internal sealed class FolderProjectRecoveryTransaction
{
    private Action? _rollback;

    public FolderProjectRepositoryStatus Status { get; }
    internal string ProjectRoot { get; }

    internal FolderProjectRecoveryTransaction(
        string projectRoot,
        FolderProjectRepositoryStatus status,
        Action rollback)
    {
        ProjectRoot = projectRoot;
        Status = status;
        _rollback = rollback;
    }

    internal void Complete() =>
        Interlocked.Exchange(ref _rollback, null);

    internal void Rollback()
    {
        var rollback = Interlocked.Exchange(ref _rollback, null) ??
                       throw new InvalidOperationException(
                           "The recovery transaction is already closed.");
        rollback();
    }
}

internal sealed record FolderProjectDiscardBackup(
    string OriginalPath,
    string StagedPath);

internal sealed record FolderProjectCommitEditSession(
    string OriginalCommitId,
    string ExpectedHeadCommitId,
    IReadOnlyList<string> RepositoryPaths,
    bool CanReturnToOriginalCommit);

internal sealed record FolderProjectBranchInfo(
    string Name,
    string TipCommitId,
    bool IsCurrent,
    bool IsPrimary = false);

internal sealed record FolderProjectStashInfo(
    int Index,
    string Message,
    DateTimeOffset StashedAt,
    IReadOnlyList<string> Paths);

internal enum FolderProjectBranchSwitchMode
{
    CarryChanges,
    StashChanges,
    DiscardChanges,
}

internal enum FolderProjectMergePhase
{
    None,
    ReadyToCommit,
    Conflicts,
    RecoveryRequired,
}

internal enum FolderProjectMergeOutcome
{
    UpToDate,
    FastForwarded,
    ReadyToCommit,
    Conflicts,
}

internal enum FolderProjectMergeChoice
{
    Current,
    Incoming,
}

internal enum FolderProjectGitFileMode
{
    NonExecutable,
    NonExecutableGroupWritable,
    Executable,
}

internal sealed record FolderProjectMergeSide(
    string RepositoryPath,
    string BlobId,
    FolderProjectGitFileMode Mode,
    long Size,
    bool IsBinary);

internal sealed record FolderProjectMergeConflict(
    string Id,
    FolderProjectMergeSide? Ancestor,
    FolderProjectMergeSide? Current,
    FolderProjectMergeSide? Incoming);

internal sealed record FolderProjectMergeState(
    FolderProjectMergePhase Phase,
    string? CurrentBranch,
    string? SourceBranch,
    string? OriginalHeadCommitId,
    string? SourceHeadCommitId,
    string? SuggestedMessage,
    IReadOnlyList<FolderProjectMergeConflict> Conflicts,
    string? RecoveryReason);

internal sealed record FolderProjectMergeStartResult(
    FolderProjectMergeOutcome Outcome,
    FolderProjectCommitSummary? Commit,
    FolderProjectMergeState State);

internal sealed record FolderProjectRepositoryStatus(
    bool IsInitialized,
    string? CurrentBranch,
    string? HeadCommitId,
    bool IsDetached,
    FolderProjectRepositoryOperationState OperationState,
    IReadOnlyList<FolderProjectWorkingChange> Changes)
{
    public bool IsClean => Changes.Count == 0;
    public bool IsBusy { get; init; }
    public bool HasPendingEditorOperation { get; init; }
}
