using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
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

                var status = RetrieveWorkingStatus(
                    repository,
                    requestedPaths).ToList();
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
                var trackedPaths = affectedPaths
                    .Where(path => head.Tree[path] != null)
                    .ToList();
                foreach (var path in trackedPaths)
                {
                    var entry = head.Tree[path];
                    if (entry.TargetType != TreeEntryTargetType.Blob ||
                        !s_regularFileModes.Contains(entry.Mode) ||
                        entry.Target is not Blob)
                    {
                        throw new FolderProjectVersionControlException(
                            FolderProjectVersionControlError
                                .UnsupportedCommitPath,
                            "Only regular files can be discarded.");
                    }
                }
                var addedPaths = affectedPaths
                    .Except(trackedPaths, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var snapshots = trackedPaths
                    .Select(
                        path => PolicyFileSnapshot.Capture(
                            ResolveRestoreTarget(root, path)))
                    .ToList();
                var indexSnapshot = GitIndexSnapshot.Capture(
                    Path.Combine(repository.Info.Path, "index"));
                var stagedAddedFiles = new List<DiscardedFileBackup>();
                var discardStagingPath = Path.Combine(
                    repository.Info.Path,
                    $"ae-discard-{Guid.NewGuid():N}");
                var completed = false;
                try
                {
                    if (trackedPaths.Count != 0)
                    {
                        Commands.Unstage(
                            repository,
                            trackedPaths,
                            new ExplicitPathsOptions
                            {
                                ShouldFailOnUnmatchedPath = false,
                            });
                        RestoreTrackedFilesInParallel(
                            root,
                            head.Sha,
                            trackedPaths);
                    }

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

                            Directory.CreateDirectory(discardStagingPath);
                            var stagedPath = Path.Combine(
                                discardStagingPath,
                                $"{stagedAddedFiles.Count:x8}.tmp");
                            _platform.MoveFile(
                                fullPath,
                                stagedPath,
                                overwrite: false);
                            stagedAddedFiles.Add(new DiscardedFileBackup(
                                fullPath,
                                stagedPath));
                        }
                    }

                    completed = true;
                }
                catch (Exception failure)
                {
                    var rollbackFailures = new List<Exception>();
                    for (var index = stagedAddedFiles.Count - 1;
                         index >= 0;
                         index--)
                    {
                        try
                        {
                            var backup = stagedAddedFiles[index];
                            Directory.CreateDirectory(
                                Path.GetDirectoryName(
                                    backup.OriginalPath)!);
                            _platform.MoveFile(
                                backup.StagedPath,
                                backup.OriginalPath,
                                overwrite: true);
                        }
                        catch (Exception exception)
                        {
                            rollbackFailures.Add(exception);
                        }
                    }
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
                finally
                {
                    if (completed)
                    {
                        TryDeleteDiscardStaging(
                            discardStagingPath,
                            stagedAddedFiles);
                    }
                    else if (Directory.Exists(discardStagingPath) &&
                             stagedAddedFiles.All(
                                 backup =>
                                     !File.Exists(backup.StagedPath)))
                    {
                        TryDeleteDiscardStaging(
                            discardStagingPath,
                            stagedAddedFiles);
                    }
                }

                return true;
            });
    }

    private static void TryDeleteDiscardStaging(
        string stagingPath,
        IReadOnlyList<DiscardedFileBackup> backups)
    {
        if (!Directory.Exists(stagingPath))
            return;

        try
        {
            foreach (var backup in backups)
            {
                if (File.Exists(backup.StagedPath))
                {
                    File.SetAttributes(
                        backup.StagedPath,
                        FileAttributes.Normal);
                }
            }
            Directory.Delete(stagingPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void RestoreTrackedFilesInParallel(
        string projectRoot,
        string commitId,
        IReadOnlyList<string> trackedPaths)
    {
        try
        {
            Parallel.ForEach(
                Partitioner.Create(0, trackedPaths.Count),
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                range =>
                {
                    using var repository = OpenRepository(projectRoot);
                    var commit = repository.Lookup<Commit>(commitId) ??
                        throw new FolderProjectVersionControlException(
                            FolderProjectVersionControlError.CommitNotFound,
                            "The current commit could not be loaded.");
                    for (var index = range.Item1;
                         index < range.Item2;
                         index++)
                    {
                        var repositoryPath = trackedPaths[index];
                        var entry = commit.Tree[repositoryPath];
                        var blob = entry?.Target as Blob ??
                            throw new FolderProjectVersionControlException(
                                FolderProjectVersionControlError
                                    .UnsupportedCommitPath,
                                "Only regular files can be discarded.");
                        WriteDiscardBlobAtomically(
                            blob,
                            projectRoot,
                            repositoryPath);
                    }
                });
        }
        catch (AggregateException failure)
        {
            var flattened = failure.Flatten();
            if (flattened.InnerExceptions.Count == 1)
            {
                ExceptionDispatchInfo.Capture(
                    flattened.InnerExceptions[0]).Throw();
            }
            throw;
        }
    }

    private void WriteDiscardBlobAtomically(
        Blob blob,
        string projectRoot,
        string repositoryPath)
    {
        var targetPath = ResolveRestoreTarget(
            projectRoot,
            repositoryPath);
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDirectory);
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
                       FileShare.None,
                       128 * 1024,
                       FileOptions.SequentialScan))
            {
                source.CopyTo(destination);
            }

            if (File.Exists(targetPath))
                File.SetAttributes(targetPath, FileAttributes.Normal);
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

    private sealed record DiscardedFileBackup(
        string OriginalPath,
        string StagedPath);

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
                Commit commit;
                try
                {
                    commit = _platform.Commit(
                        repository,
                        message.Trim(),
                        CreateSignature(identity));
                }
                catch (EmptyCommitException exception)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.NothingToCommit,
                        "The folder project has no staged changes.",
                        exception);
                }
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

    public void ResetToCommit(
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
                "The commit reset mode is not supported.");
        }

        var objectId = ParseFullCommitId(commitId);
        Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                if (RetrieveWorkingStatus(repository).IsDirty)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.WorkingTreeNotClean,
                        "A clean working tree is required before resetting a commit.");
                }

                var target = LookupCommit(repository, objectId);
                var originalHead = repository.Head.Tip!;
                if (!IsAncestor(repository, target, originalHead))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitCannotBeUndone,
                        "The reset target is not in the current branch history.");
                }
                if (target.Id == originalHead.Id)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitCannotBeUndone,
                        "The selected commit is already the current commit.");
                }

                try
                {
                    if (mode == FolderProjectCommitUndoMode.KeepChanges)
                    {
                        repository.Reset(ResetMode.Mixed, target);
                    }
                    else
                    {
                        _platform.Reset(
                            repository,
                            target,
                            new CheckoutOptions
                            {
                                CheckoutModifiers = CheckoutModifiers.Force,
                            });
                    }
                }
                catch (Exception failure)
                {
                    RestoreHeadAfterFailedHistoryOperation(
                        repository,
                        originalHead,
                        failure,
                        "Reset failed and rollback was incomplete.");
                }

                return true;
            });
    }

    public FolderProjectCommitSummary RevertCommit(
        string projectRoot,
        string commitId)
    {
        var objectId = ParseFullCommitId(commitId);
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                if (RetrieveWorkingStatus(repository).IsDirty)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.WorkingTreeNotClean,
                        "A clean working tree is required before reverting a commit.");
                }

                var commit = LookupCommit(repository, objectId);
                var originalHead = repository.Head.Tip!;
                if (!IsAncestor(repository, commit, originalHead) ||
                    commit.Parents.Count() != 1)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitCannotBeUndone,
                        "Only an ordinary commit in the current branch can be reverted.");
                }

                var identity = ReadLocalIdentity(repository);
                ValidateIdentity(identity);
                try
                {
                    var result = repository.Revert(
                        commit,
                        CreateSignature(identity),
                        new RevertOptions { CommitOnSuccess = true });
                    if (result.Status != RevertStatus.Reverted ||
                        result.Commit == null)
                    {
                        RestoreHeadAfterFailedHistoryOperation(
                            repository,
                            originalHead,
                            new FolderProjectVersionControlException(
                                FolderProjectVersionControlError
                                    .CommitCannotBeUndone,
                                "The selected commit could not be reverted cleanly."),
                            "Revert failed and rollback was incomplete.");
                    }

                    return ToSummary(result.Commit!);
                }
                catch (Exception failure)
                {
                    if (repository.Head.Tip?.Id != originalHead.Id ||
                        RetrieveWorkingStatus(repository).IsDirty ||
                        repository.Info.CurrentOperation !=
                        CurrentOperation.None)
                    {
                        RestoreHeadAfterFailedHistoryOperation(
                            repository,
                            originalHead,
                            failure,
                            "Revert failed and rollback was incomplete.");
                    }
                    throw;
                }
            });
    }

    private void RestoreHeadAfterFailedHistoryOperation(
        Repository repository,
        Commit originalHead,
        Exception failure,
        string aggregateMessage)
    {
        try
        {
            _platform.Reset(
                repository,
                originalHead,
                new CheckoutOptions
                {
                    CheckoutModifiers = CheckoutModifiers.Force,
                });
        }
        catch (Exception rollbackFailure)
        {
            throw new AggregateException(
                aggregateMessage,
                failure,
                rollbackFailure);
        }

        throw failure;
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
                    RetrieveWorkingStatus(
                        repository,
                        requestedPaths).ToList(),
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
