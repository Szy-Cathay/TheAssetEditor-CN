using LibGit2Sharp;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.PackFiles.Utility;

public sealed partial class FolderProjectVersionControlService
{
    private static readonly Mode[] s_regularFileModes =
    [
        Mode.NonExecutableFile,
        Mode.NonExecutableGroupWritableFile,
        Mode.ExecutableFile,
    ];

    public FolderProjectFileRestoreResult RestoreFile(
        string projectRoot,
        string commitId,
        string relativePath,
        bool overwriteWorkingChange = false)
    {
        var objectId = ParseFullCommitId(commitId);
        var repositoryPath = ValidateResourcePath(relativePath);
        return Execute(
            () =>
            {
                var root = Path.GetFullPath(projectRoot);
                var targetPath = ResolveRestoreTarget(
                    root,
                    repositoryPath);
                using var repository = OpenRepository(root);
                EnsureRestoreStateSupported(repository);
                var commit = LookupCommit(repository, objectId);
                if (!commit.Parents.Any())
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitCannotBeUndone,
                        "The initial folder project commit is immutable.");
                }
                var entry = commit.Tree[repositoryPath];
                if (entry == null)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitPathNotFound,
                        "The requested file does not exist in the commit.");
                }
                if (entry.TargetType != TreeEntryTargetType.Blob ||
                    !s_regularFileModes.Contains(entry.Mode) ||
                    entry.Target is not Blob blob)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.UnsupportedCommitPath,
                        "Only regular files can be restored.");
                }

                EnsureRestoreTargetIsSafe(
                    repository,
                    root,
                    repositoryPath,
                    targetPath,
                    overwriteWorkingChange);
                WriteBlobAtomically(blob, root, repositoryPath, targetPath);
                return new FolderProjectFileRestoreResult(
                    commit.Sha,
                    repositoryPath,
                    blob.Size);
            });
    }

    public IReadOnlyList<FolderProjectBranchInfo> GetBranches(
        string projectRoot)
    {
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                return repository.Branches
                    .Where(branch => !branch.IsRemote)
                    .Select(
                        branch => new
                        {
                            Info = ToBranchInfo(repository, branch),
                            CreatedAt = GetBranchCreationTime(
                                repository,
                                branch),
                        })
                    .OrderBy(branch => branch.CreatedAt)
                    .ThenBy(
                        branch => branch.Info.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        branch => branch.Info.Name,
                        StringComparer.Ordinal)
                    .Select(branch => branch.Info)
                    .ToList();
            });
    }

    public FolderProjectBranchInfo CreateRecoveryBranch(
        string projectRoot,
        string name,
        string commitId)
    {
        return CreateBranch(projectRoot, name, commitId);
    }

    public FolderProjectBranchInfo CreateBranch(
        string projectRoot,
        string name,
        string? startCommitId = null)
    {
        ValidateBranchName(name);
        var objectId = startCommitId == null
            ? null
            : ParseFullCommitId(startCommitId);
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureBranchNameAvailable(repository, name);
                var commit = objectId == null
                    ? repository.Head.Tip
                    : LookupCommit(repository, objectId);
                if (commit == null)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitNotFound,
                        "The repository has no commit for the branch.");
                }

                repository.Refs.Add(
                    $"refs/heads/{name}",
                    commit.Id,
                    $"branch: Created from {commit.Sha}");
                var branch = repository.Branches[name]!;
                return ToBranchInfo(repository, branch);
            });
    }

    public FolderProjectBranchInfo RenameBranch(
        string projectRoot,
        string oldName,
        string newName)
    {
        ValidateBranchName(oldName);
        ValidateBranchName(newName);
        if (oldName.Equals(
                newName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.BranchAlreadyExists,
                "Case-only branch renames are not supported.");
        }
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                var branch = FindLocalBranch(repository, oldName);
                if (IsPrimaryBranch(repository, branch))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.PrimaryBranchProtected,
                        "The primary branch cannot be renamed.");
                }
                EnsureBranchNameAvailable(
                    repository,
                    newName,
                    branch.FriendlyName);
                var renamed = repository.Branches.Rename(
                    branch,
                    newName,
                    allowOverwrite: false);
                return ToBranchInfo(repository, renamed);
            });
    }

    public void DeleteBranch(
        string projectRoot,
        string name)
    {
        ValidateBranchName(name);
        Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                var branch = FindLocalBranch(repository, name);
                if (IsPrimaryBranch(repository, branch))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.PrimaryBranchProtected,
                        "The primary branch cannot be deleted.");
                }
                if (branch.IsCurrentRepositoryHead)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CurrentBranchProtected,
                        "The current branch cannot be deleted.");
                }
                if (!IsBranchTipRetained(repository, branch))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.BranchNotMerged,
                        "The branch contains commits that are not retained by another local branch.");
                }

                repository.Branches.Remove(branch);
                return true;
            });
    }

    public FolderProjectBranchInfo SwitchBranch(
        string projectRoot,
        string name)
    {
        return SwitchBranch(
            projectRoot,
            name,
            FolderProjectBranchSwitchMode.CarryChanges);
    }

    public FolderProjectBranchInfo SwitchBranch(
        string projectRoot,
        string name,
        FolderProjectBranchSwitchMode mode,
        string? stashMessage = null)
    {
        if (mode is not FolderProjectBranchSwitchMode.CarryChanges and
            not FolderProjectBranchSwitchMode.StashChanges and
            not FolderProjectBranchSwitchMode.DiscardChanges)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        ValidateBranchName(name);
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                var branch = FindLocalBranch(repository, name);
                var workingStatus = RetrieveWorkingStatus(repository);
                EnsureSwitchStateSupported(
                    repository,
                    Path.GetFullPath(projectRoot),
                    branch,
                    workingStatus);
                var hasChanges = workingStatus.IsDirty;
                var createdStash = false;
                if (hasChanges &&
                    mode != FolderProjectBranchSwitchMode.CarryChanges)
                {
                    CreateStashCore(repository, stashMessage);
                    createdStash = true;
                }

                try
                {
                    var checkedOut = _platform.CheckoutBranch(
                        repository,
                        branch,
                        new CheckoutOptions
                        {
                            CheckoutModifiers = CheckoutModifiers.None,
                        });
                    if (createdStash &&
                        mode == FolderProjectBranchSwitchMode.DiscardChanges)
                    {
                        repository.Stashes.Remove(0);
                    }
                    return ToBranchInfo(repository, checkedOut);
                }
                catch (CheckoutConflictException exception)
                {
                    RestoreSwitchStashAfterFailure(
                        repository,
                        createdStash,
                        exception);
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.WorkingTreeNotClean,
                        "The branch cannot be switched because the working tree changed.",
                        exception);
                }
                catch (Exception exception)
                {
                    RestoreSwitchStashAfterFailure(
                        repository,
                        createdStash,
                        exception);
                    throw;
                }
            });
    }

    private static void RestoreSwitchStashAfterFailure(
        Repository repository,
        bool createdStash,
        Exception failure)
    {
        if (!createdStash)
            return;

        try
        {
            RestoreStashCore(repository, 0, pop: true);
        }
        catch (Exception rollbackFailure)
        {
            throw new AggregateException(
                "Branch switching failed and the saved changes could not be restored.",
                failure,
                rollbackFailure);
        }
    }

    private static bool IsBranchTipRetained(
        Repository repository,
        Branch branch)
    {
        var retainingTips = repository.Branches
            .Where(
                candidate =>
                    !candidate.IsRemote &&
                    !string.Equals(
                        candidate.CanonicalName,
                        branch.CanonicalName,
                        StringComparison.Ordinal))
            .Select(candidate => candidate.Tip);
        if (repository.Info.IsHeadDetached &&
            repository.Head.Tip is { } detachedTip)
        {
            retainingTips = retainingTips.Append(detachedTip);
        }

        return retainingTips.Any(
            tip =>
                repository.ObjectDatabase
                    .CalculateHistoryDivergence(branch.Tip, tip)
                    ?.AheadBy == 0);
    }

    private static string ValidateResourcePath(string relativePath)
    {
        try
        {
            return FolderProjectPathPolicy
                .EnsureResourcePath(relativePath)
                .Replace('\\', '/');
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidDataException or
                  NotSupportedException)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.InvalidResourcePath,
                "The restore path is not a valid folder-project resource.",
                exception);
        }
    }

    private static string ResolveRestoreTarget(
        string projectRoot,
        string repositoryPath)
    {
        try
        {
            return FolderProjectPathPolicy.ResolveFilePath(
                projectRoot,
                repositoryPath);
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidDataException or
                  NotSupportedException)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.InvalidResourcePath,
                "The restore path is not safe.",
                exception);
        }
    }

    private static Commit LookupCommit(
        Repository repository,
        ObjectId objectId)
    {
        var commit = repository.Lookup<Commit>(objectId);
        if (commit == null)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.CommitNotFound,
                "The requested commit does not exist.");
        }

        return commit;
    }

    private static void EnsureRestoreStateSupported(
        Repository repository)
    {
        if (repository.Info.CurrentOperation != CurrentOperation.None ||
            repository.Index.Conflicts.Any())
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedOperationState,
                "Files cannot be restored during a repository operation or conflict.");
        }
        if (File.Exists(Path.Combine(repository.Info.Path, "index.lock")))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryBusy,
                "The folder-project repository is busy.");
        }
    }

    private void EnsureRestoreTargetIsSafe(
        Repository repository,
        string projectRoot,
        string repositoryPath,
        string targetPath,
        bool overwriteWorkingChange)
    {
        var status = repository.RetrieveStatus(
            new StatusOptions
            {
                DetectRenamesInIndex = true,
                DetectRenamesInWorkDir = true,
                IncludeIgnored = true,
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            });
        var targetStatusEntries = status
            .Where(
                entry => StatusTouchesPath(
                    entry,
                    repositoryPath))
            .ToList();
        var hasTargetChange = targetStatusEntries.Count != 0;
        var hasNonExactTargetChange = targetStatusEntries.Any(
            entry => !StatusHasExactPath(
                entry,
                repositoryPath));
        var hasUnsupportedOverwriteChange = targetStatusEntries.Any(
            entry => HasAny(
                entry.State,
                FileStatus.RenamedInIndex |
                FileStatus.RenamedInWorkdir |
                FileStatus.TypeChangeInIndex |
                FileStatus.TypeChangeInWorkdir));
        var unreadableTarget = GetWorkingChanges(repository, projectRoot)
            .Any(
                change =>
                    HasAny(
                        change.Kind,
                        FolderProjectWorkingChangeKind.Unreadable) &&
                    IsSameOrParentPath(
                        change.RepositoryPath,
                        repositoryPath));
        if (Directory.Exists(targetPath) ||
            unreadableTarget ||
            hasNonExactTargetChange ||
            hasUnsupportedOverwriteChange ||
            hasTargetChange && !overwriteWorkingChange)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.WorkingTreeNotClean,
                "The file has working changes and cannot be restored.");
        }
    }

    private void WriteBlobAtomically(
        Blob blob,
        string projectRoot,
        string repositoryPath,
        string targetPath)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDirectory);
        targetPath = ResolveRestoreTarget(projectRoot, repositoryPath);
        var temporaryPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var source = blob.GetContentStream())
            using (var destination = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }

            _platform.MoveFile(
                temporaryPath,
                targetPath,
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

    private static void ValidateBranchName(string name)
    {
        if (!FolderProjectGitRepository.IsValidBranchName(name))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.InvalidBranchName,
                "The local branch name is invalid.");
        }
    }

    private static Branch FindLocalBranch(
        Repository repository,
        string name)
    {
        var branch = repository.Branches.FirstOrDefault(
            candidate =>
                !candidate.IsRemote &&
                candidate.FriendlyName.Equals(
                    name,
                    StringComparison.Ordinal));
        if (branch == null)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.BranchNotFound,
                "The local branch does not exist.");
        }

        return branch;
    }

    private static void EnsureBranchNameAvailable(
        Repository repository,
        string name,
        string? excludedName = null)
    {
        foreach (var branch in repository.Branches.Where(
                     branch => !branch.IsRemote))
        {
            if (excludedName != null &&
                branch.FriendlyName.Equals(
                    excludedName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (HasWindowsReferenceCollision(
                    branch.FriendlyName,
                    name))
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.BranchAlreadyExists,
                    "A conflicting local branch already exists.");
            }
        }
    }

    private static bool HasWindowsReferenceCollision(
        string existingName,
        string candidateName)
    {
        return existingName.Equals(
                   candidateName,
                   StringComparison.OrdinalIgnoreCase) ||
               existingName.StartsWith(
                   candidateName + "/",
                   StringComparison.OrdinalIgnoreCase) ||
               candidateName.StartsWith(
                   existingName + "/",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureSwitchStateSupported(
        Repository repository,
        string projectRoot,
        Branch targetBranch,
        RepositoryStatus workingStatus)
    {
        if (repository.Info.IsHeadDetached ||
            repository.Info.CurrentOperation != CurrentOperation.None ||
            repository.Index.Conflicts.Any())
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedOperationState,
                "Branch switching is unavailable in the current repository state.");
        }
        if (File.Exists(Path.Combine(repository.Info.Path, "index.lock")))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryBusy,
                "The folder-project repository is busy.");
        }
        if (workingStatus.Any(
                entry => HasAny(entry.State, FileStatus.Unreadable)))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.WorkingTreeNotClean,
                "The working tree must be clean before switching branches.");
        }

        EnsureTargetTreeSupported(targetBranch.Tip.Tree);
        EnsureCheckoutPathsReadable(
            repository,
            projectRoot,
            targetBranch);
        EnsureNoIgnoredTargetCollisions(
            repository,
            projectRoot,
            targetBranch);
    }

    private void EnsureCheckoutPathsReadable(
        Repository repository,
        string projectRoot,
        Branch targetBranch)
    {
        var currentTree = repository.Head.Tip?.Tree;
        if (currentTree == null)
            return;

        var paths = repository.Diff.Compare<TreeChanges>(
                currentTree,
                targetBranch.Tip.Tree)
            .Where(change => change.OldExists)
            .Select(change => change.OldPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var repositoryPath in paths)
        {
            var fullPath = FolderProjectPathPolicy.ResolveFilePath(
                projectRoot,
                repositoryPath);
            if (!File.Exists(fullPath))
                continue;

            try
            {
                var attributes = _platform.GetAttributes(fullPath);
                if ((attributes & (FileAttributes.Directory |
                                   FileAttributes.ReparsePoint)) != 0)
                {
                    continue;
                }

                using var stream = _platform.OpenReadForStatus(fullPath);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw UnreadableCheckoutPath(exception);
            }
            catch (IOException exception)
            {
                throw UnreadableCheckoutPath(exception);
            }
        }

        static FolderProjectVersionControlException UnreadableCheckoutPath(
            Exception exception)
        {
            return new FolderProjectVersionControlException(
                FolderProjectVersionControlError.WorkingTreeNotClean,
                "A file changed by the target branch is unreadable.",
                exception);
        }
    }

    private static DateTimeOffset GetBranchCreationTime(
        Repository repository,
        Branch branch)
    {
        var reflogPath = Path.Combine(
            repository.Info.Path,
            "logs",
            branch.CanonicalName.Replace(
                '/',
                Path.DirectorySeparatorChar));
        if (File.Exists(reflogPath))
            return new DateTimeOffset(File.GetCreationTimeUtc(reflogPath));

        return repository.Refs.Log(branch.Reference)
                   .LastOrDefault()?.Committer.When ??
               branch.Tip.Committer.When;
    }

    private static void EnsureTargetTreeSupported(Tree tree)
    {
        foreach (var entry in tree)
        {
            if (entry.TargetType == TreeEntryTargetType.Tree &&
                entry.Target is Tree childTree)
            {
                EnsureTargetTreeSupported(childTree);
                continue;
            }
            if (entry.TargetType == TreeEntryTargetType.Blob &&
                s_regularFileModes.Contains(entry.Mode))
            {
                continue;
            }

            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedCommitPath,
                "The target branch contains a file type that folder projects do not support.");
        }
    }

    private void EnsureNoIgnoredTargetCollisions(
        Repository repository,
        string projectRoot,
        Branch targetBranch)
    {
        var targetPaths = TargetTreePaths.Create(targetBranch.Tip.Tree);
        var ignoredEntries = repository.RetrieveStatus(
            new StatusOptions
            {
                IncludeIgnored = true,
                IncludeUntracked = true,
                RecurseIgnoredDirs = true,
                RecurseUntrackedDirs = true,
            });
        foreach (var entry in ignoredEntries.Where(
                     entry => HasAny(entry.State, FileStatus.Ignored)))
        {
            var ignoredPath = entry.FilePath
                .Replace('\\', '/')
                .TrimEnd('/');
            if (ignoredPath.Length == 0)
                continue;
            if (targetPaths.NonDirectoryPaths.Contains(ignoredPath) ||
                targetPaths.HasNonDirectoryAncestor(ignoredPath))
            {
                ThrowIgnoredTargetCollision();
            }
            if (!targetPaths.ParentDirectories.Contains(ignoredPath))
                continue;

            var fullPath = Path.Combine(
                projectRoot,
                ignoredPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            FileAttributes attributes;
            try
            {
                attributes = _platform.GetAttributes(fullPath);
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
                ThrowIgnoredTargetCollision();
                return;
            }
            catch (IOException)
            {
                ThrowIgnoredTargetCollision();
                return;
            }

            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
            {
                ThrowIgnoredTargetCollision();
            }
        }
    }

    private static void ThrowIgnoredTargetCollision()
    {
        throw new FolderProjectVersionControlException(
            FolderProjectVersionControlError.WorkingTreeNotClean,
            "An ignored file would be overwritten by the target branch.");
    }

    private static FolderProjectBranchInfo ToBranchInfo(
        Repository repository,
        Branch branch)
    {
        return new FolderProjectBranchInfo(
            branch.FriendlyName,
            branch.Tip.Sha,
            !repository.Info.IsHeadDetached &&
            branch.CanonicalName.Equals(
                repository.Head.CanonicalName,
                StringComparison.Ordinal),
            IsPrimaryBranch(repository, branch));
    }

    private static bool IsPrimaryBranch(
        Repository repository,
        Branch branch)
    {
        return GetPrimaryBranch(repository)?.CanonicalName.Equals(
            branch.CanonicalName,
            StringComparison.Ordinal) == true;
    }

    private static bool HasAny(
        FolderProjectWorkingChangeKind value,
        FolderProjectWorkingChangeKind flags)
    {
        return (value & flags) != 0;
    }

    private static bool IsSameOrParentPath(
        string possibleParent,
        string repositoryPath)
    {
        return possibleParent == "." ||
               possibleParent.Equals(
                   repositoryPath,
                   StringComparison.OrdinalIgnoreCase) ||
               repositoryPath.StartsWith(
                   possibleParent.TrimEnd('/') + "/",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool StatusTouchesPath(
        StatusEntry entry,
        string repositoryPath)
    {
        return PathTouches(
                   entry.FilePath,
                   repositoryPath) ||
               PathTouches(
                   entry.HeadToIndexRenameDetails?.OldFilePath,
                   repositoryPath) ||
               PathTouches(
                   entry.HeadToIndexRenameDetails?.NewFilePath,
                   repositoryPath) ||
               PathTouches(
                   entry.IndexToWorkDirRenameDetails?.OldFilePath,
                   repositoryPath) ||
               PathTouches(
                   entry.IndexToWorkDirRenameDetails?.NewFilePath,
                   repositoryPath);
    }

    private static bool PathTouches(
        string? candidatePath,
        string repositoryPath)
    {
        if (candidatePath == null)
            return false;

        var normalized = candidatePath.Replace('\\', '/');
        return normalized.Equals(
                   repositoryPath,
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(
                   repositoryPath.TrimEnd('/') + "/",
                   StringComparison.OrdinalIgnoreCase) ||
               repositoryPath.StartsWith(
                   normalized.TrimEnd('/') + "/",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool StatusHasExactPath(
        StatusEntry entry,
        string repositoryPath)
    {
        return PathEquals(entry.FilePath, repositoryPath) ||
               PathEquals(
                   entry.HeadToIndexRenameDetails?.OldFilePath,
                   repositoryPath) ||
               PathEquals(
                   entry.HeadToIndexRenameDetails?.NewFilePath,
                   repositoryPath) ||
               PathEquals(
                   entry.IndexToWorkDirRenameDetails?.OldFilePath,
                   repositoryPath) ||
               PathEquals(
                   entry.IndexToWorkDirRenameDetails?.NewFilePath,
                   repositoryPath);
    }

    private static bool PathEquals(
        string? candidatePath,
        string repositoryPath)
    {
        return candidatePath?.Replace('\\', '/').Equals(
            repositoryPath,
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed record TargetTreePaths(
        IReadOnlySet<string> NonDirectoryPaths,
        IReadOnlySet<string> ParentDirectories)
    {
        public static TargetTreePaths Create(Tree tree)
        {
            var nonDirectoryPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var parentDirectories = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            AddTreeEntries(
                tree,
                "",
                nonDirectoryPaths,
                parentDirectories);
            return new TargetTreePaths(
                nonDirectoryPaths,
                parentDirectories);
        }

        public bool HasNonDirectoryAncestor(string repositoryPath)
        {
            var separatorIndex = repositoryPath.LastIndexOf('/');
            while (separatorIndex > 0)
            {
                if (NonDirectoryPaths.Contains(
                        repositoryPath[..separatorIndex]))
                {
                    return true;
                }

                separatorIndex = repositoryPath.LastIndexOf(
                    '/',
                    separatorIndex - 1);
            }

            return false;
        }

        private static void AddTreeEntries(
            Tree tree,
            string parentPath,
            ISet<string> nonDirectoryPaths,
            ISet<string> parentDirectories)
        {
            foreach (var entry in tree)
            {
                var repositoryPath = parentPath.Length == 0
                    ? entry.Name
                    : $"{parentPath}/{entry.Name}";
                if (entry.TargetType == TreeEntryTargetType.Tree &&
                    entry.Target is Tree childTree)
                {
                    AddTreeEntries(
                        childTree,
                        repositoryPath,
                        nonDirectoryPaths,
                        parentDirectories);
                    continue;
                }

                nonDirectoryPaths.Add(repositoryPath);
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
        }
    }
}
