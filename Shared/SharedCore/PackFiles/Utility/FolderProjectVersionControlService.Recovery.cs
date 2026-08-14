using LibGit2Sharp;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.PackFiles.Utility;

internal sealed partial class FolderProjectVersionControlService
{
    private static readonly string[] s_recoveryMetadataNames =
    [
        "AUTO_MERGE",
        "BISECT_ANCESTORS_OK",
        "BISECT_EXPECTED_REV",
        "BISECT_LOG",
        "BISECT_NAMES",
        "BISECT_RUN",
        "BISECT_START",
        "BISECT_TERMS",
        "CHERRY_PICK_HEAD",
        "MERGE_HEAD",
        "MERGE_MODE",
        "MERGE_MSG",
        "ORIG_HEAD",
        "REBASE_HEAD",
        "REVERT_HEAD",
        "rebase-apply",
        "rebase-merge",
        "sequencer",
        FolderProjectMergeSessionStore.FileName,
    ];

    public FolderProjectRepositoryStatus RecoverToSafeState(
        string projectRoot)
    {
        var transaction = BeginRecoverToSafeState(projectRoot);
        CompleteRecoverToSafeState(transaction);
        return transaction.Status;
    }

    public FolderProjectRecoveryTransaction BeginRecoverToSafeState(
        string projectRoot)
    {
        return ExecuteMergeLocked(
            projectRoot,
            RecoverToSafeStateCore);
    }

    public void CompleteRecoverToSafeState(
        FolderProjectRecoveryTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.Complete();
    }

