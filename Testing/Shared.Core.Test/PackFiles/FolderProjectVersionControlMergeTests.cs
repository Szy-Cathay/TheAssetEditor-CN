using LibGit2Sharp;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Test.Shared.Core.PackFiles;

public class FolderProjectVersionControlMergeTests
{
    private static readonly FolderProjectGitIdentity s_identity =
        new("AE 用户", "ae-user@example.invalid");

    [Test]
    public void BeginMerge_SourceAlreadyContained_ReturnsUpToDateWithoutSession()
    {
        using var project = CreateInitializedProject("merge-up-to-date");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        var before = ReadHead(project.Path);

        var result = service.BeginMerge(project.Path, "source");

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Outcome,
                Is.EqualTo(FolderProjectMergeOutcome.UpToDate));
            Assert.That(ReadHead(project.Path), Is.EqualTo(before));
            Assert.That(result.State.Phase, Is.EqualTo(FolderProjectMergePhase.None));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
            using var repository = new Repository(project.Path);
            Assert.That(
                repository.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.None));
            Assert.That(repository.RetrieveStatus().IsDirty, Is.False);
        });
    }

    [Test]
    public void BeginMerge_FastForward_IgnoresNoFastForwardConfiguration()
    {
        using var project = CreateInitializedProject("merge-fast-forward");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        service.SwitchBranch(project.Path, "source");
        File.WriteAllBytes(Path.Combine(project.Path, "source.bin"), [0, 255, 13, 10]);
        var sourceTip = service.CommitAll(project.Path, "source change");
        service.SwitchBranch(project.Path, "master");
        using (var repository = new Repository(project.Path))
        {
            repository.Config.Set(
                "merge.ff",
                "false",
                ConfigurationLevel.Local);
        }

        var result = service.BeginMerge(project.Path, "source");

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Outcome,
                Is.EqualTo(FolderProjectMergeOutcome.FastForwarded));
            Assert.That(ReadHead(project.Path), Is.EqualTo(sourceTip.Id));
            Assert.That(result.State.Phase, Is.EqualTo(FolderProjectMergePhase.None));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
            Assert.That(
                File.ReadAllBytes(Path.Combine(project.Path, "source.bin")),
                Is.EqualTo(new byte[] { 0, 255, 13, 10 }));
            using var repository = new Repository(project.Path);
            Assert.That(repository.Head.FriendlyName, Is.EqualTo("master"));
            Assert.That(
                repository.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.None));
            Assert.That(repository.RetrieveStatus().IsDirty, Is.False);
        });
    }

    [Test]
    public void BeginMerge_WithProgressReportsNativeCheckoutFileCounts()
    {
        using var project = CreateInitializedProject("merge-progress");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        service.SwitchBranch(project.Path, "source");
        File.WriteAllText(
            Path.Combine(project.Path, "first.txt"),
            "first");
        File.WriteAllText(
            Path.Combine(project.Path, "second.txt"),
            "second");
        service.CommitAll(project.Path, "source files");
        service.SwitchBranch(project.Path, "master");
        var progress = new List<FolderProjectVersionControlProgress>();
        var progressOverload = typeof(IFolderProjectVersionControlService)
            .GetMethod(
                nameof(IFolderProjectVersionControlService.BeginMerge),
                [
                    typeof(string),
                    typeof(string),
                    typeof(Action<FolderProjectVersionControlProgress>),
                ]);

        Assert.That(
            progressOverload,
            Is.Not.Null,
            "BeginMerge must expose native checkout progress.");
        if (progressOverload == null)
            return;

        var result = (FolderProjectMergeStartResult)progressOverload.Invoke(
            service,
            [
                project.Path,
                "source",
                (Action<FolderProjectVersionControlProgress>)progress.Add,
            ])!;
        var fileProgress = progress
            .Where(item => item.Stage.ToString() == "MergingFiles")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Outcome,
                Is.EqualTo(FolderProjectMergeOutcome.FastForwarded));
            Assert.That(fileProgress, Is.Not.Empty);
            Assert.That(
                fileProgress.Select(item => item.Detail),
                Does.Contain("first.txt"));
            Assert.That(
                fileProgress.Select(item => item.Detail),
                Does.Contain("second.txt"));
            Assert.That(fileProgress[^1].Total, Is.GreaterThan(0));
            Assert.That(
                fileProgress[^1].Completed,
                Is.EqualTo(fileProgress[^1].Total));
        });
    }

    [Test]
    public void BeginMerge_DivergentWithoutConflicts_LeavesMergeReadyToCommit()
    {
        using var project = CreateDivergentProject(
            "merge-divergent",
            out var originalHead,
            out var sourceHead);
        var service = new FolderProjectVersionControlService();

        var result = service.BeginMerge(project.Path, "source");

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Outcome,
                Is.EqualTo(FolderProjectMergeOutcome.ReadyToCommit));
            Assert.That(result.State.Phase, Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
            Assert.That(result.State.OriginalHeadCommitId, Is.EqualTo(originalHead));
            Assert.That(result.State.SourceHeadCommitId, Is.EqualTo(sourceHead));
            Assert.That(result.State.Conflicts, Is.Empty);
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
            using var repository = new Repository(project.Path);
            Assert.That(
                repository.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.Merge));
            Assert.That(repository.Index.IsFullyMerged, Is.True);
            Assert.That(repository.RetrieveStatus().IsDirty, Is.True);
        });
    }

    [Test]
    public void BeginMerge_BinaryConflict_ReportsRawThreeSidesAndOpaqueId()
    {
        var ancestorBytes = new byte[] { 0, 1, 2, 3 };
        var currentBytes = new byte[] { 0, 10, 11, 12, 255 };
        var incomingBytes = new byte[] { 0, 20, 21, 22, 254 };
        using var project = CreateSingleFileConflict(
            "merge-binary-conflict",
            "conflict.bin",
            ancestorBytes,
            currentBytes,
            incomingBytes);
        var service = new FolderProjectVersionControlService();

        var result = service.BeginMerge(project.Path, "source");

        Assert.That(
            result.State.Conflicts,
            Has.Count.EqualTo(1),
            string.Join(
                " | ",
                result.State.Conflicts.Select(
                    conflict =>
                        $"{conflict.Ancestor?.RepositoryPath ?? "-"};" +
                        $"{conflict.Current?.RepositoryPath ?? "-"};" +
                        $"{conflict.Incoming?.RepositoryPath ?? "-"}")));
        var conflict = result.State.Conflicts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Outcome,
                Is.EqualTo(FolderProjectMergeOutcome.Conflicts));
            Assert.That(result.State.Phase, Is.EqualTo(FolderProjectMergePhase.Conflicts));
            Assert.That(conflict.Id, Has.Length.EqualTo(64));
            Assert.That(conflict.Id, Does.Not.Contain("conflict.bin"));
            AssertMergeSide(
                conflict.Ancestor,
                "conflict.bin",
                ancestorBytes);
            AssertMergeSide(
                conflict.Current,
                "conflict.bin",
                currentBytes);
            AssertMergeSide(
                conflict.Incoming,
                "conflict.bin",
                incomingBytes);
        });
    }

    [Test]
    public void BeginMerge_TextConflict_ReportsNonBinarySides()
    {
        var ancestorBytes = "ancestor"u8.ToArray();
        var currentBytes = "current"u8.ToArray();
        var incomingBytes = "incoming"u8.ToArray();
        using var project = CreateSingleFileConflict(
            "merge-text-conflict",
            "conflict.txt",
            ancestorBytes,
            currentBytes,
            incomingBytes);
        var service = new FolderProjectVersionControlService();

        var conflict = service.BeginMerge(project.Path, "source")
            .State.Conflicts.Single();

        Assert.Multiple(() =>
        {
            Assert.That(conflict.Ancestor!.IsBinary, Is.False);
            Assert.That(conflict.Current!.IsBinary, Is.False);
            Assert.That(conflict.Incoming!.IsBinary, Is.False);
            Assert.That(conflict.Ancestor.Size, Is.EqualTo(ancestorBytes.Length));
            Assert.That(conflict.Current.Size, Is.EqualTo(currentBytes.Length));
            Assert.That(conflict.Incoming.Size, Is.EqualTo(incomingBytes.Length));
        });
    }

    [Test]
    public void BeginMerge_AddAddConflict_ReportsMissingAncestor()
    {
        using var project = CreateInitializedProject("merge-add-add-conflict");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        File.WriteAllText(Path.Combine(project.Path, "added.txt"), "current");
        service.CommitAll(project.Path, "current add");
        service.SwitchBranch(project.Path, "source");
        File.WriteAllText(Path.Combine(project.Path, "added.txt"), "incoming");
        service.CommitAll(project.Path, "incoming add");
        service.SwitchBranch(project.Path, "master");

        var conflict = service.BeginMerge(project.Path, "source")
            .State.Conflicts.Single();

        Assert.Multiple(() =>
        {
            Assert.That(conflict.Ancestor, Is.Null);
            Assert.That(conflict.Current, Is.Not.Null);
            Assert.That(conflict.Incoming, Is.Not.Null);
        });
    }

    [Test]
    public void BeginMerge_DeleteModifyConflict_ReportsMissingCurrent()
    {
        using var project = CreateInitializedProject(
            "merge-delete-modify-conflict");
        var targetPath = Path.Combine(project.Path, "target.txt");
        File.WriteAllText(targetPath, "ancestor");
        var service = new FolderProjectVersionControlService();
        service.CommitAll(project.Path, "add target");
        service.CreateBranch(project.Path, "source");
        File.Delete(targetPath);
        service.CommitAll(project.Path, "current delete");
        service.SwitchBranch(project.Path, "source");
        File.WriteAllText(targetPath, "incoming edit");
        service.CommitAll(project.Path, "incoming modify");
        service.SwitchBranch(project.Path, "master");

        var conflict = service.BeginMerge(project.Path, "source")
            .State.Conflicts.Single();

        Assert.Multiple(() =>
        {
            Assert.That(conflict.Ancestor, Is.Not.Null);
            Assert.That(conflict.Current, Is.Null);
            Assert.That(conflict.Incoming, Is.Not.Null);
        });
    }

    [Test]
    public void BeginMerge_ModifyDeleteConflict_ReportsIncomingAsMissing()
    {
        using var project = CreateInitializedProject("merge-delete-conflict");
        var targetPath = Path.Combine(project.Path, "delete.bin");
        File.WriteAllBytes(targetPath, [0, 1, 2]);
        var service = new FolderProjectVersionControlService();
        service.CommitAll(project.Path, "add delete target");
        service.CreateBranch(project.Path, "source");
        File.WriteAllBytes(targetPath, [0, 3, 4]);
        service.CommitAll(project.Path, "modify target");
        service.SwitchBranch(project.Path, "source");
        File.Delete(targetPath);
        service.CommitAll(project.Path, "delete target");
        service.SwitchBranch(project.Path, "master");

        var result = service.BeginMerge(project.Path, "source");

        Assert.That(
            result.State.Conflicts,
            Has.Count.EqualTo(1),
            string.Join(
                " | ",
                result.State.Conflicts.Select(
                    conflict =>
                        $"{conflict.Ancestor?.RepositoryPath ?? "-"};" +
                        $"{conflict.Current?.RepositoryPath ?? "-"};" +
                        $"{conflict.Incoming?.RepositoryPath ?? "-"}")));
        var conflict = result.State.Conflicts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(conflict.Ancestor, Is.Not.Null);
            Assert.That(conflict.Current, Is.Not.Null);
            Assert.That(conflict.Incoming, Is.Null);
        });
    }

    [Test]
    public void BeginMerge_RenameRenameConflict_ReportsAllThreePaths()
    {
        using var project = CreateInitializedProject("merge-rename-conflict");
        var originalPath = Path.Combine(project.Path, "original.bin");
        var content = Enumerable.Range(0, 128)
            .Select(value => (byte)value)
            .ToArray();
        File.WriteAllBytes(originalPath, content);
        var service = new FolderProjectVersionControlService();
        service.CommitAll(project.Path, "add rename target");
        service.CreateBranch(project.Path, "source");
        File.Move(
            originalPath,
            Path.Combine(project.Path, "current.bin"));
        service.CommitAll(project.Path, "rename current");
        service.SwitchBranch(project.Path, "source");
        File.Move(
            originalPath,
            Path.Combine(project.Path, "incoming.bin"));
        service.CommitAll(project.Path, "rename incoming");
        service.SwitchBranch(project.Path, "master");

        var result = service.BeginMerge(project.Path, "source");

        Assert.That(
            result.State.Conflicts,
            Has.Count.EqualTo(1),
            string.Join(
                " | ",
                result.State.Conflicts.Select(
                    conflict =>
                        $"{conflict.Ancestor?.RepositoryPath ?? "-"};" +
                        $"{conflict.Current?.RepositoryPath ?? "-"};" +
                        $"{conflict.Incoming?.RepositoryPath ?? "-"}")));
        var conflict = result.State.Conflicts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                conflict.Ancestor?.RepositoryPath,
                Is.EqualTo("original.bin"));
            Assert.That(
                conflict.Current?.RepositoryPath,
                Is.EqualTo("current.bin"));
            Assert.That(
                conflict.Incoming?.RepositoryPath,
                Is.EqualTo("incoming.bin"));
        });
    }

    [TestCase(FolderProjectMergeChoice.Current)]
    [TestCase(FolderProjectMergeChoice.Incoming)]
    public void ResolveMergeConflict_WholeFileChoice_UsesExactRawBlob(
        FolderProjectMergeChoice choice)
    {
        var currentBytes = new byte[] { 0, 13, 10, 255, 1 };
        var incomingBytes = new byte[] { 0, 10, 13, 254, 2 };
        using var project = CreateSingleFileConflict(
            $"merge-resolve-{choice}",
            "conflict.bin",
            [0, 1, 2],
            currentBytes,
            incomingBytes);
        var service = new FolderProjectVersionControlService();
        var started = service.BeginMerge(project.Path, "source");

        var state = service.ResolveMergeConflict(
            project.Path,
            started.State.Conflicts.Single().Id,
            choice);

        var expected = choice == FolderProjectMergeChoice.Current
            ? currentBytes
            : incomingBytes;
        byte[] stagedBytes;
        using (var repository = new Repository(project.Path))
        using (var stream = repository
                   .Lookup<Blob>(repository.Index["conflict.bin"].Id)
                   .GetContentStream())
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            stagedBytes = memory.ToArray();
        }
        Assert.Multiple(() =>
        {
            Assert.That(state.Phase, Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
            Assert.That(state.Conflicts, Is.Empty);
            Assert.That(
                File.ReadAllBytes(Path.Combine(project.Path, "conflict.bin")),
                Is.EqualTo(expected));
            Assert.That(stagedBytes, Is.EqualTo(expected));
            using var repository = new Repository(project.Path);
            Assert.That(repository.Index.Conflicts, Is.Empty);
        });
    }

    [Test]
    public void ResolveMergeConflict_IncomingDeletion_RemovesFileAndIndexEntry()
    {
        using var project = CreateInitializedProject("merge-resolve-delete");
        var targetPath = Path.Combine(project.Path, "delete.bin");
        File.WriteAllBytes(targetPath, [0, 1, 2]);
        var service = new FolderProjectVersionControlService();
        service.CommitAll(project.Path, "add delete target");
        service.CreateBranch(project.Path, "source");
        File.WriteAllBytes(targetPath, [0, 3, 4]);
        service.CommitAll(project.Path, "modify target");
        service.SwitchBranch(project.Path, "source");
        File.Delete(targetPath);
        service.CommitAll(project.Path, "delete target");
        service.SwitchBranch(project.Path, "master");
        var started = service.BeginMerge(project.Path, "source");

        var state = service.ResolveMergeConflict(
            project.Path,
            started.State.Conflicts.Single().Id,
            FolderProjectMergeChoice.Incoming);

        Assert.Multiple(() =>
        {
            Assert.That(state.Phase, Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
            Assert.That(File.Exists(targetPath), Is.False);
            using var repository = new Repository(project.Path);
            Assert.That(repository.Index["delete.bin"], Is.Null);
            Assert.That(repository.Index.Conflicts, Is.Empty);
        });
    }

    [Test]
    public void ResolveMergeConflict_IncomingExecutable_PreservesExecutableMode()
    {
        using var project = CreateInitializedProject(
            "merge-resolve-executable");
        const string repositoryPath = "mode.bin";
        var targetPath = Path.Combine(project.Path, repositoryPath);
        var ancestorBytes = Enumerable.Repeat((byte)0, 128).ToArray();
        var currentBytes = Enumerable.Repeat((byte)1, 128).ToArray();
        var incomingBytes = Enumerable.Repeat((byte)2, 128).ToArray();
        currentBytes[0] = 0;
        incomingBytes[0] = 0;
        File.WriteAllBytes(targetPath, ancestorBytes);
        var service = new FolderProjectVersionControlService();
        service.CommitAll(project.Path, "add mode target");
        service.CreateBranch(project.Path, "source");
        File.WriteAllBytes(targetPath, currentBytes);
        service.CommitAll(project.Path, "current content");
        service.SwitchBranch(project.Path, "source");
        File.WriteAllBytes(targetPath, incomingBytes);
        using (var repository = new Repository(project.Path))
        {
            using var content = new MemoryStream(incomingBytes);
            var blob = repository.ObjectDatabase.CreateBlob(content);
            repository.Index.Add(
                blob,
                repositoryPath,
                Mode.ExecutableFile);
            repository.Index.Write();
            var signature = new Signature(
                s_identity.Name,
                s_identity.Email,
                DateTimeOffset.Now);
            repository.Commit(
                "incoming executable content",
                signature,
                signature);
        }
        service.SwitchBranch(project.Path, "master");
        var started = service.BeginMerge(project.Path, "source");
        var conflict = started.State.Conflicts.Single();

        var state = service.ResolveMergeConflict(
            project.Path,
            conflict.Id,
            FolderProjectMergeChoice.Incoming);

        Assert.Multiple(() =>
        {
            Assert.That(
                conflict.Incoming?.Mode,
                Is.EqualTo(FolderProjectGitFileMode.Executable));
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
            Assert.That(
                File.ReadAllBytes(targetPath),
                Is.EqualTo(incomingBytes));
            using var repository = new Repository(project.Path);
            Assert.That(
                repository.Index[repositoryPath]?.Mode,
                Is.EqualTo(Mode.ExecutableFile));
            Assert.That(repository.Index.Conflicts, Is.Empty);
        });
    }

    [Test]
    public void ResolveMergeConflict_RenameRenameIncoming_ClearsAllStagesAndUsesIncomingPath()
    {
        using var project = CreateInitializedProject(
            "merge-resolve-rename-conflict");
        var originalPath = Path.Combine(project.Path, "original.bin");
        var currentPath = Path.Combine(project.Path, "current.bin");
        var incomingPath = Path.Combine(project.Path, "incoming.bin");
        var originalContent = Enumerable.Range(0, 128)
            .Select(value => (byte)value)
            .ToArray();
        File.WriteAllBytes(originalPath, originalContent);
        var service = new FolderProjectVersionControlService();
        service.CommitAll(project.Path, "add rename target");
        service.CreateBranch(project.Path, "source");
        File.Move(originalPath, currentPath);
        service.CommitAll(project.Path, "rename current");
        service.SwitchBranch(project.Path, "source");
        File.Move(originalPath, incomingPath);
        service.CommitAll(project.Path, "rename incoming");
        service.SwitchBranch(project.Path, "master");
        var started = service.BeginMerge(project.Path, "source");

        var state = service.ResolveMergeConflict(
            project.Path,
            started.State.Conflicts.Single().Id,
            FolderProjectMergeChoice.Incoming);

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
            Assert.That(File.Exists(originalPath), Is.False);
            Assert.That(File.Exists(currentPath), Is.False);
            Assert.That(
                File.ReadAllBytes(incomingPath),
                Is.EqualTo(originalContent));
            using var repository = new Repository(project.Path);
            Assert.That(repository.Index.Conflicts, Is.Empty);
            Assert.That(repository.Index["original.bin"], Is.Null);
            Assert.That(repository.Index["current.bin"], Is.Null);
            Assert.That(repository.Index["incoming.bin"], Is.Not.Null);
        });
    }

    [Test]
    public void ResolveMergeConflict_WorktreeWriteFails_RestoresIndexWorktreeAndSession()
    {
        var currentBytes = new byte[] { 0, 3, 4, 5 };
        using var project = CreateSingleFileConflict(
            "merge-resolve-rollback",
            "conflict.bin",
            [0, 1, 2],
            currentBytes,
            [0, 6, 7, 8]);
        var platform = new ConflictWriteFailurePlatform(
            Path.Combine(project.Path, "conflict.bin"));
        var service = new FolderProjectVersionControlService(platform);
        var started = service.BeginMerge(project.Path, "source");
        var indexPath = Path.Combine(project.Path, ".git", "index");
        var indexBefore = File.ReadAllBytes(indexPath);
        var worktreeBefore = File.ReadAllBytes(
            Path.Combine(project.Path, "conflict.bin"));
        var sessionBefore = File.ReadAllBytes(SessionPath(project.Path));
        platform.FailNextConflictWrite = true;

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.ResolveMergeConflict(
                project.Path,
                started.State.Conflicts.Single().Id,
                FolderProjectMergeChoice.Current));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(File.ReadAllBytes(indexPath), Is.EqualTo(indexBefore));
            Assert.That(
                File.ReadAllBytes(Path.Combine(project.Path, "conflict.bin")),
                Is.EqualTo(worktreeBefore));
            Assert.That(
                File.ReadAllBytes(SessionPath(project.Path)),
                Is.EqualTo(sessionBefore));
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.Conflicts));
        });
    }

    [Test]
    public void ResolveMergeConflict_SessionWriteFailsAfterReplace_RollsBackExactly()
    {
        var currentBytes = new byte[] { 0, 3, 4, 5 };
        using var project = CreateSingleFileConflict(
            "merge-resolve-session-rollback",
            "conflict.bin",
            [0, 1, 2],
            currentBytes,
            [0, 6, 7, 8]);
        var platform = new SessionMoveThenThrowPlatform();
        var service = new FolderProjectVersionControlService(platform);
        var started = service.BeginMerge(project.Path, "source");
        var indexPath = Path.Combine(project.Path, ".git", "index");
        var conflictPath = Path.Combine(project.Path, "conflict.bin");
        var indexBefore = File.ReadAllBytes(indexPath);
        var worktreeBefore = File.ReadAllBytes(conflictPath);
        var sessionBefore = File.ReadAllBytes(SessionPath(project.Path));
        platform.FailNextSessionMoveAfterSuccess = true;

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.ResolveMergeConflict(
                project.Path,
                started.State.Conflicts.Single().Id,
                FolderProjectMergeChoice.Current));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(File.ReadAllBytes(indexPath), Is.EqualTo(indexBefore));
            Assert.That(File.ReadAllBytes(conflictPath), Is.EqualTo(worktreeBefore));
            Assert.That(
                File.ReadAllBytes(SessionPath(project.Path)),
                Is.EqualTo(sessionBefore));
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.Conflicts));
        });
    }

    [Test]
    public void ResolveMergeConflict_ConcurrentWorktreeChangeDuringFailure_IsPreserved()
    {
        var externalBytes = new byte[] { 0, 9, 9, 9 };
        using var project = CreateSingleFileConflict(
            "merge-resolve-concurrent-change",
            "conflict.bin",
            [0, 1, 2],
            [0, 3, 4, 5],
            [0, 6, 7, 8]);
        var conflictPath = Path.Combine(project.Path, "conflict.bin");
        var platform = new ConcurrentConflictWriteFailurePlatform(
            conflictPath,
            externalBytes);
        var service = new FolderProjectVersionControlService(platform);
        var started = service.BeginMerge(project.Path, "source");
        platform.FailNextSessionMoveAfterExternalWrite = true;

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.ResolveMergeConflict(
                project.Path,
                started.State.Conflicts.Single().Id,
                FolderProjectMergeChoice.Current));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(
                File.ReadAllBytes(conflictPath),
                Is.EqualTo(externalBytes));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
        });
    }

    [Test]
    public void GetMergeState_ExtraIndexChange_RequiresRecoveryAndBlocksComplete()
    {
        using var project = CreateDivergentProject(
            "merge-index-drift",
            out var originalHead,
            out _);
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");
        using (var repository = new Repository(project.Path))
        {
            File.WriteAllText(
                Path.Combine(project.Path, "base.bin"),
                "external index change");
            Commands.Stage(repository, "base.bin");
        }

        var state = service.GetMergeState(project.Path);
        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(project.Path, "must not commit"));

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.MergeRecoveryRequired));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void GetMergeState_ExtraWorktreeChange_RequiresRecoveryAndBlocksComplete()
    {
        using var project = CreateDivergentProject(
            "merge-worktree-drift",
            out var originalHead,
            out _);
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");
        File.WriteAllText(
            Path.Combine(project.Path, "source.txt"),
            "external worktree change");

        var state = service.GetMergeState(project.Path);
        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(project.Path, "must not commit"));

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.MergeRecoveryRequired));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void CompleteMerge_ReadyState_CreatesExactTwoParentCommitAndCleansSession()
    {
        using var project = CreateDivergentProject(
            "merge-complete",
            out var originalHead,
            out var sourceHead);
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");

        var commit = service.CompleteMerge(
            project.Path,
            "merge source into master");

        Assert.Multiple(() =>
        {
            Assert.That(
                commit.ParentIds,
                Is.EqualTo(new[] { originalHead, sourceHead }));
            Assert.That(ReadHead(project.Path), Is.EqualTo(commit.Id));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.None));
            using var repository = new Repository(project.Path);
            Assert.That(
                repository.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.None));
            Assert.That(repository.RetrieveStatus().IsDirty, Is.False);
        });
    }

    [Test]
    public void CompleteMerge_WithUnresolvedConflict_IsRejectedWithoutMutation()
    {
        using var project = CreateSingleFileConflict(
            "merge-complete-conflict",
            "conflict.bin",
            [0, 1],
            [0, 2],
            [0, 3]);
        var service = new FolderProjectVersionControlService();
        var started = service.BeginMerge(project.Path, "source");
        var headBefore = ReadHead(project.Path);
        var indexBefore = File.ReadAllBytes(
            Path.Combine(project.Path, ".git", "index"));

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(project.Path, "must not commit"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.UnresolvedMergeConflicts));
            Assert.That(ReadHead(project.Path), Is.EqualTo(headBefore));
            Assert.That(
                File.ReadAllBytes(Path.Combine(project.Path, ".git", "index")),
                Is.EqualTo(indexBefore));
            Assert.That(
                service.GetMergeState(project.Path).Conflicts.Single().Id,
                Is.EqualTo(started.State.Conflicts.Single().Id));
        });
    }

    [Test]
    public void CompleteMerge_CommitSucceededButSessionCleanupFails_DoesNotRollbackOrRepeat()
    {
        using var project = CreateDivergentProject(
            "merge-complete-cleanup-failure",
            out var originalHead,
            out var sourceHead);
        var platform = new SessionDeleteFailurePlatform();
        var service = new FolderProjectVersionControlService(platform);
        service.BeginMerge(project.Path, "source");
        platform.FailNextSessionDelete = true;

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(project.Path, "merge once"));

        string committedHead;
        using (var repository = new Repository(project.Path))
        {
            committedHead = repository.Head.Tip!.Sha;
            Assert.That(
                repository.Head.Tip.Parents.Select(parent => parent.Sha),
                Is.EqualTo(new[] { originalHead, sourceHead }));
            Assert.That(
                repository.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.None));
        }
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(committedHead, Is.Not.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.None));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
            var repeated = Assert.Throws<FolderProjectVersionControlException>(
                () => service.CompleteMerge(project.Path, "must not repeat"));
            Assert.That(
                repeated!.Code,
                Is.EqualTo(FolderProjectVersionControlError.MergeNotActive));
            Assert.That(ReadHead(project.Path), Is.EqualTo(committedHead));
        });
    }

    [Test]
    public void AbortMerge_RestoresOriginalTreeAndPreservesUnrelatedUntrackedAndIgnored()
    {
        using var project = CreateDivergentProject(
            "merge-abort",
            out var originalHead,
            out _);
        File.AppendAllText(
            Path.Combine(project.Path, ".git", "info", "exclude"),
            $"{Environment.NewLine}ignored.tmp{Environment.NewLine}");
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");
        var untrackedPath = Path.Combine(project.Path, "notes.tmp");
        var ignoredPath = Path.Combine(project.Path, "ignored.tmp");
        File.WriteAllText(untrackedPath, "keep untracked");
        File.WriteAllText(ignoredPath, "keep ignored");

        service.AbortMerge(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(
                File.Exists(Path.Combine(project.Path, "source.txt")),
                Is.False);
            Assert.That(
                File.ReadAllText(untrackedPath),
                Is.EqualTo("keep untracked"));
            Assert.That(
                File.ReadAllText(ignoredPath),
                Is.EqualTo("keep ignored"));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
            using var repository = new Repository(project.Path);
            Assert.That(
                repository.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.None));
        });
    }

    [Test]
    public void AbortMerge_ResetFailsOnce_KeepsRetryableSession()
    {
        using var project = CreateDivergentProject(
            "merge-abort-retry",
            out var originalHead,
            out _);
        var platform = new ResetFailurePlatform();
        var service = new FolderProjectVersionControlService(platform);
        service.BeginMerge(project.Path, "source");
        platform.FailNextReset = true;

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.AbortMerge(project.Path));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
        });

        service.AbortMerge(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.None));
        });
    }

    [Test]
    public void AbortMerge_UnresolvedConflict_RestoresOriginalTree()
    {
        var currentBytes = new byte[] { 0, 2 };
        using var project = CreateSingleFileConflict(
            "merge-abort-conflict",
            "conflict.bin",
            [0, 1],
            currentBytes,
            [0, 3]);
        var service = new FolderProjectVersionControlService();
        var originalHead = ReadHead(project.Path);
        service.BeginMerge(project.Path, "source");

        service.AbortMerge(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(
                File.ReadAllBytes(
                    Path.Combine(project.Path, "conflict.bin")),
                Is.EqualTo(currentBytes));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.None));
        });
    }

    [Test]
    public void AbortMerge_UntrackedTargetCollision_IsRejectedBeforeReset()
    {
        using var project = CreateInitializedProject(
            "merge-abort-untracked-collision");
        var restorePath = Path.Combine(project.Path, "restore.txt");
        File.WriteAllText(restorePath, "original");
        var setup = new FolderProjectVersionControlService();
        setup.CommitAll(project.Path, "add restore target");
        setup.CreateBranch(project.Path, "source");
        File.WriteAllText(Path.Combine(project.Path, "current.txt"), "current");
        setup.CommitAll(project.Path, "current change");
        setup.SwitchBranch(project.Path, "source");
        File.Delete(restorePath);
        setup.CommitAll(project.Path, "source deletes restore target");
        setup.SwitchBranch(project.Path, "master");
        var platform = new ResetObservingPlatform();
        var service = new FolderProjectVersionControlService(platform);
        service.BeginMerge(project.Path, "source");
        Assert.That(File.Exists(restorePath), Is.False);
        File.WriteAllText(restorePath, "do not overwrite");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.AbortMerge(project.Path));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.WorkingTreeNotClean));
            Assert.That(platform.ResetCalls, Is.Zero);
            Assert.That(
                File.ReadAllText(restorePath),
                Is.EqualTo("do not overwrite"));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void AbortMerge_UntrackedCollisionCreatedDuringReset_IsPreserved()
    {
        using var project = CreateInitializedProject(
            "merge-abort-dynamic-collision");
        var restorePath = Path.Combine(project.Path, "restore.txt");
        File.WriteAllText(restorePath, "original");
        var setup = new FolderProjectVersionControlService();
        setup.CommitAll(project.Path, "add restore target");
        setup.CreateBranch(project.Path, "source");
        File.WriteAllText(Path.Combine(project.Path, "current.txt"), "current");
        setup.CommitAll(project.Path, "current change");
        setup.SwitchBranch(project.Path, "source");
        File.Delete(restorePath);
        setup.CommitAll(project.Path, "source deletes restore target");
        setup.SwitchBranch(project.Path, "master");
        var platform = new ResetCollisionInjectionPlatform(
            restorePath,
            "external");
        var service = new FolderProjectVersionControlService(platform);
        service.BeginMerge(project.Path, "source");
        Assert.That(File.Exists(restorePath), Is.False);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.AbortMerge(project.Path));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(File.ReadAllText(restorePath), Is.EqualTo("external"));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void GetMergeState_PreparedSessionWithNoNativeOperation_CleansUpAsIdle()
    {
        using var project = CreateInitializedProject("merge-restart-prepared");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        using (var repository = new Repository(project.Path))
        {
            var session = new FolderProjectMergeSession(
                1,
                Guid.NewGuid().ToString("N"),
                FolderProjectMergeSessionPhase.Prepared,
                repository.Head.CanonicalName,
                repository.Head.Tip!.Sha,
                repository.Branches["source"].CanonicalName,
                repository.Branches["source"].Tip.Sha,
                null,
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow);
            new FolderProjectMergeSessionStore(
                    repository.Info.Path,
                    new FolderProjectVersionControlPlatform())
                .Write(session);
        }

        var state = new FolderProjectVersionControlService()
            .GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(state.Phase, Is.EqualTo(FolderProjectMergePhase.None));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
        });
    }

    [Test]
    public void GetMergeState_PreparedSessionWithExternalDrift_RequiresRecovery()
    {
        using var project = CreateInitializedProject(
            "merge-restart-prepared-drift");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        using (var repository = new Repository(project.Path))
        {
            var session = new FolderProjectMergeSession(
                1,
                Guid.NewGuid().ToString("N"),
                FolderProjectMergeSessionPhase.Prepared,
                repository.Head.CanonicalName,
                repository.Head.Tip!.Sha,
                repository.Branches["source"].CanonicalName,
                repository.Branches["source"].Tip.Sha,
                null,
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow);
            new FolderProjectMergeSessionStore(
                    repository.Info.Path,
                    new FolderProjectVersionControlPlatform())
                .Write(session);
        }
        var driftPath = Path.Combine(project.Path, "external.tmp");
        File.WriteAllText(driftPath, "external");

        var state = new FolderProjectVersionControlService()
            .GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(File.ReadAllText(driftPath), Is.EqualTo("external"));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void GetMergeState_AwaitingSession_RestartsAsActive()
    {
        using var project = CreateDivergentProject(
            "merge-restart-awaiting",
            out var originalHead,
            out var sourceHead);
        new FolderProjectVersionControlService()
            .BeginMerge(project.Path, "source");

        var state = new FolderProjectVersionControlService()
            .GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
            Assert.That(state.OriginalHeadCommitId, Is.EqualTo(originalHead));
            Assert.That(state.SourceHeadCommitId, Is.EqualTo(sourceHead));
        });
    }

    [Test]
    public void GetMergeState_SourceReferenceDrift_RequiresRecovery()
    {
        using var project = CreateDivergentProject(
            "merge-restart-source-ref-drift",
            out var originalHead,
            out _);
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");
        using (var repository = new Repository(project.Path))
        {
            repository.Refs.UpdateTarget(
                repository.Branches["source"].Reference,
                repository.Head.Tip!.Id);
        }

        var state = new FolderProjectVersionControlService()
            .GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void GetMergeState_FastForwardCompletedBeforeException_ReconcilesAsIdle()
    {
        using var project = CreateInitializedProject("merge-restart-ff-done");
        var setup = new FolderProjectVersionControlService();
        setup.CreateBranch(project.Path, "source");
        setup.SwitchBranch(project.Path, "source");
        File.WriteAllText(Path.Combine(project.Path, "source.txt"), "source");
        var sourceHead = setup.CommitAll(project.Path, "source").Id;
        setup.SwitchBranch(project.Path, "master");
        var platform = new MergeThenThrowPlatform();
        var service = new FolderProjectVersionControlService(platform);

        Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "source"));
        var state = new FolderProjectVersionControlService()
            .GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(ReadHead(project.Path), Is.EqualTo(sourceHead));
            Assert.That(state.Phase, Is.EqualTo(FolderProjectMergePhase.None));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
        });
    }

    [Test]
    public void GetMergeState_NativeMergeCompletedBeforeException_ReconcilesAsAwaiting()
    {
        using var project = CreateDivergentProject(
            "merge-restart-native-done",
            out var originalHead,
            out var sourceHead);
        var platform = new MergeThenThrowPlatform();
        var service = new FolderProjectVersionControlService(platform);

        Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "source"));
        var state = new FolderProjectVersionControlService()
            .GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
            Assert.That(state.OriginalHeadCommitId, Is.EqualTo(originalHead));
            Assert.That(state.SourceHeadCommitId, Is.EqualTo(sourceHead));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            using var repository = new Repository(project.Path);
            Assert.That(
                repository.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.Merge));
        });
    }

    [Test]
    public void GetMergeState_NativeMergeCrashWithConflict_RequiresRecovery()
    {
        using var project = CreateSingleFileConflict(
            "merge-restart-native-conflict",
            "conflict.bin",
            [0, 1],
            [0, 2],
            [0, 3]);
        var service = new FolderProjectVersionControlService(
            new MergeThenThrowPlatform());

        Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "source"));
        var state = new FolderProjectVersionControlService()
            .GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
            using var repository = new Repository(project.Path);
            Assert.That(repository.Index.Conflicts, Is.Not.Empty);
        });
    }

    [Test]
    public void GetMergeState_NativeMergeCrashWithWorktreeDrift_RequiresRecovery()
    {
        using var project = CreateDivergentProject(
            "merge-restart-native-drift",
            out var originalHead,
            out _);
        var service = new FolderProjectVersionControlService(
            new MergeThenThrowPlatform());

        Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "source"));
        var driftPath = Path.Combine(project.Path, "source.txt");
        File.WriteAllText(driftPath, "external");
        var state = new FolderProjectVersionControlService()
            .GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.ReadAllText(driftPath), Is.EqualTo("external"));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void GetMergeState_AwaitingSessionAfterManualReset_RequiresRecovery()
    {
        using var project = CreateDivergentProject(
            "merge-awaiting-manual-reset",
            out _,
            out var sourceHead);
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");
        using (var repository = new Repository(project.Path))
        {
            repository.Reset(
                ResetMode.Hard,
                repository.Lookup<Commit>(sourceHead)!);
        }

        var state = service.GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(ReadHead(project.Path), Is.EqualTo(sourceHead));
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void GetMergeState_AwaitingSessionAfterManualMergeCommit_RequiresRecovery()
    {
        using var project = CreateDivergentProject(
            "merge-awaiting-manual-commit",
            out _,
            out _);
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");
        string manualCommitId;
        using (var repository = new Repository(project.Path))
        {
            var signature = new Signature(
                s_identity.Name,
                s_identity.Email,
                DateTimeOffset.Now);
            manualCommitId = repository.Commit(
                "manual merge",
                signature,
                signature).Sha;
        }

        var state = service.GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(ReadHead(project.Path), Is.EqualTo(manualCommitId));
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void GetMergeState_CorruptSession_ReturnsRecoveryWithoutMutation()
    {
        using var project = CreateInitializedProject("merge-restart-corrupt");
        var headBefore = ReadHead(project.Path);
        File.WriteAllText(SessionPath(project.Path), "{not valid json");

        var state = new FolderProjectVersionControlService()
            .GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(ReadHead(project.Path), Is.EqualTo(headBefore));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void GetMergeState_ExternalNativeMerge_ReturnsRecoveryWithoutGuessing()
    {
        using var project = CreateDivergentProject(
            "merge-restart-external",
            out var originalHead,
            out _);
        using (var repository = new Repository(project.Path))
        {
            var signature = new Signature(
                s_identity.Name,
                s_identity.Email,
                DateTimeOffset.Now);
            repository.Merge(
                repository.Branches["source"].Tip,
                signature,
                new MergeOptions
                {
                    CommitOnSuccess = false,
                    FastForwardStrategy = FastForwardStrategy.NoFastForward,
                });
        }

        var state = new FolderProjectVersionControlService()
            .GetMergeState(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
        });
    }

    [Test]
    public void BeginMerge_UnrelatedHistory_IsRejectedWithoutSession()
    {
        using var project = CreateInitializedProject("merge-unrelated");
        using (var repository = new Repository(project.Path))
        {
            var signature = new Signature(
                s_identity.Name,
                s_identity.Email,
                DateTimeOffset.Now);
            var unrelated = repository.ObjectDatabase.CreateCommit(
                signature,
                signature,
                "unrelated",
                repository.Head.Tip!.Tree,
                [],
                prettifyMessage: true);
            repository.Branches.Add("unrelated", unrelated);
        }
        var service = new FolderProjectVersionControlService();

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "unrelated"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.UnrelatedHistories));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
        });
    }

    [Test]
    public void BeginMerge_RemoteOnlyName_IsRejected()
    {
        using var project = CreateInitializedProject("merge-remote-only");
        using (var repository = new Repository(project.Path))
        {
            repository.Refs.Add(
                "refs/remotes/origin/remote-only",
                repository.Head.Tip!.Id);
        }
        var service = new FolderProjectVersionControlService();

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "remote-only"));

        Assert.That(
            exception!.Code,
            Is.EqualTo(FolderProjectVersionControlError.BranchNotFound));
    }

    [TestCase(Mode.SymbolicLink)]
    [TestCase(Mode.GitLink)]
    [TestCase(Mode.NonExecutableGroupWritableFile)]
    public void BeginMerge_UnsupportedSourceEntry_IsRejectedBeforeNativeMerge(
        Mode mode)
    {
        using var project = CreateInitializedProject(
            $"merge-unsupported-{mode}");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        using (var repository = new Repository(project.Path))
        {
            var parent = repository.Head.Tip!;
            ObjectId targetId;
            if (mode == Mode.GitLink)
            {
                targetId = parent.Id;
            }
            else
            {
                using var content = new MemoryStream("base.bin"u8.ToArray());
                targetId = repository.ObjectDatabase.CreateBlob(content).Id;
            }
            var tree = mode == Mode.NonExecutableGroupWritableFile
                ? CreateSingleEntryTree(
                    repository,
                    "unsupported-entry",
                    targetId,
                    "100664")
                : CreateTreeWithEntry(
                    repository,
                    parent.Tree,
                    "unsupported-entry",
                    targetId,
                    mode);
            var signature = new Signature(
                s_identity.Name,
                s_identity.Email,
                DateTimeOffset.Now);
            var commit = repository.ObjectDatabase.CreateCommit(
                signature,
                signature,
                "unsupported source entry",
                tree,
                [parent],
                prettifyMessage: true);
            repository.Refs.UpdateTarget(
                repository.Branches["source"].Reference,
                commit.Id);
        }
        var headBefore = ReadHead(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "source"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.UnsupportedCommitPath));
            Assert.That(ReadHead(project.Path), Is.EqualTo(headBefore));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
        });
    }

    [Test]
    public void BeginMerge_IgnoredTargetCollision_IsRejectedWithoutOverwrite()
    {
        using var project = CreateInitializedProject(
            "merge-ignored-collision");
        var setup = new FolderProjectVersionControlService();
        setup.CreateBranch(project.Path, "source");
        setup.SwitchBranch(project.Path, "source");
        File.WriteAllText(
            Path.Combine(project.Path, "ignored-target.txt"),
            "source");
        setup.CommitAll(project.Path, "add ignored target");
        setup.SwitchBranch(project.Path, "master");
        File.AppendAllText(
            Path.Combine(project.Path, ".git", "info", "exclude"),
            $"{Environment.NewLine}ignored-target.txt{Environment.NewLine}");
        var collisionPath = Path.Combine(
            project.Path,
            "ignored-target.txt");
        File.WriteAllText(collisionPath, "keep local");
        var headBefore = ReadHead(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => setup.BeginMerge(project.Path, "source"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.WorkingTreeNotClean));
            Assert.That(
                File.ReadAllText(collisionPath),
                Is.EqualTo("keep local"));
            Assert.That(ReadHead(project.Path), Is.EqualTo(headBefore));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
        });
    }

    [Test]
    public void BeginMerge_UntrackedCollisionCreatedDuringNativeMerge_IsPreserved()
    {
        using var project = CreateInitializedProject(
            "merge-dynamic-collision");
        var setup = new FolderProjectVersionControlService();
        setup.CreateBranch(project.Path, "source");
        setup.SwitchBranch(project.Path, "source");
        var collisionPath = Path.Combine(project.Path, "target.txt");
        File.WriteAllText(collisionPath, "source");
        setup.CommitAll(project.Path, "add target");
        setup.SwitchBranch(project.Path, "master");
        var headBefore = ReadHead(project.Path);
        var platform = new MergeCollisionInjectionPlatform(
            collisionPath,
            "external");
        var service = new FolderProjectVersionControlService(platform);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "source"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(File.ReadAllText(collisionPath), Is.EqualTo("external"));
            Assert.That(ReadHead(project.Path), Is.EqualTo(headBefore));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
        });
    }

    [Test]
    public void CompleteMerge_CommitThrowsAfterSuccess_ReturnsVerifiedCommit()
    {
        using var project = CreateDivergentProject(
            "merge-complete-post-commit",
            out var originalHead,
            out var sourceHead);
        var platform = new CommitThenThrowPlatform();
        var service = new FolderProjectVersionControlService(platform);
        service.BeginMerge(project.Path, "source");

        var commit = service.CompleteMerge(project.Path, "merge once");

        Assert.Multiple(() =>
        {
            Assert.That(commit.Id, Is.EqualTo(platform.CommittedId));
            Assert.That(
                commit.ParentIds,
                Is.EqualTo(new[] { originalHead, sourceHead }));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.None));
        });
    }

    [Test]
    public void CompleteMerge_CommitWithUnexpectedTree_RequiresRecovery()
    {
        using var project = CreateDivergentProject(
            "merge-complete-tree-drift",
            out var originalHead,
            out _);
        var unexpectedPath = Path.Combine(project.Path, "unexpected.txt");
        var platform = new StageUnexpectedFileBeforeCommitPlatform(
            unexpectedPath);
        var service = new FolderProjectVersionControlService(platform);
        service.BeginMerge(project.Path, "source");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(project.Path, "must verify tree"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.MergeRecoveryRequired));
            Assert.That(ReadHead(project.Path), Is.Not.EqualTo(originalHead));
            Assert.That(File.ReadAllText(unexpectedPath), Is.EqualTo("external"));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
        });
    }

    [Test]
    public void CompleteMerge_UntrackedFileCreatedAfterCommit_RequiresRecovery()
    {
        using var project = CreateDivergentProject(
            "merge-complete-postflight-untracked",
            out var originalHead,
            out _);
        var untrackedPath = Path.Combine(project.Path, "post-commit.tmp");
        var platform = new CreateUntrackedFileAfterCommitPlatform(
            untrackedPath);
        var service = new FolderProjectVersionControlService(platform);
        service.BeginMerge(project.Path, "source");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(
                project.Path,
                "must verify worktree"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.MergeRecoveryRequired));
            Assert.That(ReadHead(project.Path), Is.Not.EqualTo(originalHead));
            Assert.That(File.ReadAllText(untrackedPath), Is.EqualTo("external"));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
        });
    }

    [Test]
    public void CompleteMerge_CommitFailsBeforeMutation_RestoresRetryableSession()
    {
        using var project = CreateDivergentProject(
            "merge-complete-retry",
            out var originalHead,
            out _);
        var platform = new FailCommitOncePlatform();
        var service = new FolderProjectVersionControlService(platform);
        service.BeginMerge(project.Path, "source");
        platform.FailNextCommit = true;

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(project.Path, "first try"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
        });

        var commit = service.CompleteMerge(project.Path, "second try");
        Assert.That(commit.ParentIds[0], Is.EqualTo(originalHead));
        Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
    }

    [Test]
    public void CompleteMerge_EmptyMessage_IsRejectedWithoutChangingActiveMerge()
    {
        using var project = CreateDivergentProject(
            "merge-complete-empty-message",
            out var originalHead,
            out _);
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(project.Path, "   "));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.EmptyCommitMessage));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
        });
    }

    [Test]
    public void CompleteMerge_ExtraUntrackedFile_IsRejectedAsRecovery()
    {
        using var project = CreateDivergentProject(
            "merge-complete-untracked",
            out var originalHead,
            out _);
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");
        var untrackedPath = Path.Combine(project.Path, "untracked.tmp");
        File.WriteAllText(untrackedPath, "keep");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(project.Path, "must not commit"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.MergeRecoveryRequired));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.ReadAllText(untrackedPath), Is.EqualTo("keep"));
        });
    }

    [Test]
    public void CompleteMerge_MissingIdentity_IsRejectedWithoutCommit()
    {
        using var project = CreateDivergentProject(
            "merge-complete-identity",
            out var originalHead,
            out _);
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");
        using (var repository = new Repository(project.Path))
        {
            repository.Config.Unset(
                "user.name",
                ConfigurationLevel.Local);
        }

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(project.Path, "must not commit"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.IdentityMissing));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void CompleteMerge_UnreadableWorkingPath_IsRejectedAsRecovery()
    {
        using var project = CreateDivergentProject(
            "merge-complete-unreadable",
            out var originalHead,
            out _);
        var platform = new UnreadableMergePlatform(
            Path.Combine(project.Path, "source.txt"));
        var service = new FolderProjectVersionControlService(platform);
        service.BeginMerge(project.Path, "source");
        platform.IsUnreadable = true;

        var state = service.GetMergeState(project.Path);
        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteMerge(project.Path, "must not commit"));

        Assert.Multiple(() =>
        {
            Assert.That(
                state.Phase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.MergeRecoveryRequired));
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
        });
    }

    [Test]
    public void BeginMerge_UntrackedWorkingFile_IsRejectedBeforeSession()
    {
        using var project = CreateInitializedProject("merge-start-untracked");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        service.SwitchBranch(project.Path, "source");
        File.WriteAllText(Path.Combine(project.Path, "source.txt"), "source");
        service.CommitAll(project.Path, "source");
        service.SwitchBranch(project.Path, "master");
        var untrackedPath = Path.Combine(project.Path, "untracked.tmp");
        File.WriteAllText(untrackedPath, "keep");
        var headBefore = ReadHead(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "source"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.WorkingTreeNotClean));
            Assert.That(ReadHead(project.Path), Is.EqualTo(headBefore));
            Assert.That(File.ReadAllText(untrackedPath), Is.EqualTo("keep"));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
        });
    }

    [Test]
    public void BeginMerge_CurrentBranch_IsRejected()
    {
        using var project = CreateInitializedProject("merge-current");
        var service = new FolderProjectVersionControlService();

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "master"));

        Assert.That(
            exception!.Code,
            Is.EqualTo(FolderProjectVersionControlError.MergeSourceIsCurrent));
    }

    [Test]
    public void BeginMerge_DetachedHead_IsRejected()
    {
        using var project = CreateInitializedProject("merge-detached");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        using (var repository = new Repository(project.Path))
        {
            Commands.Checkout(repository, repository.Head.Tip!);
        }

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.BeginMerge(project.Path, "source"));

        Assert.That(
            exception!.Code,
            Is.EqualTo(
                FolderProjectVersionControlError.UnsupportedOperationState));
    }

    [Test]
    public void BeginMerge_IndexLock_IsRejectedAsBusy()
    {
        using var project = CreateInitializedProject("merge-index-lock");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        var lockPath = Path.Combine(project.Path, ".git", "index.lock");
        File.WriteAllText(lockPath, "busy");
        try
        {
            var exception =
                Assert.Throws<FolderProjectVersionControlException>(
                    () => service.BeginMerge(project.Path, "source"));
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryBusy));
        }
        finally
        {
            File.Delete(lockPath);
        }
    }

    [Test]
    public void AbortMerge_IgnoredTargetCollision_IsRejectedBeforeReset()
    {
        using var project = CreateInitializedProject(
            "merge-abort-ignored-collision");
        var restorePath = Path.Combine(project.Path, "restore.txt");
        File.WriteAllText(restorePath, "original");
        var setup = new FolderProjectVersionControlService();
        setup.CommitAll(project.Path, "add restore target");
        setup.CreateBranch(project.Path, "source");
        File.WriteAllText(Path.Combine(project.Path, "current.txt"), "current");
        setup.CommitAll(project.Path, "current change");
        setup.SwitchBranch(project.Path, "source");
        File.Delete(restorePath);
        setup.CommitAll(project.Path, "source deletes restore target");
        setup.SwitchBranch(project.Path, "master");
        var platform = new ResetObservingPlatform();
        var service = new FolderProjectVersionControlService(platform);
        service.BeginMerge(project.Path, "source");
        File.AppendAllText(
            Path.Combine(project.Path, ".git", "info", "exclude"),
            $"{Environment.NewLine}restore.txt{Environment.NewLine}");
        File.WriteAllText(restorePath, "do not overwrite");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.AbortMerge(project.Path));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.WorkingTreeNotClean));
            Assert.That(platform.ResetCalls, Is.Zero);
            Assert.That(
                File.ReadAllText(restorePath),
                Is.EqualTo("do not overwrite"));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.True);
        });
    }

    [Test]
    public void AbortMerge_AbortingMarkerBeforeReset_IsDirectlyRetryable()
    {
        using var project = CreateDivergentProject(
            "merge-abort-marker-before-reset",
            out var originalHead,
            out _);
        var service = new FolderProjectVersionControlService();
        service.BeginMerge(project.Path, "source");
        using (var repository = new Repository(project.Path))
        {
            var store = new FolderProjectMergeSessionStore(
                repository.Info.Path,
                new FolderProjectVersionControlPlatform());
            store.Write(
                store.Read()! with
                {
                    Phase = FolderProjectMergeSessionPhase.Aborting,
                });
        }

        service.AbortMerge(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.None));
        });
    }

    [Test]
    public void GetMergeState_AbortingEqualTreeMerge_RemainsRetryable()
    {
        using var project = CreateInitializedProject(
            "merge-abort-equal-tree-recovery");
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        var currentPath = Path.Combine(project.Path, "current.txt");
        File.WriteAllText(currentPath, "current");
        service.CommitAll(project.Path, "current add");
        File.Delete(currentPath);
        var originalHead = service.CommitAll(
            project.Path,
            "current remove").Id;
        service.SwitchBranch(project.Path, "source");
        var sourcePath = Path.Combine(project.Path, "source.txt");
        File.WriteAllText(sourcePath, "source");
        service.CommitAll(project.Path, "source add");
        File.Delete(sourcePath);
        service.CommitAll(project.Path, "source remove");
        service.SwitchBranch(project.Path, "master");

        service.BeginMerge(project.Path, "source");
        using (var repository = new Repository(project.Path))
        {
            var store = new FolderProjectMergeSessionStore(
                repository.Info.Path,
                new FolderProjectVersionControlPlatform());
            store.Write(
                store.Read()! with
                {
                    Phase = FolderProjectMergeSessionPhase.Aborting,
                });

            Assert.Multiple(() =>
            {
                Assert.That(
                    repository.Head.Tip!.Tree.Id,
                    Is.EqualTo(
                        repository.Branches["source"]!.Tip!.Tree.Id));
                Assert.That(
                    repository.Info.CurrentOperation,
                    Is.EqualTo(CurrentOperation.Merge));
                Assert.That(repository.RetrieveStatus().IsDirty, Is.False);
                Assert.That(
                    File.Exists(
                        Path.Combine(repository.Info.Path, "MERGE_HEAD")),
                    Is.True);
            });
        }

        var state = service.GetMergeState(project.Path);

        using (var repository = new Repository(project.Path))
        {
            var store = new FolderProjectMergeSessionStore(
                repository.Info.Path,
                new FolderProjectVersionControlPlatform());
            Assert.Multiple(() =>
            {
                Assert.That(
                    state.Phase,
                    Is.EqualTo(FolderProjectMergePhase.ReadyToCommit));
                Assert.That(
                    store.Read()!.Phase,
                    Is.EqualTo(
                        FolderProjectMergeSessionPhase.AwaitingUser));
                Assert.That(
                    File.Exists(
                        Path.Combine(repository.Info.Path, "MERGE_HEAD")),
                    Is.True);
            });
        }

        service.AbortMerge(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(ReadHead(project.Path), Is.EqualTo(originalHead));
            Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.None));
        });
    }

    [Test]
    public void MergeSessionStore_UnsafeFingerprintPath_IsRejected()
    {
        using var project = CreateInitializedProject("merge-session-path");
        using var repository = new Repository(project.Path);
        var session = new FolderProjectMergeSession(
            1,
            Guid.NewGuid().ToString("N"),
            FolderProjectMergeSessionPhase.Prepared,
            repository.Head.CanonicalName,
            repository.Head.Tip!.Sha,
            "refs/heads/source",
            repository.Head.Tip.Sha,
            null,
            new Dictionary<string, string>
            {
                ["../escape"] = "missing",
            },
            DateTimeOffset.UtcNow);
        var store = new FolderProjectMergeSessionStore(
            repository.Info.Path,
            new FolderProjectVersionControlPlatform());

        Assert.Throws<InvalidDataException>(() => store.Write(session));
        Assert.That(File.Exists(SessionPath(project.Path)), Is.False);
    }

    private static TemporaryDirectory CreateInitializedProject(string suffix)
    {
        var project = new TemporaryDirectory(suffix);
        File.WriteAllBytes(Path.Combine(project.Path, "base.bin"), [1, 2, 3]);
        new FolderProjectVersionControlService().Initialize(project.Path, s_identity);
        return project;
    }

    private static TemporaryDirectory CreateDivergentProject(
        string suffix,
        out string originalHead,
        out string sourceHead)
    {
        var project = CreateInitializedProject(suffix);
        var service = new FolderProjectVersionControlService();
        service.CreateBranch(project.Path, "source");
        File.WriteAllText(Path.Combine(project.Path, "current.txt"), "current");
        originalHead = service.CommitAll(project.Path, "current change").Id;
        service.SwitchBranch(project.Path, "source");
        File.WriteAllText(Path.Combine(project.Path, "source.txt"), "source");
        sourceHead = service.CommitAll(project.Path, "source change").Id;
        service.SwitchBranch(project.Path, "master");
        return project;
    }

    private static TemporaryDirectory CreateSingleFileConflict(
        string suffix,
        string repositoryPath,
        byte[] ancestorBytes,
        byte[] currentBytes,
        byte[] incomingBytes)
    {
        var project = CreateInitializedProject(suffix);
        var fullPath = Path.Combine(project.Path, repositoryPath);
        File.WriteAllBytes(fullPath, ancestorBytes);
        var service = new FolderProjectVersionControlService();
        service.CommitAll(project.Path, "add conflict target");
        service.CreateBranch(project.Path, "source");
        File.WriteAllBytes(fullPath, currentBytes);
        service.CommitAll(project.Path, "current content");
        service.SwitchBranch(project.Path, "source");
        File.WriteAllBytes(fullPath, incomingBytes);
        service.CommitAll(project.Path, "incoming content");
        service.SwitchBranch(project.Path, "master");
        return project;
    }

    private static Tree CreateTreeWithEntry(
        Repository repository,
        Tree baseTree,
        string repositoryPath,
        ObjectId targetId,
        Mode mode)
    {
        var definition = TreeDefinition.From(baseTree);
        definition.Add(repositoryPath, targetId, mode);
        return repository.ObjectDatabase.CreateTree(definition);
    }

    private static Tree CreateSingleEntryTree(
        Repository repository,
        string repositoryPath,
        ObjectId targetId,
        string mode)
    {
        var entryPrefix = Encoding.ASCII.GetBytes(
            $"{mode} {repositoryPath}\0");
        var entry = new byte[entryPrefix.Length + 20];
        entryPrefix.CopyTo(entry, 0);
        Convert.FromHexString(targetId.Sha).CopyTo(
            entry,
            entryPrefix.Length);
        var treeId = WriteLooseObject(
            repository.Info.Path,
            "tree",
            entry);
        return repository.Lookup<Tree>(treeId)!;
    }

    private static ObjectId WriteLooseObject(
        string metadataPath,
        string type,
        byte[] content)
    {
        var header = Encoding.ASCII.GetBytes(
            $"{type} {content.Length}\0");
        var objectBytes = new byte[header.Length + content.Length];
        header.CopyTo(objectBytes, 0);
        content.CopyTo(objectBytes, header.Length);
        var objectId = Convert.ToHexString(SHA1.HashData(objectBytes))
            .ToLowerInvariant();
        var objectDirectory = Path.Combine(
            metadataPath,
            "objects",
            objectId[..2]);
        Directory.CreateDirectory(objectDirectory);
        var objectPath = Path.Combine(
            objectDirectory,
            objectId[2..]);
        using (var file = new FileStream(
                   objectPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        using (var compressed = new ZLibStream(
                   file,
                   CompressionLevel.SmallestSize))
        {
            compressed.Write(objectBytes);
        }

        return new ObjectId(objectId);
    }

    private static void AssertMergeSide(
        FolderProjectMergeSide? side,
        string repositoryPath,
        byte[] expectedBytes)
    {
        Assert.Multiple(() =>
        {
            Assert.That(side, Is.Not.Null);
            Assert.That(side!.RepositoryPath, Is.EqualTo(repositoryPath));
            Assert.That(side.BlobId, Has.Length.EqualTo(40));
            Assert.That(
                side.Mode,
                Is.EqualTo(FolderProjectGitFileMode.NonExecutable));
            Assert.That(side.Size, Is.EqualTo(expectedBytes.Length));
            Assert.That(side.IsBinary, Is.True);
        });
    }

    private static string ReadHead(string projectPath)
    {
        using var repository = new Repository(projectPath);
        return repository.Head.Tip!.Sha;
    }

    private static string SessionPath(string projectPath)
    {
        return Path.Combine(
            projectPath,
            ".git",
            "ae-folder-project-merge.json");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; }

        public TemporaryDirectory(string suffix)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ae-folder-git-{suffix}-{Guid.NewGuid():N}");
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

    private sealed class ConflictWriteFailurePlatform :
        FolderProjectVersionControlPlatform
    {
        private readonly string _conflictPath;

        public ConflictWriteFailurePlatform(string conflictPath)
        {
            _conflictPath = Path.GetFullPath(conflictPath);
        }

        public bool FailNextConflictWrite { get; set; }

        public override void MoveFile(
            string sourcePath,
            string destinationPath,
            bool overwrite)
        {
            if (FailNextConflictWrite &&
                Path.GetFullPath(destinationPath).Equals(
                    _conflictPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                FailNextConflictWrite = false;
                throw new IOException("Injected conflict write failure.");
            }

            base.MoveFile(sourcePath, destinationPath, overwrite);
        }
    }

    private sealed class SessionDeleteFailurePlatform :
        FolderProjectVersionControlPlatform
    {
        public bool FailNextSessionDelete { get; set; }

        public override void DeleteFile(string path)
        {
            if (FailNextSessionDelete &&
                Path.GetFileName(path).Equals(
                    "ae-folder-project-merge.json",
                    StringComparison.Ordinal))
            {
                FailNextSessionDelete = false;
                throw new IOException("Injected session cleanup failure.");
            }

            base.DeleteFile(path);
        }
    }

    private sealed class SessionMoveThenThrowPlatform :
        FolderProjectVersionControlPlatform
    {
        public bool FailNextSessionMoveAfterSuccess { get; set; }

        public override void MoveFile(
            string sourcePath,
            string destinationPath,
            bool overwrite)
        {
            base.MoveFile(sourcePath, destinationPath, overwrite);
            if (FailNextSessionMoveAfterSuccess &&
                Path.GetFileName(destinationPath).Equals(
                    "ae-folder-project-merge.json",
                    StringComparison.Ordinal))
            {
                FailNextSessionMoveAfterSuccess = false;
                throw new IOException(
                    "Injected post-session-replace failure.");
            }
        }
    }

    private sealed class ConcurrentConflictWriteFailurePlatform :
        FolderProjectVersionControlPlatform
    {
        private readonly string _conflictPath;
        private readonly byte[] _externalBytes;

        public ConcurrentConflictWriteFailurePlatform(
            string conflictPath,
            byte[] externalBytes)
        {
            _conflictPath = conflictPath;
            _externalBytes = externalBytes;
        }

        public bool FailNextSessionMoveAfterExternalWrite { get; set; }

        public override void MoveFile(
            string sourcePath,
            string destinationPath,
            bool overwrite)
        {
            base.MoveFile(sourcePath, destinationPath, overwrite);
            if (FailNextSessionMoveAfterExternalWrite &&
                Path.GetFileName(destinationPath).Equals(
                    "ae-folder-project-merge.json",
                    StringComparison.Ordinal))
            {
                FailNextSessionMoveAfterExternalWrite = false;
                File.WriteAllBytes(_conflictPath, _externalBytes);
                throw new IOException(
                    "Injected post-session-replace failure.");
            }
        }
    }

    private sealed class ResetFailurePlatform :
        FolderProjectVersionControlPlatform
    {
        public bool FailNextReset { get; set; }

        public override void Reset(
            Repository repository,
            Commit commit,
            CheckoutOptions options)
        {
            if (FailNextReset)
            {
                FailNextReset = false;
                throw new IOException("Injected reset failure.");
            }

            base.Reset(repository, commit, options);
        }
    }

    private sealed class ResetObservingPlatform :
        FolderProjectVersionControlPlatform
    {
        public int ResetCalls { get; private set; }

        public override void Reset(
            Repository repository,
            Commit commit,
            CheckoutOptions options)
        {
            ResetCalls++;
            base.Reset(repository, commit, options);
        }
    }

    private sealed class ResetCollisionInjectionPlatform :
        FolderProjectVersionControlPlatform
    {
        private readonly string _collisionPath;
        private readonly string _content;

        public ResetCollisionInjectionPlatform(
            string collisionPath,
            string content)
        {
            _collisionPath = collisionPath;
            _content = content;
        }

        public override void Reset(
            Repository repository,
            Commit commit,
            CheckoutOptions options)
        {
            File.WriteAllText(_collisionPath, _content);
            base.Reset(repository, commit, options);
        }
    }

    private sealed class MergeThenThrowPlatform :
        FolderProjectVersionControlPlatform
    {
        public override MergeResult Merge(
            Repository repository,
            Commit commit,
            Signature signature,
            MergeOptions options)
        {
            base.Merge(repository, commit, signature, options);
            throw new IOException("Injected post-merge failure.");
        }
    }

    private sealed class MergeCollisionInjectionPlatform :
        FolderProjectVersionControlPlatform
    {
        private readonly string _collisionPath;
        private readonly string _content;

        public MergeCollisionInjectionPlatform(
            string collisionPath,
            string content)
        {
            _collisionPath = collisionPath;
            _content = content;
        }

        public override MergeResult Merge(
            Repository repository,
            Commit commit,
            Signature signature,
            MergeOptions options)
        {
            File.WriteAllText(_collisionPath, _content);
            return base.Merge(repository, commit, signature, options);
        }
    }

    private sealed class CommitThenThrowPlatform :
        FolderProjectVersionControlPlatform
    {
        public string? CommittedId { get; private set; }

        public override Commit Commit(
            Repository repository,
            string message,
            Signature signature)
        {
            var commit = base.Commit(repository, message, signature);
            CommittedId = commit.Sha;
            throw new IOException("Injected post-commit failure.");
        }
    }

    private sealed class StageUnexpectedFileBeforeCommitPlatform :
        FolderProjectVersionControlPlatform
    {
        private readonly string _unexpectedPath;

        public StageUnexpectedFileBeforeCommitPlatform(
            string unexpectedPath)
        {
            _unexpectedPath = unexpectedPath;
        }

        public override Commit Commit(
            Repository repository,
            string message,
            Signature signature)
        {
            File.WriteAllText(_unexpectedPath, "external");
            Commands.Stage(
                repository,
                Path.GetFileName(_unexpectedPath));
            return base.Commit(repository, message, signature);
        }
    }

    private sealed class CreateUntrackedFileAfterCommitPlatform :
        FolderProjectVersionControlPlatform
    {
        private readonly string _untrackedPath;

        public CreateUntrackedFileAfterCommitPlatform(
            string untrackedPath)
        {
            _untrackedPath = untrackedPath;
        }

        public override Commit Commit(
            Repository repository,
            string message,
            Signature signature)
        {
            var commit = base.Commit(repository, message, signature);
            File.WriteAllText(_untrackedPath, "external");
            return commit;
        }
    }

    private sealed class FailCommitOncePlatform :
        FolderProjectVersionControlPlatform
    {
        public bool FailNextCommit { get; set; }

        public override Commit Commit(
            Repository repository,
            string message,
            Signature signature)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new IOException("Injected commit failure.");
            }

            return base.Commit(repository, message, signature);
        }
    }

    private sealed class UnreadableMergePlatform :
        FolderProjectVersionControlPlatform
    {
        private readonly string _unreadablePath;

        public UnreadableMergePlatform(string unreadablePath)
        {
            _unreadablePath = Path.GetFullPath(unreadablePath);
        }

        public bool IsUnreadable { get; set; }

        public override Stream OpenReadForStatus(string path)
        {
            if (IsUnreadable &&
                Path.GetFullPath(path).Equals(
                    _unreadablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "Injected unreadable path.");
            }

            return base.OpenReadForStatus(path);
        }
    }
}
