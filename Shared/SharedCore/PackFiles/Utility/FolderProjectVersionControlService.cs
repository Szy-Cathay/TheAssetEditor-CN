using LibGit2Sharp;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.PackFiles.Utility;

public interface IFolderProjectVersionControlService
{
    FolderProjectRepositoryStatus GetStatus(
        string projectRoot,
        bool scanUnreadableEntries = false);

    FolderProjectRepositoryStatus GetStatus(
        string projectRoot,
        Action<FolderProjectVersionControlProgress> reportProgress,
        bool scanUnreadableEntries = false);

    FolderProjectRepositoryStatus GetStatus(
        string projectRoot,
        IReadOnlyList<string> relativePaths);

    FolderProjectCommitSummary Initialize(
        string projectRoot,
        FolderProjectGitIdentity identity,
        string primaryBranchName = "master");

    FolderProjectCommitSummary Initialize(
        string projectRoot,
        FolderProjectGitIdentity identity,
        string primaryBranchName,
        Action<FolderProjectVersionControlProgress> reportProgress);

    FolderProjectGitIdentity GetIdentity(string projectRoot);

    void SetIdentity(
        string projectRoot,
        FolderProjectGitIdentity identity);

    FolderProjectCommitSummary CommitAll(
        string projectRoot,
        string message);

    void StageChanges(
        string projectRoot,
        IReadOnlyList<string> relativePaths);

    void UnstageChanges(
        string projectRoot,
        IReadOnlyList<string> relativePaths);

    void DiscardChanges(
        string projectRoot,
        IReadOnlyList<string> relativePaths);

    FolderProjectDiscardRollback BeginDiscardChanges(
        string projectRoot,
        IReadOnlyList<string> relativePaths,
        Action<FolderProjectVersionControlProgress>? reportProgress = null);

    void CompleteDiscardChanges(FolderProjectDiscardRollback rollback);

    void RollbackDiscardChanges(
        string projectRoot,
        FolderProjectDiscardRollback rollback);

    FolderProjectCommitSummary CommitStaged(
        string projectRoot,
        string message);

    IReadOnlyList<FolderProjectStashInfo> GetStashes(
        string projectRoot);

    FolderProjectStashInfo StashChanges(
        string projectRoot,
        string message);

    void ApplyStash(
        string projectRoot,
        int index);

    void PopStash(
        string projectRoot,
        int index);

    void DeleteStash(
        string projectRoot,
        int index);

    void ClearStashes(string projectRoot);

    void UndoLatestCommit(
        string projectRoot,
        string commitId,
        FolderProjectCommitUndoMode mode);

    void ResetToCommit(
        string projectRoot,
        string commitId,
        FolderProjectCommitUndoMode mode);

    FolderProjectCommitSummary RevertCommit(
        string projectRoot,
        string commitId);

    FolderProjectCommitSummary RevertCommitChanges(
        string projectRoot,
        string commitId,
        IReadOnlyList<string> relativePaths);

    FolderProjectCommitEditSession EditLatestCommitChanges(
        string projectRoot,
        string commitId,
        IReadOnlyList<string> relativePaths,
        FolderProjectCommitChangeEditMode mode);

    FolderProjectCommitSummary CompleteLatestCommitEdit(
        string projectRoot,
        FolderProjectCommitEditSession editSession);

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

    IReadOnlyList<FolderProjectCommitChange> GetCommitChanges(
        string projectRoot,
        string commitId,
        Action<FolderProjectVersionControlProgress> reportProgress);

    FolderProjectCommitChangeSummary GetCommitChangeSummary(
        string projectRoot,
        string commitId);

    int GetRestoreImpactCount(
        string projectRoot,
        string commitId);

    FolderProjectFileRestoreResult RestoreFile(
        string projectRoot,
        string commitId,
        string relativePath,
        bool overwriteWorkingChange = false);

    FolderProjectFileRestoreTransaction BeginRestoreFile(
        string projectRoot,
        string commitId,
        string relativePath,
        bool overwriteWorkingChange = false);

    void CompleteRestoreFile(FolderProjectFileRestoreTransaction transaction);

    void RollbackRestoreFile(FolderProjectFileRestoreTransaction transaction);

