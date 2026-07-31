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
    InvalidCommitId,
    InvalidResourcePath,
    CommitPathNotFound,
    UnsupportedCommitPath,
    WorkingTreeNotClean,
    InvalidBranchName,
    BranchNotFound,
    BranchAlreadyExists,
    CurrentBranchProtected,
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

    public FolderProjectVersionControlException(
        FolderProjectVersionControlError code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
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
    FolderProjectWorkingChangeKind Kind);

public sealed record FolderProjectCommitSummary(
    string Id,
    string Message,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset CommittedAt,
    IReadOnlyList<string> ParentIds)
{
    public string ShortId => Id[..Math.Min(7, Id.Length)];
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

public sealed record FolderProjectFileRestoreResult(
    string CommitId,
    string RepositoryPath,
    long Size);

public sealed record FolderProjectBranchInfo(
    string Name,
    string TipCommitId,
    bool IsCurrent);

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