    public void RollbackRecoverToSafeState(
        FolderProjectRecoveryTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ExecuteMergeLocked(
            transaction.ProjectRoot,
            _ =>
            {
                try
                {
                    transaction.Rollback();
                }
                catch (FolderProjectVersionControlException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.RepositoryFailure,
                        "The repository recovery could not be rolled back.",
                        exception,
                        isRollbackIncomplete: true);
                }
                return true;
            });
    }

    private FolderProjectRecoveryTransaction RecoverToSafeStateCore(
        string projectRoot)
    {
        RepositoryRecoverySnapshot? snapshot = null;
        string? createdBranchReference = null;
        string? createdBranchTip = null;
        try
        {
            using (var repository = OpenRepository(projectRoot))
            {
                if (File.Exists(Path.Combine(
                        repository.Info.Path,
                        "index.lock")))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.RepositoryBusy,
                        "The folder-project repository is busy.");
                }

                var head = repository.Head.Tip;
                if (head == null)
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError
                            .UnsupportedOperationState,
                        "The repository has no history tip to recover.");
                }

                var changes = GetWorkingChanges(
                    repository,
                    projectRoot,
                    scanUnreadableEntries: true);
                if (changes.Any(change => change.Kind.HasFlag(
                        FolderProjectWorkingChangeKind.Unreadable)))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.WorkingTreeNotClean,
                        "Unreadable project files prevent safe recovery.");
                }

                var needsOperationRecovery =
                    repository.Info.CurrentOperation != CurrentOperation.None ||
                    repository.Index.Conflicts.Any() ||
                    File.Exists(Path.Combine(
                        repository.Info.Path,
                        FolderProjectMergeSessionStore.FileName));
                if (!repository.Info.IsHeadDetached &&
                    !needsOperationRecovery)
                {
                    var unchangedStatus = GetStatus(
                        projectRoot,
                        scanUnreadableEntries: true);
                    return new FolderProjectRecoveryTransaction(
                        projectRoot,
                        unchangedStatus,
                        () => { });
                }

                snapshot = RepositoryRecoverySnapshot.Capture(repository);
                if (needsOperationRecovery)
                {
                    _platform.ResetMixed(repository, head);
                    new FolderProjectMergeSessionStore(
                            repository.Info.Path,
                            _platform)
                        .Delete();
                }

                if (repository.Info.IsHeadDetached)
                {
                    var branch = FindBranchAtHead(repository, head);
                    if (branch == null)
                    {
                        createdBranchTip = head.Sha;
                        branch = CreateRecoveryBranch(
                            repository,
                            head,
                            out createdBranchReference);
                    }
                    _platform.AttachHead(
                        repository,
                        branch.CanonicalName);
                }
            }

            using (var verification = OpenRepository(projectRoot))
            {
                if (verification.Info.IsHeadDetached ||
                    verification.Info.CurrentOperation !=
                    CurrentOperation.None ||
                    verification.Index.Conflicts.Any() ||
                    File.Exists(Path.Combine(
                        verification.Info.Path,
                        "index.lock")) ||
                    File.Exists(Path.Combine(
                        verification.Info.Path,
                        FolderProjectMergeSessionStore.FileName)))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError
                            .UnsupportedOperationState,
                        "The repository did not reach a safe state.");
                }
            }

            var status = GetStatus(projectRoot, scanUnreadableEntries: true);
            return new FolderProjectRecoveryTransaction(
                projectRoot,
                status,
                () => RestoreRecoverySnapshot(
                    projectRoot,
                    snapshot!,
                    createdBranchReference,
                    createdBranchTip));
        }
        catch (Exception failure)
        {
            if (snapshot == null)
                throw MapRecoveryFailure(failure);

            try
            {
                RestoreRecoverySnapshot(
                    projectRoot,
                    snapshot,
                    createdBranchReference,
                    createdBranchTip);
            }
            catch (Exception rollbackFailure)
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.RepositoryFailure,
                    "The repository recovery could not be rolled back.",
                    new AggregateException(failure, rollbackFailure),
                    isRollbackIncomplete: true);
            }

            throw MapRecoveryFailure(failure);
        }
    }

    private void RestoreRecoverySnapshot(
        string projectRoot,
        RepositoryRecoverySnapshot snapshot,
        string? createdBranchReference,
        string? createdBranchTip)
    {
        snapshot.Restore(_platform);
        RemoveCreatedRecoveryBranch(
            projectRoot,
            createdBranchReference,
            createdBranchTip);
    }

    private Branch CreateRecoveryBranch(
        Repository repository,
        Commit head,
        out string branchReference)
    {
        var shortId = head.Sha[..8];
        var baseName = $"asseteditor-recovery-{shortId}";
        var name = baseName;
        var suffix = 2;
        while (repository.Branches[name] != null)
            name = $"{baseName}-{suffix++}";

        branchReference = $"refs/heads/{name}";
        return _platform.CreateBranch(repository, name, head);
    }

    private static Branch? FindBranchAtHead(
        Repository repository,
        Commit head)
    {
        return repository.Branches
            .Where(branch => !branch.IsRemote && branch.Tip?.Sha == head.Sha)
            .OrderBy(branch => branch.FriendlyName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private void RemoveCreatedRecoveryBranch(
        string projectRoot,
        string? branchReference,
        string? expectedTip)
    {
        if (branchReference == null || expectedTip == null)
            return;

        using var repository = OpenRepository(projectRoot);
        var branch = repository.Branches.FirstOrDefault(candidate =>
            candidate.CanonicalName.Equals(
                branchReference,
                StringComparison.Ordinal));
        if (branch?.Tip?.Sha == expectedTip && !branch.IsCurrentRepositoryHead)
            repository.Branches.Remove(branch);

        var branchLogPath = Path.Combine(
            repository.Info.Path,
            "logs",
            branchReference.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(branchLogPath))
        {
            File.SetAttributes(branchLogPath, FileAttributes.Normal);
            File.Delete(branchLogPath);
        }
    }

    private static FolderProjectVersionControlException MapRecoveryFailure(
        Exception exception)
    {
        return exception as FolderProjectVersionControlException ??
               MapRepositoryException(
                   exception,
                   FolderProjectVersionControlError.RepositoryFailure);
    }

    private sealed record RepositoryRecoverySnapshot(
        GitIndexSnapshot Index,
        IReadOnlyList<RepositoryMetadataSnapshot> Metadata)
    {
        public static RepositoryRecoverySnapshot Capture(
            Repository repository)
        {
            var metadataPath = repository.Info.Path;
            var paths = s_recoveryMetadataNames
                .Select(name => Path.Combine(metadataPath, name))
                .Append(Path.Combine(metadataPath, "HEAD"))
                .Append(Path.Combine(metadataPath, "logs", "HEAD"));
            if (!repository.Info.IsHeadDetached)
            {
                paths = paths.Append(Path.Combine(
                    metadataPath,
                    "logs",
                    repository.Head.CanonicalName.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
            }

            return new RepositoryRecoverySnapshot(
                GitIndexSnapshot.Capture(Path.Combine(metadataPath, "index")),
                paths.Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(RepositoryMetadataSnapshot.Capture)
                    .ToList());
        }

        public void Restore(FolderProjectVersionControlPlatform platform)
        {
            var failures = new List<Exception>();
            foreach (var item in Metadata)
            {
                try
                {
                    item.Restore();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            try
            {
                Index.Restore(platform);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (failures.Count != 0)
            {
                throw new AggregateException(
                    "Repository recovery metadata could not be restored.",
                    failures);
            }
        }
    }

    private sealed record RepositoryMetadataSnapshot(
        string Path,
        MetadataEntryKind Kind,
        byte[] Bytes,
        FileAttributes Attributes,
        IReadOnlyList<RepositoryMetadataSnapshot> Children)
    {
        public static RepositoryMetadataSnapshot Capture(string path)
        {
            if (File.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                EnsureRegularMetadataEntry(path, attributes);
                return new RepositoryMetadataSnapshot(
                    path,
                    MetadataEntryKind.File,
                    File.ReadAllBytes(path),
                    attributes,
                    []);
            }
            if (Directory.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                EnsureRegularMetadataEntry(path, attributes);
                return new RepositoryMetadataSnapshot(
                    path,
                    MetadataEntryKind.Directory,
                    [],
                    attributes,
                    Directory.EnumerateFileSystemEntries(path)
                        .Select(Capture)
                        .ToList());
            }

            return new RepositoryMetadataSnapshot(
                path,
                MetadataEntryKind.Missing,
                [],
                FileAttributes.Normal,
                []);
        }

        public void Restore()
        {
            DeleteCurrentEntry(Path);
            if (Kind == MetadataEntryKind.Missing)
                return;
            if (Kind == MetadataEntryKind.File)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllBytes(Path, Bytes);
                File.SetAttributes(Path, Attributes);
                return;
            }

            Directory.CreateDirectory(Path);
            foreach (var child in Children)
                child.Restore();
            File.SetAttributes(Path, Attributes);
        }

        private static void DeleteCurrentEntry(string path)
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(
                             path,
                             "*",
                             SearchOption.AllDirectories))
                {
                    File.SetAttributes(entry, FileAttributes.Normal);
                }
                File.SetAttributes(path, FileAttributes.Normal);
                Directory.Delete(path, recursive: true);
            }
        }

        private static void EnsureRegularMetadataEntry(
            string path,
            FileAttributes attributes)
        {
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.UnsupportedRepository,
                    $"Repository recovery metadata is redirected: {path}");
            }
        }
    }

    private enum MetadataEntryKind
    {
        Missing,
        File,
        Directory,
    }
}
