namespace Shared.Core.PackFiles.Models;

public enum FolderProjectVersionControlError
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

public sealed class FolderProjectVersionControlException :
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

public sealed record FolderProjectGitIdentity(
    string Name,
    string Email);

public enum FolderProjectRepositoryOperationState
{
    None,
    Merge,
    Other,
}

[Flags]
public enum FolderProjectWorkingChangeKind
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

public sealed record FolderProjectWorkingChange(
    string RepositoryPath,
    FolderProjectWorkingChangeKind Kind,
    string? PreviousRepositoryPath = null);

public enum FolderProjectVersionControlProgressStage
{
    PreparingRepository,
    ScanningWorkingTree,
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

public sealed record FolderProjectVersionControlProgress(
    FolderProjectVersionControlProgressStage Stage,
    string? Detail = null,
    long Completed = 0,
    long Total = 0);

public enum FolderProjectCommitUndoMode
{
    KeepChanges,
    DiscardChanges,
}

public enum FolderProjectCommitChangeEditMode
{
    Discard,
    StageForEdit,
    KeepChanges,
}

public sealed record FolderProjectCommitSummary(
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

public enum FolderProjectCommitMergeStatus
{
    Unknown,
    NotMerged,
    Merged,
}

public enum FolderProjectCommitChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    TypeChanged,
}

public sealed record FolderProjectCommitChange(
    string RepositoryPath,
    string? PreviousRepositoryPath,
    FolderProjectCommitChangeKind Kind,
    bool IsBinary);

public sealed record FolderProjectCommitChangeSummary(
    int Added,
    int Modified,
    int Deleted,
    int Renamed,
    int TypeChanged);

public sealed record FolderProjectFileRestoreResult(
    string CommitId,
    string RepositoryPath,
    long Size);

public sealed record FolderProjectFileRestoreTransaction(
    FolderProjectFileRestoreResult Result,
    string StagingPath,
    string TargetPath,
    string? BackupPath);

public sealed record FolderProjectProjectRestoreResult(
    FolderProjectCommitSummary RestoreCommit,
    FolderProjectCommitSummary? SafetyCommit,
    FolderProjectProjectRestoreRollback Rollback);

public sealed record FolderProjectProjectRestoreRollback(
    string OriginalCommitId,
    string RestoreCommitId,
    string? SafetyCommitId,
    FolderProjectIndexSnapshot Index);

public sealed record FolderProjectDiscardRollback(
    string StagingPath,
    IReadOnlyList<FolderProjectDiscardBackup> Backups,
    IReadOnlyList<string> AffectedPaths,
    IReadOnlyList<string> CreatedDirectories,
    FolderProjectIndexSnapshot Index);

public sealed record FolderProjectIndexSnapshot(
    bool Existed,
    byte[] Bytes,
    FileAttributes Attributes);

public sealed record FolderProjectDiscardBackup(
    string OriginalPath,
    string StagedPath);

public sealed record FolderProjectCommitEditSession(
    string OriginalCommitId,
    string ExpectedHeadCommitId,
    IReadOnlyList<string> RepositoryPaths,
    bool CanReturnToOriginalCommit);

public sealed record FolderProjectBranchInfo(
    string Name,
    string TipCommitId,
    bool IsCurrent,
    bool IsPrimary = false);

public sealed record FolderProjectStashInfo(
    int Index,
    string Message,
    DateTimeOffset StashedAt,
    IReadOnlyList<string> Paths);

public enum FolderProjectBranchSwitchMode
{
    CarryChanges,
    StashChanges,
    DiscardChanges,
}

public enum FolderProjectMergePhase
{
    None,
    ReadyToCommit,
    Conflicts,
    RecoveryRequired,
}

public enum FolderProjectMergeOutcome
{
    UpToDate,
    FastForwarded,
    ReadyToCommit,
    Conflicts,
}

public enum FolderProjectMergeChoice
{
    Current,
    Incoming,
}

public enum FolderProjectGitFileMode
{
    NonExecutable,
    NonExecutableGroupWritable,
    Executable,
}

public sealed record FolderProjectMergeSide(
    string RepositoryPath,
    string BlobId,
    FolderProjectGitFileMode Mode,
    long Size,
    bool IsBinary);

public sealed record FolderProjectMergeConflict(
    string Id,
    FolderProjectMergeSide? Ancestor,
    FolderProjectMergeSide? Current,
    FolderProjectMergeSide? Incoming);

public sealed record FolderProjectMergeState(
    FolderProjectMergePhase Phase,
    string? CurrentBranch,
    string? SourceBranch,
    string? OriginalHeadCommitId,
    string? SourceHeadCommitId,
    string? SuggestedMessage,
    IReadOnlyList<FolderProjectMergeConflict> Conflicts,
    string? RecoveryReason);

public sealed record FolderProjectMergeStartResult(
    FolderProjectMergeOutcome Outcome,
    FolderProjectCommitSummary? Commit,
    FolderProjectMergeState State);

public sealed record FolderProjectRepositoryStatus(
    bool IsInitialized,
    string? CurrentBranch,
    string? HeadCommitId,
    bool IsDetached,
    FolderProjectRepositoryOperationState OperationState,
    IReadOnlyList<FolderProjectWorkingChange> Changes)
{
    public bool IsClean => Changes.Count == 0;
}