    FolderProjectProjectRestoreResult RestoreProject(
        string projectRoot,
        string commitId,
        string safetyMessage,
        string restoreMessage,
        Action<FolderProjectVersionControlProgress> reportProgress);

    void RollbackProjectRestore(
        string projectRoot,
        FolderProjectProjectRestoreRollback rollback);

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

    FolderProjectBranchInfo SwitchBranch(
        string projectRoot,
        string name,
        FolderProjectBranchSwitchMode mode,
        string? stashMessage = null);

    FolderProjectMergeState GetMergeState(string projectRoot);

    FolderProjectMergeStartResult BeginMerge(
        string projectRoot,
        string sourceLocalBranch);

    FolderProjectMergeStartResult BeginMerge(
        string projectRoot,
        string sourceLocalBranch,
        Action<FolderProjectVersionControlProgress> reportProgress);

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
    public virtual RepositoryStatus RetrieveStatus(
        Repository repository,
        StatusOptions options)
    {
        return repository.RetrieveStatus(options);
    }

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

    public virtual void MoveDirectory(
        string sourcePath,
        string destinationPath)
    {
        Directory.Move(sourcePath, destinationPath);
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

    public virtual void DeleteDirectory(string path)
    {
        Directory.Delete(path, recursive: true);
    }
}

public sealed partial class FolderProjectVersionControlService :
    IFolderProjectVersionControlService
{
    private const string PrimaryBranchConfigKey =
        "asseteditor.primaryBranch";
    private const string InitialCommitConfigKey =
        "asseteditor.initialCommit";
    private static readonly HashSet<string> s_knownBinaryAudioExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".bnk",
            ".wav",
            ".wem",
        };
    private readonly FolderProjectVersionControlPlatform _platform;
    private static readonly ILogger s_logger =
        Logging.Create<FolderProjectVersionControlService>();

    public FolderProjectVersionControlService()
        : this(new FolderProjectVersionControlPlatform())
    {
    }

    internal FolderProjectVersionControlService(
        FolderProjectVersionControlPlatform platform)
    {
        _platform = platform;
    }

    private void FinalizeStagingDirectory(string stagingPath)
    {
        if (!Directory.Exists(stagingPath))
            return;

        var cleanupPath = Path.Combine(
            Path.GetDirectoryName(stagingPath)!,
            $"ae-cleanup-{Guid.NewGuid():N}");
        _platform.MoveDirectory(stagingPath, cleanupPath);
        TryDeleteFinalizedStagingDirectory(cleanupPath);
    }

    private void TryDeleteFinalizedStagingDirectory(string stagingPath)
    {
        try
        {
            if (Directory.Exists(stagingPath))
                _platform.DeleteDirectory(stagingPath);
        }
        catch (Exception exception)
        {
            s_logger.Warning(
                exception,
                "Could not remove finalized folder project transaction data at {StagingPath}",
                stagingPath);
        }
    }

    private void TryDeleteFinalizedStagingDirectories(string repositoryPath)
    {
        try
        {
            foreach (var path in Directory.EnumerateDirectories(
                         repositoryPath,
                         "ae-cleanup-*",
                         SearchOption.TopDirectoryOnly))
            {
                TryDeleteFinalizedStagingDirectory(path);
            }
        }
        catch (Exception exception)
        {
            s_logger.Warning(
                exception,
                "Could not scan finalized folder project transaction data at {RepositoryPath}",
                repositoryPath);
        }
    }

    public FolderProjectRepositoryStatus GetStatus(
        string projectRoot,
        bool scanUnreadableEntries = false)
    {
        return GetStatusCore(
            projectRoot,
            scanUnreadableEntries,
            pathSpec: null,
            reportProgress: null);
    }

