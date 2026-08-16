using LibGit2Sharp;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.PackFiles.Utility;

internal sealed partial class FolderProjectVersionControlService
{
    public FolderProjectRestorePointDeleteRollback BeginDeleteRestorePoint(
        string projectRoot,
        string commitId)
    {
        var objectId = ParseFullCommitId(commitId);
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                if (repository.Info.IsHeadDetached)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError
                            .UnsupportedOperationState,
                        "A restore point cannot be deleted from a detached HEAD.");
                }

                var target = LookupCommit(repository, objectId);
                var originalHead = repository.Head.Tip ??
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitNotFound,
                        "The repository has no current restore point.");
                if (target.Parents.Count() != 1)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError
                            .CommitCannotBeDeleted,
                        "The initial restore point cannot be deleted.");
                }

                var descendants = new List<Commit>();
                var current = originalHead;
                while (current.Id != target.Id)
                {
                    if (current.Parents.Count() != 1)
                    {
                        throw new FolderProjectVersionControlException(
                            FolderProjectVersionControlError
                                .CommitCannotBeDeleted,
                            "Only a restore point in the current linear history can be deleted.");
                    }

                    descendants.Add(current);
                    current = current.Parents.Single();
                }

                var rewrittenHead = target.Parents.Single();
                foreach (var descendant in descendants.AsEnumerable().Reverse())
                {
                    rewrittenHead = repository.ObjectDatabase.CreateCommit(
                        descendant.Author,
                        descendant.Committer,
                        descendant.Message,
                        descendant.Tree,
                        [rewrittenHead],
                        prettifyMessage: false);
                }

                var deletesCurrent = target.Id == originalHead.Id;
                var indexSnapshot = deletesCurrent
                    ? GitIndexSnapshot.Capture(
                        Path.Combine(repository.Info.Path, "index"))
                    : null;
                var headUpdated = false;
                try
                {
                    repository.Refs.UpdateTarget(
                        repository.Refs[repository.Head.CanonicalName],
                        rewrittenHead.Sha);
                    headUpdated = true;
                    if (deletesCurrent)
                        _platform.ResetMixed(repository, rewrittenHead);
                }
                catch (Exception failure)
                {
                    var rollbackFailures = new List<Exception>();
                    if (headUpdated)
                    {
                        try
                        {
                            repository.Refs.UpdateTarget(
                                repository.Refs[
                                    repository.Head.CanonicalName],
                                originalHead.Sha);
                        }
                        catch (Exception rollbackFailure)
                        {
                            rollbackFailures.Add(rollbackFailure);
                        }
                    }
                    if (indexSnapshot != null)
                    {
                        try
                        {
                            indexSnapshot.Restore(_platform);
                        }
                        catch (Exception rollbackFailure)
                        {
                            rollbackFailures.Add(rollbackFailure);
                        }
                    }

                    if (rollbackFailures.Count != 0)
                    {
                        throw new FolderProjectVersionControlException(
                            FolderProjectVersionControlError.RepositoryFailure,
                            "Restore-point deletion failed and rollback was incomplete.",
                            new AggregateException(
                                [failure, .. rollbackFailures]),
                            isRollbackIncomplete: true);
                    }
                    throw;
                }

                return new FolderProjectRestorePointDeleteRollback(
                    originalHead.Sha,
                    rewrittenHead.Sha,
                    indexSnapshot == null
                        ? null
                        : new FolderProjectIndexSnapshot(
                            indexSnapshot.Exists,
                            indexSnapshot.Bytes,
                            indexSnapshot.Attributes));
            });
    }

    public void CompleteDeleteRestorePoint(
        FolderProjectRestorePointDeleteRollback rollback)
    {
        ArgumentNullException.ThrowIfNull(rollback);
    }

    public void RollbackDeleteRestorePoint(
        string projectRoot,
        FolderProjectRestorePointDeleteRollback rollback)
    {
        ArgumentNullException.ThrowIfNull(rollback);
        Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                if (repository.Head.Tip?.Sha != rollback.RewrittenCommitId)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError
                            .CommitCannotBeDeleted,
                        "The rewritten restore-point history is no longer current.");
                }

                try
                {
                    var originalHead = LookupCommit(
                        repository,
                        ParseFullCommitId(rollback.OriginalCommitId));
                    repository.Refs.UpdateTarget(
                        repository.Refs[repository.Head.CanonicalName],
                        originalHead.Sha);
                    if (rollback.Index != null)
                    {
                        new GitIndexSnapshot(
                                Path.Combine(repository.Info.Path, "index"),
                                rollback.Index.Existed,
                                rollback.Index.Bytes,
                                rollback.Index.Attributes)
                            .Restore(_platform);
                    }
                }
                catch (Exception exception)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.RepositoryFailure,
                        "Restore-point deletion rollback was incomplete.",
                        exception,
                        isRollbackIncomplete: true);
                }
                return true;
            });
    }

    public FolderProjectCommitEditSession EditLatestCommitChanges(
        string projectRoot,
        string commitId,
        IReadOnlyList<string> relativePaths,
        FolderProjectCommitChangeEditMode mode)
    {
        if (mode is not FolderProjectCommitChangeEditMode.Discard and
            not FolderProjectCommitChangeEditMode.StageForEdit and
            not FolderProjectCommitChangeEditMode.KeepChanges)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The commit change edit mode is not supported.");
        }

        var objectId = ParseFullCommitId(commitId);
        var requestedPaths = ValidateChangePaths(relativePaths);
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                var commit = LookupCommit(repository, objectId);
                EnsureLatestEditableCommit(repository, commit);
                if (RetrieveWorkingStatus(repository).IsDirty)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.WorkingTreeNotClean,
                        "Commit files can only be edited from a clean working tree.");
                }

                var parent = commit.Parents.Single();
                var changes = repository.Diff.Compare<TreeChanges>(
                    parent.Tree,
                    commit.Tree,
                    new CompareOptions
                    {
                        Similarity = SimilarityOptions.Renames,
                    });
                var selectedChanges = changes
                    .Where(
                        change => requestedPaths.Contains(
                            change.Path,
                            StringComparer.OrdinalIgnoreCase))
                    .ToList();
                if (selectedChanges.Count != requestedPaths.Count ||
                    selectedChanges.Any(
                        change => change.Status is not ChangeKind.Added and
                                  not ChangeKind.Modified and
                                  not ChangeKind.Deleted and
                                  not ChangeKind.Renamed))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.UnsupportedCommitPath,
                        "Every selected path must be an editable file change in the latest commit.");
                }

                var definition = TreeDefinition.From(commit.Tree);
                var affectedPaths = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var change in selectedChanges)
                {
                    definition.Remove(change.Path);
                    affectedPaths.Add(change.Path);
                    var parentPath = change.Status == ChangeKind.Renamed
                        ? change.OldPath
                        : change.Path;
                    if (!string.IsNullOrWhiteSpace(parentPath))
                    {
                        affectedPaths.Add(parentPath);
                        var parentEntry = parent.Tree[parentPath];
                        if (parentEntry?.Target is Blob blob)
                        {
                            definition.Add(
                                parentPath,
                                blob,
                                parentEntry.Mode);
                        }
                    }
                }

                var rewrittenTree = repository.ObjectDatabase.CreateTree(
                    definition);
                var rewrittenHead = rewrittenTree.Id == parent.Tree.Id
                    ? parent
                    : repository.ObjectDatabase.CreateCommit(
                        commit.Author,
                        commit.Committer,
                        commit.Message,
                        rewrittenTree,
                        [parent],
                        prettifyMessage: false);
                var headUpdated = false;
                try
                {
                    repository.Refs.UpdateTarget(
                        repository.Refs[repository.Head.CanonicalName],
                        rewrittenHead.Sha);
                    headUpdated = true;
                    if (mode == FolderProjectCommitChangeEditMode.Discard)
                    {
                        _platform.Reset(
                            repository,
                            rewrittenHead,
                            new CheckoutOptions
                            {
                                CheckoutModifiers = CheckoutModifiers.Force,
                            });
                    }
                    else if (mode ==
                             FolderProjectCommitChangeEditMode.KeepChanges)
                    {
                        repository.Reset(ResetMode.Mixed, rewrittenHead);
                    }
                }
                catch (Exception failure)
                {
                    if (!headUpdated)
                        throw;

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
                            "Commit file editing failed and rollback was incomplete.",
                            failure,
                            rollbackFailure);
                    }
                    throw;
                }

                return new FolderProjectCommitEditSession(
                    commit.Sha,
                    rewrittenHead.Sha,
                    affectedPaths.OrderBy(path => path, StringComparer.Ordinal)
                        .ToList(),
                    rewrittenHead.Id != parent.Id);
            });
    }

    public FolderProjectCommitSummary RevertCommitChanges(
        string projectRoot,
        string commitId,
        IReadOnlyList<string> relativePaths)
    {
        var objectId = ParseFullCommitId(commitId);
        var requestedPaths = ValidateChangePaths(relativePaths);
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                if (repository.Info.IsHeadDetached)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError
                            .UnsupportedOperationState,
                        "Commit files cannot be reverted from a detached HEAD.");
                }
                if (RetrieveWorkingStatus(repository).IsDirty)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.WorkingTreeNotClean,
                        "A clean working tree is required before reverting commit files.");
                }

                var commit = LookupCommit(repository, objectId);
                var originalHead = repository.Head.Tip!;
                if (!IsAncestor(repository, commit, originalHead) ||
                    commit.Parents.Count() != 1)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitCannotBeUndone,
                        "Only files from an ordinary commit in the current branch can be reverted.");
                }

                var parent = commit.Parents.Single();
                var changes = repository.Diff.Compare<TreeChanges>(
                    parent.Tree,
                    commit.Tree,
                    new CompareOptions
                    {
                        Similarity = SimilarityOptions.Renames,
                    });
                var selectedChanges = changes
                    .Where(
                        change => requestedPaths.Contains(
                            change.Path,
                            StringComparer.OrdinalIgnoreCase))
                    .ToList();
                if (selectedChanges.Count != requestedPaths.Count ||
                    selectedChanges.Any(
                        change => change.Status is not ChangeKind.Added and
                                  not ChangeKind.Modified and
                                  not ChangeKind.Deleted and
                                  not ChangeKind.Renamed))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.UnsupportedCommitPath,
                        "Every selected path must be a reversible file change.");
                }

                var definition = TreeDefinition.From(originalHead.Tree);
                foreach (var change in selectedChanges)
                {
                    EnsureCommitSideIsCurrent(
                        originalHead.Tree,
                        commit.Tree,
                        change);
                    definition.Remove(change.Path);
                    var parentPath = change.Status == ChangeKind.Renamed
                        ? change.OldPath
                        : change.Path;
                    if (change.Status == ChangeKind.Added ||
                        string.IsNullOrWhiteSpace(parentPath))
                    {
                        continue;
                    }

                    var parentEntry = parent.Tree[parentPath];
                    if (parentEntry?.Target is not Blob parentBlob)
                    {
                        throw new FolderProjectVersionControlException(
                            FolderProjectVersionControlError
                                .UnsupportedCommitPath,
                            "The selected path is not an ordinary file change.");
                    }
                    definition.Add(
                        parentPath,
                        parentBlob,
                        parentEntry.Mode);
                }

                var revertedTree = repository.ObjectDatabase.CreateTree(
                    definition);
                if (revertedTree.Id == originalHead.Tree.Id)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitCannotBeUndone,
                        "The selected file changes have already been reverted.");
                }

                var identity = ReadLocalIdentity(repository);
                ValidateIdentity(identity);
                var signature = CreateSignature(identity);
                var reverted = repository.ObjectDatabase.CreateCommit(
                    signature,
                    signature,
                    $"Revert \"{commit.MessageShort}\" (selected files)",
                    revertedTree,
                    [originalHead],
                    prettifyMessage: false);
                var headUpdated = false;
                try
                {
                    repository.Refs.UpdateTarget(
                        repository.Refs[repository.Head.CanonicalName],
                        reverted.Sha);
                    headUpdated = true;
                    _platform.Reset(
                        repository,
                        reverted,
                        new CheckoutOptions
                        {
                            CheckoutModifiers = CheckoutModifiers.Force,
                        });
                }
                catch (Exception failure)
                {
                    if (!headUpdated)
                        throw;

                    RestoreHeadAfterFailedHistoryOperation(
                        repository,
                        originalHead,
                        failure,
                        "Selected-file revert failed and rollback was incomplete.");
                }

                return ToSummary(reverted);
            });
    }

    private static void EnsureCommitSideIsCurrent(
        Tree currentTree,
        Tree commitTree,
        TreeEntryChanges change)
    {
        var currentEntry = currentTree[change.Path];
        var commitEntry = commitTree[change.Path];
        var matchesCommitSide = change.Status == ChangeKind.Deleted
            ? currentEntry == null
            : TreeEntriesMatch(currentEntry, commitEntry);
        if (change.Status == ChangeKind.Renamed)
        {
            matchesCommitSide = matchesCommitSide &&
                                currentTree[change.OldPath] == null;
        }
        if (matchesCommitSide)
            return;

        throw new FolderProjectVersionControlException(
            FolderProjectVersionControlError.CommitCannotBeUndone,
            "The selected file changes could not be reverted cleanly because the files changed later.");
    }

    private static bool TreeEntriesMatch(
        TreeEntry? currentEntry,
        TreeEntry? commitEntry)
    {
        return currentEntry != null &&
               commitEntry != null &&
               currentEntry.Mode == commitEntry.Mode &&
               currentEntry.Target.Id == commitEntry.Target.Id;
    }

    public FolderProjectCommitSummary CompleteLatestCommitEdit(
        string projectRoot,
        FolderProjectCommitEditSession editSession)
    {
        ArgumentNullException.ThrowIfNull(editSession);
        var originalId = ParseFullCommitId(editSession.OriginalCommitId);
        var expectedHeadId = ParseFullCommitId(
            editSession.ExpectedHeadCommitId);
        var editedPaths = ValidateChangePaths(editSession.RepositoryPaths);
        if (!editSession.CanReturnToOriginalCommit)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.CommitNotFound,
                "The original commit no longer exists after all of its changes were removed.");
        }
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                if (repository.Info.IsHeadDetached ||
                    repository.Head.Tip?.Id != expectedHeadId)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitIsNotLatest,
                        "The commit being edited is no longer the current branch head.");
                }

                var original = LookupCommit(repository, originalId);
                if (original.Parents.Count() != 1)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.CommitCannotBeUndone,
                        "Only a latest commit with one parent can be edited here.");
                }

                var status = RetrieveWorkingStatus(repository).ToList();
                var staged = status
                    .Where(change => HasStagedChanges(change.State))
                    .ToList();
                if (staged.Count == 0 ||
                    staged.Any(
                        change => !editedPaths.Contains(
                            change.FilePath,
                            StringComparer.OrdinalIgnoreCase)) ||
                    staged.Any(
                        change => HasUnstagedChanges(change.State)))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.WorkingTreeNotClean,
                        "Only the files being returned to the commit may be staged.");
                }

                var tree = repository.Index.WriteToTree();

                var completed = repository.ObjectDatabase.CreateCommit(
                    original.Author,
                    original.Committer,
                    original.Message,
                    tree,
                    [original.Parents.Single()],
                    prettifyMessage: false);
                repository.Refs.UpdateTarget(
                    repository.Refs[repository.Head.CanonicalName],
                    completed.Sha);
                return ToSummary(completed);
            });
    }

    private static void EnsureLatestEditableCommit(
        Repository repository,
        Commit commit)
    {
        if (repository.Info.IsHeadDetached)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedOperationState,
                "Commit files cannot be edited from a detached HEAD.");
        }
        if (repository.Head.Tip?.Id != commit.Id)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.CommitIsNotLatest,
                "Only the current branch's latest commit can be edited.");
        }
        if (commit.Parents.Count() != 1)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.CommitCannotBeUndone,
                "Only a latest commit with one parent can be edited here.");
        }
    }
}
