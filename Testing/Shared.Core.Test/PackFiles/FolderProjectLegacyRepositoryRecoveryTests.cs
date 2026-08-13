using LibGit2Sharp;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace Test.Shared.Core.PackFiles;

public sealed class FolderProjectLegacyRepositoryRecoveryTests
{
    private static readonly FolderProjectGitIdentity s_identity =
        new("AE 用户", "ae-user@example.invalid");

    [Test]
    public void ReadLegacyRepository_PreservesFilesIndexAndAllReferences()
    {
        using var project = new TemporaryDirectory("legacy-read-only");
        var versionControl = new FolderProjectVersionControlService();
        var trackedPath = Path.Combine(project.Path, "tracked.txt");
        File.WriteAllText(trackedPath, "base");
        versionControl.Initialize(project.Path, s_identity, "legacy-main");
        File.WriteAllText(
            Path.Combine(project.Path, "stash-only.txt"),
            "stash");
        versionControl.StashChanges(project.Path, "legacy stash");
        using (var repository = new Repository(project.Path))
        {
            repository.CreateBranch("legacy-extra");
            repository.Tags.Add("legacy-tag", repository.Head.Tip!);
            repository.Refs.Add(
                "refs/archive/legacy-pin",
                repository.Head.Tip!.Id);
        }
        File.WriteAllText(trackedPath, "staged");
        using (var repository = new Repository(project.Path))
            Commands.Stage(repository, "tracked.txt");
        File.WriteAllText(trackedPath, "working");
        File.WriteAllText(
            Path.Combine(project.Path, "untracked.txt"),
            "untracked");
        var references = CaptureAllReferences(project.Path);
        var indexBytes = File.ReadAllBytes(
            Path.Combine(project.Path, ".git", "index"));
        var trackedBytes = File.ReadAllBytes(trackedPath);
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        var history = new FolderProjectHistoryService(
            versionControl,
            localization);

        var status = history.GetStatus(project.Path);
        var restorePoints = history.GetRestorePoints(project.Path);

        using var reopened = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(status.Availability,
                Is.EqualTo(FolderProjectHistoryAvailability.Ready));
            Assert.That(status.UnrecordedChanges, Has.Count.EqualTo(2));
            Assert.That(
                status.UnrecordedChanges.Count(change =>
                    change.Path == "tracked.txt"),
                Is.EqualTo(1));
            Assert.That(restorePoints, Has.Count.EqualTo(1));
            Assert.That(reopened.Head.FriendlyName, Is.EqualTo("legacy-main"));
            Assert.That(CaptureAllReferences(project.Path),
                Is.EqualTo(references));
            Assert.That(
                File.ReadAllBytes(Path.Combine(project.Path, ".git", "index")),
                Is.EqualTo(indexBytes));
            Assert.That(File.ReadAllBytes(trackedPath), Is.EqualTo(trackedBytes));
        });
    }

    [Test]
    public void ReadLegacyRepository_IndexLockReportsBlockedRecoveryWithoutMutation()
    {
        using var project = new TemporaryDirectory("legacy-index-lock");
        var versionControl = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "tracked.txt"), "base");
        versionControl.Initialize(project.Path, s_identity);
        var lockPath = Path.Combine(project.Path, ".git", "index.lock");
        File.WriteAllText(lockPath, "legacy lock");
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        var history = new FolderProjectHistoryService(
            versionControl,
            localization);

        var status = history.GetStatus(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(status.Availability,
                Is.EqualTo(FolderProjectHistoryAvailability.RecoveryRequired));
            Assert.That(status.RecoveryReason,
                Is.EqualTo(FolderProjectHistoryRecoveryReason.RepositoryBusy));
            Assert.That(status.CanRecover, Is.False);
            Assert.That(File.ReadAllText(lockPath), Is.EqualTo("legacy lock"));
        });
    }

    [Test]
    public void RecoverToSafeState_ExternalMerge_PreservesFilesAndExtraReferences()
    {
        using var project = new TemporaryDirectory("legacy-merge-recovery");
        var service = new FolderProjectVersionControlService();
        var conflictPath = Path.Combine(project.Path, "conflict.txt");
        File.WriteAllText(conflictPath, "base");
        service.Initialize(project.Path, s_identity, "legacy-main");
        File.WriteAllText(
            Path.Combine(project.Path, "stash-only.txt"),
            "keep in stash");
        service.StashChanges(project.Path, "legacy stash");

        using (var repository = new Repository(project.Path))
        {
            var feature = repository.CreateBranch("legacy-feature");
            repository.Tags.Add("legacy-tag", repository.Head.Tip!);
            repository.Refs.Add(
                "refs/archive/legacy-pin",
                repository.Head.Tip!.Id);
            Commands.Checkout(repository, feature);
            File.WriteAllText(conflictPath, "feature");
            Commands.Stage(repository, "conflict.txt");
            repository.Commit("feature", Signature(), Signature());
            Commands.Checkout(repository, "legacy-main");
            File.WriteAllText(conflictPath, "main");
            Commands.Stage(repository, "conflict.txt");
            repository.Commit("main", Signature(), Signature());
            var result = repository.Merge(feature, Signature());
            Assert.That(result.Status, Is.EqualTo(MergeStatus.Conflicts));
        }

        var conflictBytes = File.ReadAllBytes(conflictPath);
        var before = CaptureProtectedState(project.Path);

        var status = service.RecoverToSafeState(project.Path);

        var after = CaptureProtectedState(project.Path);
        using var reopened = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(status.IsDetached, Is.False);
            Assert.That(
                status.OperationState,
                Is.EqualTo(FolderProjectRepositoryOperationState.None));
            Assert.That(
                status.Changes.Any(change => change.Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Conflicted)),
                Is.False);
            Assert.That(
                status.Changes.All(change =>
                    !change.Kind.HasFlag(
                        FolderProjectWorkingChangeKind.Staged)),
                Is.True);
            Assert.That(File.ReadAllBytes(conflictPath), Is.EqualTo(conflictBytes));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(reopened.Head.FriendlyName, Is.EqualTo("legacy-main"));
        });
    }

    [Test]
    public void RecoverToSafeState_UnfinishedLegacyOperation_KeepsResultAsUnrecorded()
    {
        using var project = new TemporaryDirectory("legacy-revert-recovery");
        var service = new FolderProjectVersionControlService();
        var trackedPath = Path.Combine(project.Path, "tracked.txt");
        File.WriteAllText(trackedPath, "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(trackedPath, "two");
        var second = service.CommitAll(project.Path, "second");
        using (var repository = new Repository(project.Path))
        {
            repository.Revert(
                repository.Head.Tip!,
                Signature(),
                new RevertOptions { CommitOnSuccess = false });
            Assert.That(repository.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.Revert));
        }

        var status = service.RecoverToSafeState(project.Path);

        using var reopened = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(reopened.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.None));
            Assert.That(reopened.Head.Tip!.Sha, Is.EqualTo(second.Id));
            Assert.That(File.ReadAllText(trackedPath), Is.EqualTo("one"));
            Assert.That(status.Changes, Has.Count.EqualTo(1));
            Assert.That(status.Changes[0].Kind.HasFlag(
                FolderProjectWorkingChangeKind.Unstaged), Is.True);
            Assert.That(status.Changes[0].Kind.HasFlag(
                FolderProjectWorkingChangeKind.Staged), Is.False);
        });
    }

    [Test]
    public void RecoverToSafeState_DetachedHead_AttachesWithoutChangingDiskOrIndex()
    {
        using var project = new TemporaryDirectory("legacy-detached-recovery");
        var service = new FolderProjectVersionControlService();
        var trackedPath = Path.Combine(project.Path, "tracked.txt");
        File.WriteAllText(trackedPath, "one");
        var initial = service.Initialize(project.Path, s_identity);
        File.WriteAllText(trackedPath, "two");
        service.CommitAll(project.Path, "second");
        using (var repository = new Repository(project.Path))
        {
            repository.Tags.Add("legacy-tag", initial.Id);
            Commands.Checkout(repository, initial.Id);
        }
        File.WriteAllText(trackedPath, "detached staged");
        using (var repository = new Repository(project.Path))
            Commands.Stage(repository, "tracked.txt");
        File.WriteAllText(trackedPath, "detached working");
        var indexBytes = File.ReadAllBytes(
            Path.Combine(project.Path, ".git", "index"));
        var fileBytes = File.ReadAllBytes(trackedPath);
        var protectedState = CaptureProtectedState(project.Path);

        var status = service.RecoverToSafeState(project.Path);

        using var reopened = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(status.IsDetached, Is.False);
            Assert.That(status.OperationState,
                Is.EqualTo(FolderProjectRepositoryOperationState.None));
            Assert.That(reopened.Head.Tip!.Sha, Is.EqualTo(initial.Id));
            Assert.That(reopened.Head.FriendlyName,
                Does.StartWith("asseteditor-recovery-"));
            Assert.That(File.ReadAllBytes(trackedPath), Is.EqualTo(fileBytes));
            Assert.That(
                File.ReadAllBytes(Path.Combine(project.Path, ".git", "index")),
                Is.EqualTo(indexBytes));
            Assert.That(CaptureProtectedState(project.Path),
                Is.EqualTo(protectedState));
        });
    }

    [Test]
    public void RecoverToSafeState_WhenResetFails_RestoresMergeMetadataAndIndex()
    {
        using var project = new TemporaryDirectory("legacy-recovery-failure");
        var setup = new FolderProjectVersionControlService();
        var trackedPath = Path.Combine(project.Path, "tracked.txt");
        File.WriteAllText(trackedPath, "one");
        var initial = setup.Initialize(project.Path, s_identity);
        File.WriteAllText(trackedPath, "staged");
        setup.StageChanges(project.Path, ["tracked.txt"]);
        File.WriteAllText(trackedPath, "working");
        var mergeHeadPath = Path.Combine(project.Path, ".git", "MERGE_HEAD");
        var mergeMessagePath = Path.Combine(project.Path, ".git", "MERGE_MSG");
        File.WriteAllText(mergeHeadPath, initial.Id + "\n");
        File.WriteAllText(mergeMessagePath, "legacy merge message\n");
        var indexBytes = File.ReadAllBytes(
            Path.Combine(project.Path, ".git", "index"));
        var fileBytes = File.ReadAllBytes(trackedPath);
        var service = new FolderProjectVersionControlService(
            new ResetMixedThenThrowPlatform());

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RecoverToSafeState(project.Path));

        using var reopened = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.IsRollbackIncomplete, Is.False);
            Assert.That(reopened.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.Merge));
            Assert.That(File.ReadAllText(mergeHeadPath),
                Is.EqualTo(initial.Id + "\n"));
            Assert.That(File.ReadAllText(mergeMessagePath),
                Is.EqualTo("legacy merge message\n"));
            Assert.That(
                File.ReadAllBytes(Path.Combine(project.Path, ".git", "index")),
                Is.EqualTo(indexBytes));
            Assert.That(File.ReadAllBytes(trackedPath), Is.EqualTo(fileBytes));
        });
    }

    [Test]
    public void RecoveryTransaction_RollbackRestoresMergeMetadataAndIndex()
    {
        using var project = new TemporaryDirectory(
            "legacy-recovery-host-failure");
        var service = new FolderProjectVersionControlService();
        var trackedPath = Path.Combine(project.Path, "tracked.txt");
        File.WriteAllText(trackedPath, "one");
        var initial = service.Initialize(project.Path, s_identity);
        File.WriteAllText(trackedPath, "staged");
        service.StageChanges(project.Path, ["tracked.txt"]);
        File.WriteAllText(trackedPath, "working");
        var mergeHeadPath = Path.Combine(project.Path, ".git", "MERGE_HEAD");
        File.WriteAllText(mergeHeadPath, initial.Id + "\n");
        var indexPath = Path.Combine(project.Path, ".git", "index");
        var indexBytes = File.ReadAllBytes(indexPath);

        var transaction = service.BeginRecoverToSafeState(project.Path);
        service.RollbackRecoverToSafeState(transaction);

        using var reopened = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                reopened.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.Merge));
            Assert.That(
                File.ReadAllText(mergeHeadPath),
                Is.EqualTo(initial.Id + "\n"));
            Assert.That(
                File.ReadAllBytes(indexPath),
                Is.EqualTo(indexBytes));
            Assert.That(File.ReadAllText(trackedPath), Is.EqualTo("working"));
        });
    }

    [Test]
    public void RecoverToSafeState_WhenBranchCreationFails_RemovesCreatedReference()
    {
        using var project = new TemporaryDirectory(
            "legacy-detached-recovery-failure");
        var setup = new FolderProjectVersionControlService();
        var trackedPath = Path.Combine(project.Path, "tracked.txt");
        File.WriteAllText(trackedPath, "one");
        var initial = setup.Initialize(project.Path, s_identity);
        File.WriteAllText(trackedPath, "two");
        setup.CommitAll(project.Path, "second");
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, initial.Id);
        var headBytes = File.ReadAllBytes(
            Path.Combine(project.Path, ".git", "HEAD"));
        var service = new FolderProjectVersionControlService(
            new CreateBranchThenThrowPlatform());

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RecoverToSafeState(project.Path));

        using var reopened = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.IsRollbackIncomplete, Is.False);
            Assert.That(reopened.Info.IsHeadDetached, Is.True);
            Assert.That(reopened.Head.Tip!.Sha, Is.EqualTo(initial.Id));
            Assert.That(
                reopened.Branches.Any(branch => branch.FriendlyName.StartsWith(
                    "asseteditor-recovery-",
                    StringComparison.Ordinal)),
                Is.False);
            Assert.That(
                File.ReadAllBytes(Path.Combine(project.Path, ".git", "HEAD")),
                Is.EqualTo(headBytes));
        });
    }

    private static Signature Signature() =>
        new(s_identity.Name, s_identity.Email, DateTimeOffset.Now);

    private static IReadOnlyDictionary<string, string> CaptureProtectedState(
        string projectRoot)
    {
        using var repository = new Repository(projectRoot);
        return repository.Refs
            .Where(reference =>
                reference.CanonicalName.StartsWith("refs/tags/", StringComparison.Ordinal) ||
                reference.CanonicalName.StartsWith("refs/stash", StringComparison.Ordinal) ||
                reference.CanonicalName.StartsWith("refs/archive/", StringComparison.Ordinal) ||
                reference.CanonicalName.Equals(
                    "refs/heads/legacy-feature",
                    StringComparison.Ordinal))
            .ToDictionary(
                reference => reference.CanonicalName,
                reference => reference.ResolveToDirectReference().TargetIdentifier,
                StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> CaptureAllReferences(
        string projectRoot)
    {
        using var repository = new Repository(projectRoot);
        return repository.Refs.ToDictionary(
            reference => reference.CanonicalName,
            reference => reference.ResolveToDirectReference().TargetIdentifier,
            StringComparer.Ordinal);
    }

    private sealed class ResetMixedThenThrowPlatform :
        FolderProjectVersionControlPlatform
    {
        public override void ResetMixed(
            Repository repository,
            Commit commit)
        {
            base.ResetMixed(repository, commit);
            throw new IOException("Injected recovery failure.");
        }
    }

    private sealed class CreateBranchThenThrowPlatform :
        FolderProjectVersionControlPlatform
    {
        public override Branch CreateBranch(
            Repository repository,
            string name,
            Commit commit)
        {
            _ = base.CreateBranch(repository, name, commit);
            throw new IOException("Injected branch creation failure.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; }

        public TemporaryDirectory(string name)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ae-{name}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (!Directory.Exists(Path))
                return;
            foreach (var file in Directory.EnumerateFiles(
                         Path,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Path, recursive: true);
        }
    }
}