    public FolderProjectRepositoryStatus GetStatus(
        string projectRoot,
        Action<FolderProjectVersionControlProgress> reportProgress,
        bool scanUnreadableEntries = false)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        return GetStatusCore(
            projectRoot,
            scanUnreadableEntries,
            pathSpec: null,
            reportProgress);
    }

    public FolderProjectRepositoryStatus GetStatus(
        string projectRoot,
        IReadOnlyList<string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        return GetStatusCore(
            projectRoot,
            scanUnreadableEntries: false,
            relativePaths,
            reportProgress: null);
    }

    private FolderProjectRepositoryStatus GetStatusCore(
        string projectRoot,
        bool scanUnreadableEntries,
        IReadOnlyList<string>? pathSpec,
        Action<FolderProjectVersionControlProgress>? reportProgress)
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
                    Path.GetFullPath(projectRoot),
                    scanUnreadableEntries,
                    pathSpec,
                    reportProgress);
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
        string projectRoot,
        bool scanUnreadableEntries = true,
        IReadOnlyList<string>? pathSpec = null,
        Action<FolderProjectVersionControlProgress>? reportProgress = null)
    {
        var trackedPaths = scanUnreadableEntries
            ? TrackedRepositoryPaths.Create(repository.Index)
            : null;
        var changes = new Dictionary<
            string,
            FolderProjectWorkingChangeKind>(
            StringComparer.OrdinalIgnoreCase);
        var previousPaths = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        reportProgress?.Invoke(new FolderProjectVersionControlProgress(
            FolderProjectVersionControlProgressStage.ScanningWorkingTree,
            projectRoot));
        var statusEntries = RetrieveWorkingStatus(repository, pathSpec)
            .Where(entry => entry.State != FileStatus.Ignored)
            .ToList();
        for (var index = 0; index < statusEntries.Count; index++)
        {
            var entry = statusEntries[index];
            var repositoryPath = entry.FilePath.Replace('\\', '/');
            MergeWorkingChange(
                changes,
                repositoryPath,
                MapWorkingChange(entry.State));
            var previousPath = GetPreviousRepositoryPath(entry);
            if (previousPath != null)
                previousPaths[repositoryPath] = previousPath;
            reportProgress?.Invoke(new FolderProjectVersionControlProgress(
                FolderProjectVersionControlProgressStage
                    .ProcessingWorkingChanges,
                repositoryPath,
                index + 1,
                statusEntries.Count));
        }

        if (scanUnreadableEntries)
        {
            ScanUnreadableEntries(
                repository,
                projectRoot,
                "",
                trackedPaths!,
                changes);
        }
        return changes
            .Where(change =>
                change.Value != FolderProjectWorkingChangeKind.None)
            .OrderBy(change => change.Key, StringComparer.Ordinal)
             .Select(change => new FolderProjectWorkingChange(
                 change.Key,
                 change.Value,
                 previousPaths.GetValueOrDefault(change.Key)))
            .ToList();
    }

    private static string? GetPreviousRepositoryPath(StatusEntry entry)
    {
        var previousPath =
            entry.IndexToWorkDirRenameDetails?.OldFilePath ??
            entry.HeadToIndexRenameDetails?.OldFilePath;
        return previousPath?.Replace('\\', '/');
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
        FolderProjectGitIdentity identity,
        string primaryBranchName = "master")
    {
        return InitializeCore(
            projectRoot,
            identity,
            primaryBranchName,
            reportProgress: null);
    }

    public FolderProjectCommitSummary Initialize(
        string projectRoot,
        FolderProjectGitIdentity identity,
        string primaryBranchName,
        Action<FolderProjectVersionControlProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        return InitializeCore(
            projectRoot,
            identity,
            primaryBranchName,
            reportProgress);
    }

    private FolderProjectCommitSummary InitializeCore(
        string projectRoot,
        FolderProjectGitIdentity identity,
        string primaryBranchName,
        Action<FolderProjectVersionControlProgress>? reportProgress)
    {
        ValidateIdentity(identity);
        primaryBranchName = primaryBranchName.Trim();
        ValidateBranchName(primaryBranchName);
        reportProgress?.Invoke(
            new FolderProjectVersionControlProgress(
                FolderProjectVersionControlProgressStage.PreparingRepository,
                Path.GetFullPath(projectRoot)));
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
                        if (!repository.Head.FriendlyName.Equals(
                                primaryBranchName,
                                StringComparison.Ordinal))
                        {
                            repository.Refs.UpdateTarget(
                                "HEAD",
                                $"refs/heads/{primaryBranchName}",
                                "asseteditor: Set primary branch");
                        }
                        if (reportProgress == null)
                            Commands.Stage(repository, "*");
                        else
                            StageInitialFiles(repository, reportProgress);
                        reportProgress?.Invoke(
                            new FolderProjectVersionControlProgress(
                                FolderProjectVersionControlProgressStage
                                    .CreatingInitialCommit,
                                Completed: 0,
                                Total: 1));
                        var signature = CreateSignature(identity);
                        var initialCommit = repository.Commit(
                            "初始化文件夹工程",
                            signature,
                            signature);
                        reportProgress?.Invoke(
                            new FolderProjectVersionControlProgress(
                                FolderProjectVersionControlProgressStage
                                    .CreatingInitialCommit,
                                Completed: 1,
                                Total: 1));
                        SetRepositoryMetadata(
                            repository,
                            primaryBranchName,
                            initialCommit.Sha);
                    }
                    else
                        EnsureRepositoryMetadata(repository);

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

    private void StageInitialFiles(
        Repository repository,
        Action<FolderProjectVersionControlProgress> reportProgress)
    {
        reportProgress(
            new FolderProjectVersionControlProgress(
                FolderProjectVersionControlProgressStage.ScanningWorkingTree));
        var repositoryPaths = RetrieveWorkingStatus(repository)
            .Where(entry => entry.State != FileStatus.Ignored)
            .Select(entry => entry.FilePath.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        for (var index = 0; index < repositoryPaths.Count; index++)
        {
            var repositoryPath = repositoryPaths[index];
            repository.Index.Add(repositoryPath);
            reportProgress(
                new FolderProjectVersionControlProgress(
                    FolderProjectVersionControlProgressStage.IndexingFiles,
                    repositoryPath,
                    index + 1,
                    repositoryPaths.Count));
        }
        repository.Index.Write();
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
        var primaryBranch = GetPrimaryBranch(repository);
        var primaryCommitIds = primaryBranch?.Tip == null
            ? null
            : repository.Commits.QueryBy(
                    new CommitFilter
                    {
                        IncludeReachableFrom =
                            primaryBranch.Tip,
                    })
                .Select(commit => commit.Sha)
                .ToHashSet(StringComparer.Ordinal);
        var reachableCommits = repository.Commits.QueryBy(
                new CommitFilter
                {
                    IncludeReachableFrom = tip,
                    SortBy =
                        CommitSortStrategies.Topological |
                        CommitSortStrategies.Time,
                })
            .ToList();
        var visibleCommits = reachableCommits
            .Take(maxCount)
            .ToList();

        return visibleCommits
            .Select(
                commit => ToSummary(
                    commit,
                    primaryCommitIds == null
                        ? FolderProjectCommitMergeStatus.Unknown
                        : primaryCommitIds.Contains(commit.Sha)
                            ? FolderProjectCommitMergeStatus.Merged
                            : FolderProjectCommitMergeStatus.NotMerged))
            .ToList();
    }

    private static void SetRepositoryMetadata(
        Repository repository,
        string primaryBranchName,
        string initialCommitId)
    {
        repository.Config.Set(
            PrimaryBranchConfigKey,
            primaryBranchName,
            ConfigurationLevel.Local);
        repository.Config.Set(
            InitialCommitConfigKey,
            initialCommitId,
            ConfigurationLevel.Local);
    }

    private static void EnsureRepositoryMetadata(Repository repository)
    {
        var primaryBranch = GetPrimaryBranch(repository);
        var initialCommit = GetInitialCommit(
            repository,
            repository.Commits.ToList());
        if (primaryBranch != null && initialCommit != null)
        {
            SetRepositoryMetadata(
                repository,
                primaryBranch.FriendlyName,
                initialCommit.Sha);
        }
    }

    private static Branch? GetPrimaryBranch(Repository repository)
    {
        var configuredName = repository.Config.Get<string>(
            PrimaryBranchConfigKey,
            ConfigurationLevel.Local)?.Value;
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            var configuredBranch = repository.Branches[configuredName];
            if (configuredBranch != null && !configuredBranch.IsRemote)
                return configuredBranch;
        }

        return repository.Branches["master"] ??
               repository.Branches.FirstOrDefault(
                   branch => branch.IsCurrentRepositoryHead) ??
               repository.Branches.FirstOrDefault(
                   branch => !branch.IsRemote);
    }

    private static Commit? GetInitialCommit(
        Repository repository,
        IReadOnlyList<Commit> reachableCommits)
    {
        var configuredId = repository.Config.Get<string>(
            InitialCommitConfigKey,
            ConfigurationLevel.Local)?.Value;
        if (!string.IsNullOrWhiteSpace(configuredId))
        {
            var configuredCommit = repository.Lookup<Commit>(configuredId);
            if (configuredCommit != null)
                return configuredCommit;
        }

        return reachableCommits.LastOrDefault(
            commit => !commit.Parents.Any());
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
        string commitId) =>
        GetCommitChanges(projectRoot, commitId, _ => { });

    public IReadOnlyList<FolderProjectCommitChange> GetCommitChanges(
        string projectRoot,
        string commitId,
        Action<FolderProjectVersionControlProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        var objectId = ParseFullCommitId(commitId);
        reportProgress(new FolderProjectVersionControlProgress(
            FolderProjectVersionControlProgressStage.ReadingCommitChanges,
            commitId));
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
                var relevantChanges = changes
                    .Where(
                        change =>
                            change.Status != ChangeKind.Unmodified)
                    .ToList();
                var result = new List<FolderProjectCommitChange>(
                    relevantChanges.Count);
                for (var index = 0; index < relevantChanges.Count; index++)
                {
                    var mappedChange = ToCommitChange(
                        commit,
                        parent,
                        relevantChanges[index]);
                    result.Add(mappedChange);
                    reportProgress(new FolderProjectVersionControlProgress(
                        FolderProjectVersionControlProgressStage
                            .ProcessingCommitChanges,
                        mappedChange.RepositoryPath,
                        index + 1,
                        relevantChanges.Count));
                }

                return result
                    .OrderBy(
                        change => change.RepositoryPath,
                        StringComparer.Ordinal)
                    .ToList();
            });
    }

    public FolderProjectCommitChangeSummary GetCommitChangeSummary(
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
                return new FolderProjectCommitChangeSummary(
                    changes.Count(change =>
                        change.Status == ChangeKind.Added),
                    changes.Count(change =>
                        change.Status == ChangeKind.Modified),
                    changes.Count(change =>
                        change.Status == ChangeKind.Deleted),
                    changes.Count(change =>
                        change.Status == ChangeKind.Renamed),
                    changes.Count(change =>
                        change.Status == ChangeKind.TypeChanged));
            });
    }

    private Repository OpenRepository(string projectRoot)
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

        var repository = new Repository(Path.GetFullPath(projectRoot));
        TryDeleteFinalizedStagingDirectories(repository.Info.Path);
        return repository;
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

    private RepositoryStatus RetrieveWorkingStatus(
        Repository repository,
        IReadOnlyList<string>? pathSpec = null)
    {
        var options = new StatusOptions
        {
            DetectRenamesInIndex = true,
            DetectRenamesInWorkDir = true,
            IncludeIgnored = false,
            IncludeUntracked = true,
            RecurseUntrackedDirs = true,
        };
        if (pathSpec != null)
        {
            options.PathSpec = pathSpec.ToArray();
            options.DisablePathSpecMatch = true;
        }

        return _platform.RetrieveStatus(
            repository,
            options);
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
        if (IsKnownBinaryAudioPath(change.Path) ||
            change.OldExists && IsKnownBinaryAudioPath(change.OldPath))
        {
            return true;
        }

        TreeEntry? entry = null;
        if (change.Exists)
            entry = commit.Tree[change.Path];
        else if (parent != null && change.OldExists)
            entry = parent.Tree[change.OldPath];

        return entry?.Target is Blob blob && blob.IsBinary;
    }

    private static bool IsKnownBinaryAudioPath(string path)
    {
        return s_knownBinaryAudioExtensions.Contains(
            Path.GetExtension(path));
    }

    private static FolderProjectCommitSummary ToSummary(
        Commit commit,
        FolderProjectCommitMergeStatus mergeStatus =
            FolderProjectCommitMergeStatus.Unknown)
    {
        var title = commit.MessageShort.Trim();
        var fullMessage = commit.Message.TrimEnd();
        var description = fullMessage.Length > title.Length
            ? fullMessage[title.Length..].TrimStart('\r', '\n')
            : "";
        return new FolderProjectCommitSummary(
            commit.Sha,
            title,
            commit.Author.Name,
            commit.Author.Email,
            commit.Author.When,
            commit.Parents.Select(parent => parent.Sha).ToList())
        {
            Description = description,
            MergeStatus = mergeStatus,
        };
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
