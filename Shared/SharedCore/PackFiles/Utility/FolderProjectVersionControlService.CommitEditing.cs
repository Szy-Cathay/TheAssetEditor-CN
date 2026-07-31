using LibGit2Sharp;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.PackFiles.Utility;

public sealed partial class FolderProjectVersionControlService
{
    public FolderProjectCommitEditSession EditLatestCommitChanges(
        string projectRoot,
        string commitId,
        IReadOnlyList<string> relativePaths,
        FolderProjectCommitChangeEditMode mode)
    {
        if (mode is not FolderProjectCommitChangeEditMode.Discard and
            not FolderProjectCommitChangeEditMode.StageForEdit)
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
