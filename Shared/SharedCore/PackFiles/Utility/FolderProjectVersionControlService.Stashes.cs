using LibGit2Sharp;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.PackFiles.Utility;

public sealed partial class FolderProjectVersionControlService
{
    public IReadOnlyList<FolderProjectStashInfo> GetStashes(
        string projectRoot)
    {
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                return repository.Stashes
                    .Select(
                        (stash, index) => ToStashInfo(
                            repository,
                            stash,
                            index))
                    .ToList();
            });
    }

    public FolderProjectStashInfo StashChanges(
        string projectRoot,
        string message)
    {
        return Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                if (!RetrieveWorkingStatus(repository).Any())
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.NothingToCommit,
                        "The folder project has no changes to stash.");
                }

                var stash = CreateStashCore(repository, message);
                return ToStashInfo(repository, stash, 0);
            });
    }

    public void ApplyStash(
        string projectRoot,
        int index)
    {
        ApplyOrPopStash(projectRoot, index, pop: false);
    }

    public void PopStash(
        string projectRoot,
        int index)
    {
        ApplyOrPopStash(projectRoot, index, pop: true);
    }

    public void DeleteStash(
        string projectRoot,
        int index)
    {
        Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureStashExists(repository, index);
                repository.Stashes.Remove(index);
                return true;
            });
    }

    public void ClearStashes(string projectRoot)
    {
        Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                while (repository.Stashes.Any())
                    repository.Stashes.Remove(0);
                return true;
            });
    }

    private static void ApplyOrPopStash(
        string projectRoot,
        int index,
        bool pop)
    {
        Execute(
            () =>
            {
                using var repository = OpenRepository(projectRoot);
                EnsureCommitStateSupported(repository);
                if (RetrieveWorkingStatus(repository).IsDirty)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.WorkingTreeNotClean,
                        "A stash can only be restored to a clean working tree.");
                }

                EnsureStashExists(repository, index);
                RestoreStashCore(repository, index, pop);

                return true;
            });
    }

    private static Stash CreateStashCore(
        Repository repository,
        string? message)
    {
        var identity = ReadLocalIdentity(repository);
        ValidateIdentity(identity);
        return repository.Stashes.Add(
            CreateSignature(identity),
            string.IsNullOrWhiteSpace(message)
                ? $"WIP on {repository.Head.FriendlyName}"
                : message.Trim(),
            StashModifiers.IncludeUntracked);
    }

    private static void RestoreStashCore(
        Repository repository,
        int index,
        bool pop)
    {
        var options = new StashApplyOptions
        {
            ApplyModifiers = StashApplyModifiers.ReinstateIndex,
        };
        var status = pop
            ? repository.Stashes.Pop(index, options)
            : repository.Stashes.Apply(index, options);
        if (status != StashApplyStatus.Applied)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.WorkingTreeNotClean,
                "The stash could not be restored cleanly.");
        }
    }

    private static void EnsureStashExists(
        Repository repository,
        int index)
    {
        if (index < 0 || index >= repository.Stashes.Count())
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.CommitNotFound,
                "The requested stash does not exist.");
        }
    }

    private static FolderProjectStashInfo ToStashInfo(
        Repository repository,
        Stash stash,
        int index)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in repository.Diff.Compare<TreeChanges>(
                     stash.Base.Tree,
                     stash.WorkTree.Tree))
        {
            paths.Add(change.Path.Replace('\\', '/'));
        }

        if (stash.Untracked != null)
            AddTreePaths(stash.Untracked.Tree, "", paths);

        return new FolderProjectStashInfo(
            index,
            stash.Message,
            stash.WorkTree.Author.When,
            paths.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static void AddTreePaths(
        Tree tree,
        string parentPath,
        ISet<string> paths)
    {
        foreach (var entry in tree)
        {
            var path = parentPath.Length == 0
                ? entry.Name
                : $"{parentPath}/{entry.Name}";
            if (entry.TargetType == TreeEntryTargetType.Tree &&
                entry.Target is Tree childTree)
            {
                AddTreePaths(childTree, path, paths);
            }
            else if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                paths.Add(path);
            }
        }
    }
}
