using LibGit2Sharp;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.PackFiles.Utility;

public interface IFolderProjectVersionControlService
{
    FolderProjectRepositoryStatus GetStatus(string projectRoot);

    FolderProjectCommitSummary Initialize(
        string projectRoot,
        FolderProjectGitIdentity identity);

    FolderProjectGitIdentity GetIdentity(string projectRoot);

    void SetIdentity(
        string projectRoot,
        FolderProjectGitIdentity identity);

    FolderProjectCommitSummary CommitAll(
        string projectRoot,
        string message);

    IReadOnlyList<FolderProjectCommitSummary> GetHistory(
        string projectRoot,
        int maxCount = 100);

    IReadOnlyList<FolderProjectCommitSummary> GetHistory(
        string projectRoot,
        string localBranch,
        int maxCount = 100);

    IReadOnlyList<FolderProjectCommitChange> GetCommitChanges(
        string projectRoot,
        string commitId);

    FolderProjectFileRestoreResult RestoreFile(
        string projectRoot,
        string commitId,
        string relativePath,
        bool overwriteWorkingChange = false);

    IReadOnlyList<FolderProjectBranchInfo> GetBranches(
        string projectRoot);

    FolderProjectBranchInfo CreateRecoveryBranch(
        string projectRoot,
        string name,
        string commitId);

    FolderProjectBranchInfo CreateBranch(
        string projectRoot,
        string name,
        string? startCommitId = null);

    FolderProjectBranchInfo RenameBranch(
        string projectRoot,
        string oldName,
        string newName);

    void DeleteBranch(
        string projectRoot,
        string name);

    FolderProjectBranchInfo SwitchBranch(
        string projectRoot,
        string name);

    FolderProjectMergeState GetMergeState(string projectRoot);

    FolderProjectMergeStartResult BeginMerge(
        string projectRoot,
        string sourceLocalBranch);

    FolderProjectMergeState ResolveMergeConflict(
        string projectRoot,
        string conflictId,
        FolderProjectMergeChoice choice);

    FolderProjectCommitSummary CompleteMerge(
        string projectRoot,
        string message);

    void AbortMerge(string projectRoot);
}

internal class FolderProjectVersionControlPlatform
{
    public virtual void InitializeRepository(string projectRoot)
    {
        FolderProjectGitRepository.Initialize(projectRoot);
    }

    public virtual void SetLocalConfig(
        Repository repository,
        string key,
        string value)
    {
        repository.Config.Set(
            key,
            value,
            ConfigurationLevel.Local);
    }

    public virtual Commit Commit(
        Repository repository,
        string message,
        Signature signature)
    {
        return repository.Commit(
            message,
            signature,
            signature);
    }

    public virtual IReadOnlyList<string> GetFileSystemEntries(
        string path)
    {
        return Directory.GetFileSystemEntries(path);
    }

    public virtual FileAttributes GetAttributes(string path)
    {
        return File.GetAttributes(path);
    }

    public virtual Stream OpenReadForStatus(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
    }

    public virtual void MoveFile(
        string sourcePath,
        string destinationPath,
        bool overwrite)
    {
        File.Move(sourcePath, destinationPath, overwrite);
    }

    public virtual Branch CheckoutBranch(
        Repository repository,
        Branch branch,
        CheckoutOptions options)
    {
        return Commands.Checkout(repository, branch, options);
    }

    public virtual MergeResult Merge(
        Repository repository,
        Commit commit,
        Signature signature,
        MergeOptions options)
    {
        return repository.Merge(commit, signature, options);
    }

    public virtual void Reset(
        Repository repository,
        Commit commit,
        CheckoutOptions options)
    {
        repository.Reset(ResetMode.Hard, commit, options);
    }

    public virtual void DeleteFile(string path)
    {
        File.Delete(path);
    }
}

