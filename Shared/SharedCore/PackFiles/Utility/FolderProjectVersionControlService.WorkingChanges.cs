using LibGit2Sharp;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.PackFiles.Utility;

public sealed partial class FolderProjectVersionControlService
{
    public void StageChanges(
        string projectRoot,
        IReadOnlyList<string> relativePaths)
    {
        UpdateStagingArea(projectRoot, relativePaths, stage: true);
    }

    public void UnstageChanges(
        string projectRoot,
        IReadOnlyList<string> relativePaths)
    {
        UpdateStagingArea(projectRoot, relativePaths, stage: false);
    }

    public void DiscardChanges(
        string projectRoot,
        IReadOnlyList<string> relativePaths)
    {
        var requestedPaths = ValidateChangePaths(relativePaths);
        Execute(
            () =>
            {
                var root = Path.GetFullPath(projectRoot);
                using var repository = OpenRepository(root);
                EnsureCommitStateSupported(repository);
                var head = repository.Head.Tip;
                if (head == null)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitNotFound,
                        "The repository has no current commit.");
                }

                var status = RetrieveWorkingStatus(repository).ToList();
                var affectedPaths = GetAffectedChangePaths(
                    status,
                    requestedPaths);
                foreach (var path in affectedPaths)
                {
                    EnsureNoReparsePoints(
                        root,
                        ResolveRestoreTarget(root, path),
                        includeLeaf: true);
                }
                var snapshots = affectedPaths
                    .Select(
                        path => PolicyFileSnapshot.Capture(
                            ResolveRestoreTarget(root, path)))
                    .ToList();
                var indexSnapshot = GitIndexSnapshot.Capture(
                    Path.Combine(repository.Info.Path, "index"));
                try
                {
                    var trackedPaths = affectedPaths
                        .Where(path => head.Tree[path] != null)
                        .ToList();
                    if (trackedPaths.Count != 0)
                    {
                        repository.CheckoutPaths(
                            head.Sha,
                            trackedPaths,
                            new CheckoutOptions
                            {
                                CheckoutModifiers = CheckoutModifiers.Force,
                            });
                    }

                    var addedPaths = affectedPaths
                        .Except(trackedPaths, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (addedPaths.Count != 0)
                    {
                        Commands.Unstage(
                            repository,
                            addedPaths,
                            new ExplicitPathsOptions
                            {
                                ShouldFailOnUnmatchedPath = false,
                            });
                        foreach (var path in addedPaths)
                        {
                            var fullPath = ResolveRestoreTarget(root, path);
                            if (Directory.Exists(fullPath))
                            {
                                throw new FolderProjectVersionControlException(
                                    FolderProjectVersionControlError.InvalidResourcePath,
                                    "Only files can be discarded.");
                            }
                            if (!File.Exists(fullPath))
                                continue;

                            File.SetAttributes(fullPath, FileAttributes.Normal);
                            _platform.DeleteFile(fullPath);
                        }
                    }
                }
                catch (Exception failure)
                {
                    var rollbackFailures = new List<Exception>();
                    foreach (var snapshot in snapshots)
                    {
                        try
                        {
                            snapshot.Restore();
                        }
                        catch (Exception exception)
                        {
                            rollbackFailures.Add(exception);
                        }
                    }
                    try
                    {
                        indexSnapshot.Restore(_platform);
                    }
                    catch (Exception exception)
                    {
                        rollbackFailures.Add(exception);
                    }

                    if (rollbackFailures.Count != 0)
                    {
                        throw new AggregateException(
                            "Discard failed and rollback was incomplete.",
                            [failure, .. rollbackFailures]);
                    }
                    throw;
                }

                return true;
            });
    }

    public FolderProjectCommitSummary CommitStaged(
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
                if (!RetrieveWorkingStatus(repository).Any(
                        entry => HasStagedChanges(entry.State)))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.NothingToCommit,
                        "The folder project has no staged changes.");
                }

                var commit = _platform.Commit(
                    repository,
                    message.Trim(),
                    CreateSignature(identity));
                return ToSummary(commit);
            });
    }

    public void UndoLatestCommit(
        string projectRoot,
        string commitId,
        FolderProjectCommitUndoMode mode)
    {
        if (mode is not FolderProjectCommitUndoMode.KeepChanges and
            not FolderProjectCommitUndoMode.DiscardChanges)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The commit undo mode is not supported.");
        }

        var objectId = ParseFullCommitId(commitId);
        Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                var statusBefore = RetrieveWorkingStatus(repository);
                if (statusBefore.IsDirty)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.WorkingTreeNotClean,
                        "A commit can only be undone from a clean working tree.");
                }

                var commit = LookupCommit(repository, objectId);
                if (repository.Info.IsHeadDetached)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.UnsupportedOperationState,
                        "The latest commit cannot be undone from a detached HEAD.");
                }
                if (repository.Head.Tip?.Id != commit.Id)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitIsNotLatest,
                        "Only the current branch's latest commit can be undone.");
                }
                if (commit.Parents.Count() != 1)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitCannotBeUndone,
                        "Only a latest commit with one parent can be undone here.");
                }

                var parent = commit.Parents.Single();
                try
                {
                    if (mode == FolderProjectCommitUndoMode.KeepChanges)
                    {
                        repository.Reset(ResetMode.Mixed, parent);
                    }
                    else
                    {
                        _platform.Reset(
                            repository,
                            parent,
                            new CheckoutOptions
                            {
                                CheckoutModifiers = CheckoutModifiers.Force,
                            });
                    }
                }
                catch (Exception failure)
                {
                    try
                    {
                        _platform.Reset(
                            repository,
                            commit,
                            new CheckoutOptions
                            {
                                CheckoutModifiers = CheckoutModifiers.Force,
                            });
                    }
                    catch (Exception rollbackFailure)
                    {
                        throw new AggregateException(
                            "Undo failed and rollback was incomplete.",
                            failure,
                            rollbackFailure);
                    }
                    throw;
                }

                return true;
            });
    }

    private void UpdateStagingArea(
        string projectRoot,
        IReadOnlyList<string> relativePaths,
        bool stage)
    {
        var requestedPaths = ValidateChangePaths(relativePaths);
        Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                var affectedPaths = GetAffectedChangePaths(
                    RetrieveWorkingStatus(repository).ToList(),
                    requestedPaths);
                if (stage)
                {
                    Commands.Stage(repository, affectedPaths);
                }
                else
                {
                    Commands.Unstage(
                        repository,
                        affectedPaths,
                        new ExplicitPathsOptions
                        {
                            ShouldFailOnUnmatchedPath = false,
                        });
                }
                return true;
            });
    }

    private static IReadOnlyList<string> ValidateChangePaths(
        IReadOnlyList<string> relativePaths)
    {
        if (relativePaths.Count == 0)
            throw new ArgumentException("At least one path is required.");

        return relativePaths
            .Select(ValidateWorkingChangePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetAffectedChangePaths(
        IReadOnlyList<StatusEntry> status,
        IReadOnlyList<string> requestedPaths)
    {
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requestedPath in requestedPaths)
        {
            var matches = status
                .Where(entry => StatusTouchesPath(entry, requestedPath))
                .ToList();
            if (matches.Count == 0)
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.NothingToCommit,
                    "The selected path has no working changes.");
            }
            foreach (var entry in matches)
            {
                foreach (var path in GetStatusPaths(entry))
                    affected.Add(ValidateWorkingChangePath(path));
            }
        }
        return affected.ToList();
    }

    private static string ValidateWorkingChangePath(string relativePath)
    {
        try
        {
            var normalized = FolderProjectPathPolicy
                .NormalizeRelativePath(relativePath);
            if (FolderProjectPathPolicy.IsMetadataDirectoryPath(normalized))
            {
                throw new InvalidDataException(
                    "The path is reserved for repository metadata.");
            }

            return normalized.Replace('\\', '/');
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidDataException or
                  NotSupportedException)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.InvalidResourcePath,
                "The selected working-change path is invalid.",
                exception);
        }
    }

    private static IEnumerable<string> GetStatusPaths(StatusEntry entry)
    {
        yield return entry.FilePath.Replace('\\', '/');
        if (entry.HeadToIndexRenameDetails is { } indexRename)
        {
            yield return indexRename.OldFilePath.Replace('\\', '/');
            yield return indexRename.NewFilePath.Replace('\\', '/');
        }
        if (entry.IndexToWorkDirRenameDetails is { } workingRename)
        {
            yield return workingRename.OldFilePath.Replace('\\', '/');
            yield return workingRename.NewFilePath.Replace('\\', '/');
        }
    }

    private static bool HasStagedChanges(FileStatus status)
    {
        return HasAny(
            status,
            FileStatus.NewInIndex |
            FileStatus.ModifiedInIndex |
            FileStatus.DeletedFromIndex |
            FileStatus.RenamedInIndex |
             FileStatus.TypeChangeInIndex);
    }

    private static bool HasUnstagedChanges(FileStatus status)
    {
        return HasAny(
            status,
            FileStatus.NewInWorkdir |
            FileStatus.ModifiedInWorkdir |
            FileStatus.DeletedFromWorkdir |
            FileStatus.RenamedInWorkdir |
            FileStatus.TypeChangeInWorkdir);
    }
}