public sealed partial class FolderProjectVersionControlService :
    IFolderProjectVersionControlService
{
    private readonly FolderProjectVersionControlPlatform _platform;

    public FolderProjectVersionControlService()
        : this(new FolderProjectVersionControlPlatform())
    {
    }

    internal FolderProjectVersionControlService(
        FolderProjectVersionControlPlatform platform)
    {
        _platform = platform;
    }

    public FolderProjectRepositoryStatus GetStatus(string projectRoot)
    {
        var repositoryState = GetRepositoryState(projectRoot);
        if (repositoryState == RepositoryState.Unsupported)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedRepository,
                "The folder project contains unsupported Git metadata.");
        }
        if (repositoryState == RepositoryState.Uninitialized)
        {
            return new FolderProjectRepositoryStatus(
                false,
                null,
                null,
                false,
                FolderProjectRepositoryOperationState.None,
                []);
        }

        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                var head = repository.Head;
                var changes = GetWorkingChanges(
                    repository,
                    Path.GetFullPath(projectRoot));
                return new FolderProjectRepositoryStatus(
                    true,
                    repository.Info.IsHeadDetached
                        ? null
                        : head.FriendlyName,
                    head.Tip?.Sha,
                    repository.Info.IsHeadDetached,
                    GetOperationState(repository.Info.CurrentOperation),
                    changes);
            });
    }

    private IReadOnlyList<FolderProjectWorkingChange> GetWorkingChanges(
        Repository repository,
        string projectRoot)
    {
        var trackedPaths = TrackedRepositoryPaths.Create(
            repository.Index);
        var changes = new Dictionary<
            string,
            FolderProjectWorkingChangeKind>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in RetrieveWorkingStatus(repository))
        {
            if (entry.State == FileStatus.Ignored)
                continue;

            MergeWorkingChange(
                changes,
                entry.FilePath.Replace('\\', '/'),
                MapWorkingChange(entry.State));
        }

        ScanUnreadableEntries(
            repository,
            projectRoot,
            "",
            trackedPaths,
            changes);
        return changes
            .Where(change =>
                change.Value != FolderProjectWorkingChangeKind.None)
            .OrderBy(change => change.Key, StringComparer.Ordinal)
            .Select(change => new FolderProjectWorkingChange(
                change.Key,
                change.Value))
            .ToList();
    }

    private void ScanUnreadableEntries(
        Repository repository,
        string projectRoot,
        string relativeDirectory,
        TrackedRepositoryPaths trackedPaths,
        IDictionary<string, FolderProjectWorkingChangeKind> changes)
    {
        var directoryPath = relativeDirectory.Length == 0
            ? projectRoot
            : Path.Combine(
                projectRoot,
                relativeDirectory.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        IReadOnlyList<string> entries;
        try
        {
            entries = _platform.GetFileSystemEntries(directoryPath);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            AddUnreadableDirectory(changes, relativeDirectory);
            return;
        }
        catch (IOException)
        {
            AddUnreadableDirectory(changes, relativeDirectory);
            return;
        }

        foreach (var entryPath in entries)
        {
            var repositoryPath = Path.GetRelativePath(
                    projectRoot,
                    entryPath)
                .Replace('\\', '/');
            var isTracked = trackedPaths.ExactPaths.Contains(repositoryPath);
            var hasTrackedDescendant =
                trackedPaths.ParentDirectories.Contains(repositoryPath);
            if (FolderProjectPathPolicy.IsMetadataDirectoryPath(
                    repositoryPath) ||
                !isTracked &&
                !hasTrackedDescendant &&
                repository.Ignore.IsPathIgnored(repositoryPath))
            {
                continue;
            }

            FileAttributes attributes;
            try
            {
                attributes = _platform.GetAttributes(entryPath);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                MergeWorkingChange(
                    changes,
                    repositoryPath,
                    FolderProjectWorkingChangeKind.Unreadable);
                continue;
            }
            catch (IOException)
            {
                MergeWorkingChange(
                    changes,
                    repositoryPath,
                    FolderProjectWorkingChangeKind.Unreadable);
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!isTracked &&
                    !hasTrackedDescendant &&
                    repository.Ignore.IsPathIgnored(
                        repositoryPath + "/"))
                {
                    continue;
                }

                ScanUnreadableEntries(
                    repository,
                    projectRoot,
                    repositoryPath,
                    trackedPaths,
                    changes);
                continue;
            }

            try
            {
                using var stream = _platform.OpenReadForStatus(entryPath);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (UnauthorizedAccessException)
            {
                MergeWorkingChange(
                    changes,
                    repositoryPath,
                    FolderProjectWorkingChangeKind.Unreadable);
            }
            catch (IOException)
            {
                MergeWorkingChange(
                    changes,
                    repositoryPath,
                    FolderProjectWorkingChangeKind.Unreadable);
            }
        }
    }

    private static void AddUnreadableDirectory(
        IDictionary<string, FolderProjectWorkingChangeKind> changes,
        string relativeDirectory)
    {
        MergeWorkingChange(
            changes,
            relativeDirectory.Length == 0 ? "." : relativeDirectory,
            FolderProjectWorkingChangeKind.Unreadable);
    }

    private static void MergeWorkingChange(
        IDictionary<string, FolderProjectWorkingChangeKind> changes,
        string repositoryPath,
        FolderProjectWorkingChangeKind kind)
    {
        if (kind == FolderProjectWorkingChangeKind.None)
            return;

        changes.TryGetValue(repositoryPath, out var existingKind);
        changes[repositoryPath] = existingKind | kind;
    }

    public FolderProjectCommitSummary Initialize(
        string projectRoot,
        FolderProjectGitIdentity identity)
    {
        ValidateIdentity(identity);
        var repositoryState = GetRepositoryState(projectRoot);
        if (repositoryState == RepositoryState.Unsupported)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedRepository,
                "The folder project contains unsupported Git metadata.");
        }
        if (repositoryState == RepositoryState.Initialized)
        {
            Execute(
                () =>
                {
                    using var repository = OpenRepository(projectRoot);
                    EnsureCommitStateSupported(repository);
                    return true;
                });
        }
        var policySnapshot = repositoryState == RepositoryState.Initialized
            ? Execute(
                () => PolicyFilesSnapshot.Capture(
                    Path.GetFullPath(projectRoot)))
            : null;
        try
        {
            _platform.InitializeRepository(projectRoot);
        }
        catch (Exception exception)
        {
            throw MapRepositoryException(
                exception,
                FolderProjectVersionControlError.UnsupportedRepository);
        }

        var postflightCompleted = false;
        try
        {
            return Execute(
                () =>
                {
                    using var repository = OpenRepository(projectRoot);
                    EnsureCommitStateSupported(repository);
                    postflightCompleted = true;
                    SetLocalIdentity(repository, identity);
                    if (repository.Head.Tip == null)
                    {
                        Commands.Stage(repository, "*");
                        var signature = CreateSignature(identity);
                        repository.Commit(
                            "初始化文件夹工程",
                            signature,
                            signature);
                    }

                    return ToSummary(repository.Head.Tip!);
                });
        }
        catch (Exception failure)
            when (policySnapshot != null && !postflightCompleted)
        {
            try
            {
                policySnapshot.Restore();
            }
            catch (Exception rollbackFailure)
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.RepositoryFailure,
                    "Repository validation failed and policy rollback was incomplete.",
                    new AggregateException(
                        failure,
                        rollbackFailure));
            }

            throw;
        }
    }

    public FolderProjectGitIdentity GetIdentity(string projectRoot)
    {
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                return ReadLocalIdentity(repository);
            });
    }

    public void SetIdentity(
        string projectRoot,
        FolderProjectGitIdentity identity)
    {
        ValidateIdentity(identity);
        Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                SetLocalIdentity(repository, identity);
                return true;
            });
    }

    public IReadOnlyList<FolderProjectCommitSummary> GetHistory(
        string projectRoot,
        int maxCount = 100)
    {
        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCount),
                maxCount,
                "History count must be greater than zero.");
        }

        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                if (repository.Head.Tip == null)
                    return [];

                return GetHistory(
                    repository,
                    repository.Head.Tip,
                    maxCount);
            });
    }

    public IReadOnlyList<FolderProjectCommitSummary> GetHistory(
        string projectRoot,
        string localBranch,
        int maxCount = 100)
    {
        ValidateBranchName(localBranch);
        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCount),
                maxCount,
                "History count must be greater than zero.");
        }

        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                var branch = FindLocalBranch(repository, localBranch);
                if (branch.Tip == null)
                    return [];

                return GetHistory(repository, branch.Tip, maxCount);
            });
    }

    private static IReadOnlyList<FolderProjectCommitSummary> GetHistory(
        Repository repository,
        Commit tip,
        int maxCount)
    {
        return repository.Commits.QueryBy(
                new CommitFilter
                {
                    IncludeReachableFrom = tip,
                    SortBy =
                        CommitSortStrategies.Topological |
                        CommitSortStrategies.Time,
                })
            .Take(maxCount)
            .Select(ToSummary)
            .ToList();
    }

    public FolderProjectCommitSummary CommitAll(
        string projectRoot,
        string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.EmptyCommitMessage,
                "A commit message is required.");
        }

        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                var identity = ReadLocalIdentity(repository);
                ValidateIdentity(identity);
                var signature = CreateSignature(identity);
                var changes = RetrieveWorkingStatus(repository);
                if (!changes.Any())
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.NothingToCommit,
                        "The folder project has no changes to commit.");
                }

                var indexSnapshot = GitIndexSnapshot.Capture(
                    Path.Combine(repository.Info.Path, "index"));
                var originalHeadId = repository.Head.Tip?.Sha;
                Commit commit;
                try
                {
                    Commands.Stage(repository, "*");
                    commit = _platform.Commit(
                        repository,
                        message.Trim(),
                        signature);
                }
                catch (Exception failure)
                {
                    if (string.Equals(
                            repository.Head.Tip?.Sha,
                            originalHeadId,
                            StringComparison.Ordinal))
                    {
                        try
                        {
                            indexSnapshot.Restore(_platform);
                        }
                        catch (Exception rollbackFailure)
                        {
                            throw new AggregateException(
                                "Commit failed and the Git index rollback was incomplete.",
                                [failure, rollbackFailure]);
                        }
                    }

                    throw;
                }

                return ToSummary(commit);
            });
    }

    public IReadOnlyList<FolderProjectCommitChange> GetCommitChanges(
        string projectRoot,
        string commitId)
    {
        var objectId = ParseFullCommitId(commitId);
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                var commit = repository.Lookup<Commit>(objectId);
                if (commit == null)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitNotFound,
                        "The requested commit does not exist.");
                }

                var parent = commit.Parents.FirstOrDefault();
                var changes = repository.Diff.Compare<TreeChanges>(
                    parent?.Tree!,
                    commit.Tree,
                    new CompareOptions
                    {
                        Similarity = SimilarityOptions.Renames,
                    });
                return changes
                    .Where(
                        change =>
                            change.Status != ChangeKind.Unmodified)
                    .Select(
                        change => ToCommitChange(
                            commit,
                            parent,
                            change))
                    .OrderBy(
                        change => change.RepositoryPath,
                        StringComparer.Ordinal)
                    .ToList();
            });
    }

    private static Repository OpenRepository(string projectRoot)
    {
        var repositoryState = GetRepositoryState(projectRoot);
        if (repositoryState != RepositoryState.Initialized)
        {
            throw new FolderProjectVersionControlException(
                repositoryState == RepositoryState.Unsupported
                    ? FolderProjectVersionControlError.UnsupportedRepository
                    : FolderProjectVersionControlError.RepositoryNotInitialized,
                repositoryState == RepositoryState.Unsupported
                    ? "The folder project contains unsupported Git metadata."
                    : "The folder project has no local repository.");
        }

        return new Repository(Path.GetFullPath(projectRoot));
    }

    private static void ValidateIdentity(
        FolderProjectGitIdentity identity)
    {
        if (identity == null ||
            string.IsNullOrWhiteSpace(identity.Name) ||
            string.IsNullOrWhiteSpace(identity.Email) ||
            ContainsInvalidIdentityCharacter(identity.Name) ||
            ContainsInvalidIdentityCharacter(identity.Email))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.InvalidIdentity,
                "A valid name and email address are required.");
        }
    }

    private static bool ContainsInvalidIdentityCharacter(string value)
    {
        return value.IndexOfAny(['\0', '\r', '\n']) >= 0;
    }

    private void SetLocalIdentity(
        Repository repository,
        FolderProjectGitIdentity identity)
    {
        var nameSnapshot = CaptureLocalConfig(
            repository,
            "user.name");
        var emailSnapshot = CaptureLocalConfig(
            repository,
            "user.email");
        try
        {
            _platform.SetLocalConfig(
                repository,
                "user.name",
                identity.Name);
            _platform.SetLocalConfig(
                repository,
                "user.email",
                identity.Email);
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            TryRestoreLocalConfig(
                repository,
                "user.name",
                nameSnapshot,
                rollbackFailures);
            TryRestoreLocalConfig(
                repository,
                "user.email",
                emailSnapshot,
                rollbackFailures);
            if (rollbackFailures.Count != 0)
            {
                throw new AggregateException(
                    "Local Git identity update failed and rollback was incomplete.",
                    [failure, .. rollbackFailures]);
            }

            throw;
        }
    }

    private static LocalConfigSnapshot CaptureLocalConfig(
        Repository repository,
        string key)
    {
        var entry = repository.Config.Get<string>(
            key,
            ConfigurationLevel.Local);
        return new LocalConfigSnapshot(
            entry != null,
            entry?.Value);
    }

    private static void TryRestoreLocalConfig(
        Repository repository,
        string key,
        LocalConfigSnapshot snapshot,
        ICollection<Exception> failures)
    {
        try
        {
            if (snapshot.Exists)
            {
                repository.Config.Set(
                    key,
                    snapshot.Value!,
                    ConfigurationLevel.Local);
            }
            else
            {
                repository.Config.Unset(
                    key,
                    ConfigurationLevel.Local);
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static FolderProjectGitIdentity ReadLocalIdentity(
        Repository repository)
    {
        var name = repository.Config.Get<string>(
            "user.name",
            ConfigurationLevel.Local)?.Value;
        var email = repository.Config.Get<string>(
            "user.email",
            ConfigurationLevel.Local)?.Value;
        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(email))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.IdentityMissing,
                "The folder-project repository has no local identity.");
        }

        return new FolderProjectGitIdentity(name, email);
    }

    private static void EnsureCommitStateSupported(
        Repository repository)
    {
        if (repository.Info.IsHeadDetached ||
            repository.Info.CurrentOperation != CurrentOperation.None ||
            repository.Index.Conflicts.Any())
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedOperationState,
                "Committing is unavailable in the current repository state.");
        }

        if (File.Exists(
                Path.Combine(repository.Info.Path, "index.lock")))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryBusy,
                "The folder-project repository is busy.");
        }
    }

    private static RepositoryStatus RetrieveWorkingStatus(
        Repository repository)
    {
        return repository.RetrieveStatus(
            new StatusOptions
            {
                DetectRenamesInIndex = true,
                DetectRenamesInWorkDir = true,
                IncludeIgnored = false,
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            });
    }

    private static Signature CreateSignature(
        FolderProjectGitIdentity identity)
    {
        return new Signature(
            identity.Name,
            identity.Email,
            DateTimeOffset.Now);
    }

    private static ObjectId ParseFullCommitId(string commitId)
    {
        if (string.IsNullOrWhiteSpace(commitId) ||
            commitId.Length != 40 ||
            commitId.Any(character => !IsAsciiHexDigit(character)) ||
            !ObjectId.TryParse(
                commitId.ToLowerInvariant(),
                out var objectId))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.InvalidCommitId,
                "A full 40-character hexadecimal commit ID is required.");
        }

        return objectId;
    }

    private static bool IsAsciiHexDigit(char character)
    {
        return character is >= '0' and <= '9' or
               >= 'a' and <= 'f' or
               >= 'A' and <= 'F';
    }

    private static FolderProjectCommitChange ToCommitChange(
        Commit commit,
        Commit? parent,
        TreeEntryChanges change)
    {
        var kind = change.Status switch
        {
            ChangeKind.Added =>
                FolderProjectCommitChangeKind.Added,
            ChangeKind.Modified =>
                FolderProjectCommitChangeKind.Modified,
            ChangeKind.Deleted =>
                FolderProjectCommitChangeKind.Deleted,
            ChangeKind.Renamed =>
                FolderProjectCommitChangeKind.Renamed,
            ChangeKind.TypeChanged =>
                FolderProjectCommitChangeKind.TypeChanged,
            _ => throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryFailure,
                $"Unsupported commit change type: {change.Status}."),
        };
        var previousPath = change.Status == ChangeKind.Renamed
            ? change.OldPath
            : null;
        return new FolderProjectCommitChange(
            change.Path,
            previousPath,
            kind,
            IsBinaryChange(commit, parent, change));
    }

    private static bool IsBinaryChange(
        Commit commit,
        Commit? parent,
        TreeEntryChanges change)
    {
        TreeEntry? entry = null;
        if (change.Exists)
            entry = commit.Tree[change.Path];
        else if (parent != null && change.OldExists)
            entry = parent.Tree[change.OldPath];

        return entry?.Target is Blob blob && blob.IsBinary;
    }

    private static FolderProjectCommitSummary ToSummary(Commit commit)
    {
        return new FolderProjectCommitSummary(
            commit.Sha,
            commit.MessageShort,
            commit.Author.Name,
            commit.Author.Email,
            commit.Author.When,
            commit.Parents.Select(parent => parent.Sha).ToList());
    }

    private static FolderProjectRepositoryOperationState GetOperationState(
        CurrentOperation operation)
    {
        return operation switch
        {
            CurrentOperation.None =>
                FolderProjectRepositoryOperationState.None,
            CurrentOperation.Merge =>
                FolderProjectRepositoryOperationState.Merge,
            _ => FolderProjectRepositoryOperationState.Other,
        };
    }

    internal static FolderProjectWorkingChangeKind MapWorkingChange(
        FileStatus status)
    {
        var kind = FolderProjectWorkingChangeKind.None;
        if (HasAny(
                status,
                FileStatus.NewInIndex |
                FileStatus.NewInWorkdir))
        {
            kind |= FolderProjectWorkingChangeKind.Added;
        }
        if (HasAny(
                status,
                FileStatus.ModifiedInIndex |
                FileStatus.ModifiedInWorkdir))
        {
            kind |= FolderProjectWorkingChangeKind.Modified;
        }
        if (HasAny(
                status,
                FileStatus.DeletedFromIndex |
                FileStatus.DeletedFromWorkdir))
        {
            kind |= FolderProjectWorkingChangeKind.Deleted;
        }
        if (HasAny(
                status,
                FileStatus.RenamedInIndex |
                FileStatus.RenamedInWorkdir))
        {
            kind |= FolderProjectWorkingChangeKind.Renamed;
        }
        if (HasAny(
                status,
                FileStatus.TypeChangeInIndex |
                FileStatus.TypeChangeInWorkdir))
        {
            kind |= FolderProjectWorkingChangeKind.TypeChanged;
        }
        if (HasAny(status, FileStatus.Conflicted))
            kind |= FolderProjectWorkingChangeKind.Conflicted;
        if (HasAny(status, FileStatus.Unreadable))
            kind |= FolderProjectWorkingChangeKind.Unreadable;
        if (HasAny(status, FileStatus.NewInWorkdir))
            kind |= FolderProjectWorkingChangeKind.Untracked;
        if (HasAny(
                status,
                FileStatus.NewInIndex |
                FileStatus.ModifiedInIndex |
                FileStatus.DeletedFromIndex |
                FileStatus.RenamedInIndex |
                FileStatus.TypeChangeInIndex))
        {
            kind |= FolderProjectWorkingChangeKind.Staged;
        }
        if (HasAny(
                status,
                FileStatus.NewInWorkdir |
                FileStatus.ModifiedInWorkdir |
                FileStatus.DeletedFromWorkdir |
                FileStatus.RenamedInWorkdir |
                FileStatus.TypeChangeInWorkdir))
        {
            kind |= FolderProjectWorkingChangeKind.Unstaged;
        }

        return kind;
    }

    private static bool HasAny(FileStatus value, FileStatus flags)
    {
        return (value & flags) != 0;
    }

    private static RepositoryState GetRepositoryState(string projectRoot)
    {
        try
        {
            if (FolderProjectGitRepository.IsRepository(projectRoot))
                return RepositoryState.Initialized;

            var root = Path.GetFullPath(projectRoot);
            var markerPath = Path.Combine(root, ".git");
            if (File.Exists(markerPath) ||
                Directory.Exists(markerPath) ||
                IsBareRepositoryRoot(root))
            {
                return RepositoryState.Unsupported;
            }

            return RepositoryState.Uninitialized;
        }
        catch (FolderProjectVersionControlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapRepositoryException(
                exception,
                FolderProjectVersionControlError.UnsupportedRepository);
        }
    }

    private static bool IsBareRepositoryRoot(string root)
    {
        if (!File.Exists(Path.Combine(root, "HEAD")) ||
            !Directory.Exists(Path.Combine(root, "objects")) ||
            !Directory.Exists(Path.Combine(root, "refs")))
        {
            return false;
        }

        try
        {
            using var repository = new Repository(root);
            return repository.Info.IsBare &&
                   Path.TrimEndingDirectorySeparator(
                       Path.GetFullPath(repository.Info.Path))
                       .Equals(
                           Path.TrimEndingDirectorySeparator(root),
                           StringComparison.OrdinalIgnoreCase);
        }
        catch (RepositoryNotFoundException)
        {
            return false;
        }
    }

    private static T Execute<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (FolderProjectVersionControlException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapRepositoryException(
                exception,
                FolderProjectVersionControlError.RepositoryFailure);
        }
    }

    private static FolderProjectVersionControlException
        MapRepositoryException(
            Exception exception,
            FolderProjectVersionControlError fallback)
    {
        var code = exception switch
        {
            RepositoryNotFoundException =>
                FolderProjectVersionControlError.RepositoryNotInitialized,
            LockedFileException =>
                FolderProjectVersionControlError.RepositoryBusy,
            UnauthorizedAccessException or IOException =>
                FolderProjectVersionControlError.RepositoryFailure,
            AggregateException aggregateException
                when aggregateException.InnerExceptions.Any(
                    IsRepositoryStorageFailure) =>
                FolderProjectVersionControlError.RepositoryFailure,
            _ => fallback,
        };
        return new FolderProjectVersionControlException(
            code,
            "The folder-project repository operation failed.",
            exception);
    }

    private static bool IsRepositoryStorageFailure(Exception exception)
    {
        return exception is UnauthorizedAccessException or IOException ||
               exception is AggregateException aggregateException &&
               aggregateException.InnerExceptions.Any(
                   IsRepositoryStorageFailure);
    }

    private enum RepositoryState
    {
        Uninitialized,
        Initialized,
        Unsupported,
    }

    private sealed record LocalConfigSnapshot(
        bool Exists,
        string? Value);

    private sealed record TrackedRepositoryPaths(
        IReadOnlySet<string> ExactPaths,
        IReadOnlySet<string> ParentDirectories)
    {
        public static TrackedRepositoryPaths Create(
            LibGit2Sharp.Index index)
        {
            var exactPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var parentDirectories = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var entry in index)
            {
                var repositoryPath = entry.Path.Replace('\\', '/');
                exactPaths.Add(repositoryPath);
                var separatorIndex = repositoryPath.IndexOf('/');
                while (separatorIndex > 0)
                {
                    parentDirectories.Add(
                        repositoryPath[..separatorIndex]);
                    separatorIndex = repositoryPath.IndexOf(
                        '/',
                        separatorIndex + 1);
                }
            }

            return new TrackedRepositoryPaths(
                exactPaths,
                parentDirectories);
        }
    }

    private sealed record PolicyFilesSnapshot(
        IReadOnlyList<PolicyFileSnapshot> Files)
    {
        public static PolicyFilesSnapshot Capture(string projectRoot)
        {
            var metadataPath = Path.Combine(projectRoot, ".git");
            return new PolicyFilesSnapshot(
            [
                PolicyFileSnapshot.Capture(
                    Path.Combine(projectRoot, ".gitignore")),
                PolicyFileSnapshot.Capture(
                    Path.Combine(projectRoot, ".gitattributes")),
                PolicyFileSnapshot.Capture(
                    Path.Combine(metadataPath, "info", "exclude")),
                PolicyFileSnapshot.Capture(
                    Path.Combine(metadataPath, "info", "attributes")),
            ]);
        }

        public void Restore()
        {
            var failures = new List<Exception>();
            foreach (var file in Files)
            {
                try
                {
                    file.Restore();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count != 0)
            {
                throw new AggregateException(
                    "One or more policy files could not be restored.",
                    failures);
            }
        }
    }

    private sealed record PolicyFileSnapshot(
        string Path,
        bool Exists,
        byte[] Bytes,
        FileAttributes Attributes)
    {
        public static PolicyFileSnapshot Capture(string path)
        {
            if (!File.Exists(path))
            {
                return new PolicyFileSnapshot(
                    path,
                    false,
                    [],
                    FileAttributes.Normal);
            }

            return new PolicyFileSnapshot(
                path,
                true,
                File.ReadAllBytes(path),
                File.GetAttributes(path));
        }

        public void Restore()
        {
            if (!Exists)
            {
                if (File.Exists(Path))
                {
                    File.SetAttributes(Path, FileAttributes.Normal);
                    File.Delete(Path);
                }
                return;
            }

            if (File.Exists(Path))
                File.SetAttributes(Path, FileAttributes.Normal);
            File.WriteAllBytes(Path, Bytes);
            File.SetAttributes(Path, Attributes);
        }
    }

    private sealed record GitIndexSnapshot(
        string Path,
        bool Exists,
        byte[] Bytes,
        FileAttributes Attributes)
    {
        public static GitIndexSnapshot Capture(string path)
        {
            if (!File.Exists(path))
            {
                return new GitIndexSnapshot(
                    path,
                    false,
                    [],
                    FileAttributes.Normal);
            }

            return new GitIndexSnapshot(
                path,
                true,
                File.ReadAllBytes(path),
                File.GetAttributes(path));
        }

        public void Restore(
            FolderProjectVersionControlPlatform platform)
        {
            if (!Exists)
            {
                if (File.Exists(Path))
                {
                    File.SetAttributes(Path, FileAttributes.Normal);
                    File.Delete(Path);
                }
                return;
            }

            var directoryPath = System.IO.Path.GetDirectoryName(Path)!;
            var temporaryPath = System.IO.Path.Combine(
                directoryPath,
                $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporaryPath, Bytes);
                File.SetAttributes(temporaryPath, Attributes);
                platform.MoveFile(
                    temporaryPath,
                    Path,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.SetAttributes(
                        temporaryPath,
                        FileAttributes.Normal);
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
