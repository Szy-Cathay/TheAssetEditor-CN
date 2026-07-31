using LibGit2Sharp;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Test.Shared.Core.PackFiles;

public class FolderProjectVersionControlRestoreAndBranchTests
{
    private static readonly FolderProjectGitIdentity s_identity =
        new("AE 用户", "ae-user@example.invalid");

    [Test]
    public void RestoreFile_RawBinary_LeavesHeadIndexAndOtherFilesUnchanged()
    {
        using var project = new TemporaryDirectory("restore-binary");
        var targetPath = Path.Combine(project.Path, "db", "target.bin");
        var otherPath = Path.Combine(project.Path, "db", "other.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var originalBytes = new byte[] { 0, 13, 10, 255, 42 };
        File.WriteAllBytes(targetPath, originalBytes);
        File.WriteAllBytes(otherPath, [9, 8, 7]);
        var service = new FolderProjectVersionControlService();
        var original = service.Initialize(project.Path, s_identity);
        File.WriteAllBytes(targetPath, [1, 2, 3]);
        service.CommitAll(project.Path, "new target");
        var before = CaptureRepository(project.Path);

        var restored = service.RestoreFile(
            project.Path,
            original.Id,
            "db/target.bin");

        var after = CaptureRepository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(restored.CommitId, Is.EqualTo(original.Id));
            Assert.That(restored.RepositoryPath, Is.EqualTo("db/target.bin"));
            Assert.That(restored.Size, Is.EqualTo(originalBytes.Length));
            Assert.That(File.ReadAllBytes(targetPath), Is.EqualTo(originalBytes));
            Assert.That(File.ReadAllBytes(otherPath), Is.EqualTo(new byte[] { 9, 8, 7 }));
            Assert.That(after.HeadId, Is.EqualTo(before.HeadId));
            Assert.That(after.HeadName, Is.EqualTo(before.HeadName));
            Assert.That(after.IndexBytes, Is.EqualTo(before.IndexBytes));
        });
    }

    [TestCase("staged")]
    [TestCase("unstaged")]
    [TestCase("untracked")]
    [TestCase("deleted")]
    [TestCase("ignored")]
    public void RestoreFile_DirtyTarget_DefaultRejectsWithoutChanges(
        string dirtyState)
    {
        using var project = CreateRestoreProject(dirtyState, out var historical);
        var service = new FolderProjectVersionControlService();
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(
                project.Path,
                historical,
                "target.bin"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.WorkingTreeNotClean));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RestoreFile_RenamedTarget_RejectsWithoutChanges(
        bool overwrite)
    {
        using var project = new TemporaryDirectory("restore-renamed");
        var targetPath = Path.Combine(project.Path, "target.bin");
        File.WriteAllBytes(targetPath, [1, 2, 3]);
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        File.Move(
            targetPath,
            Path.Combine(project.Path, "renamed.bin"));
        using (var repository = new Repository(project.Path))
            Commands.Stage(repository, "*");
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(
                project.Path,
                initial.Id,
                "target.bin",
                overwrite));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.WorkingTreeNotClean));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RestoreFile_UntrackedDirectoryOccupyingTarget_Rejects(
        bool overwrite)
    {
        using var project = new TemporaryDirectory("restore-untracked-directory");
        var targetPath = Path.Combine(project.Path, "target");
        File.WriteAllBytes(targetPath, [1, 2, 3]);
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        File.Delete(targetPath);
        service.CommitAll(project.Path, "delete target");
        Directory.CreateDirectory(targetPath);
        File.WriteAllBytes(
            Path.Combine(targetPath, "child.bin"),
            [4, 5, 6]);
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(
                project.Path,
                initial.Id,
                "target",
                overwrite));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.WorkingTreeNotClean));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [TestCase("staged")]
    [TestCase("ignored")]
    public void RestoreFile_OverwriteDirtyTarget_PreservesIndex(
        string dirtyState)
    {
        using var project = CreateRestoreProject(dirtyState, out var historical);
        var service = new FolderProjectVersionControlService();
        var indexBefore = File.ReadAllBytes(
            Path.Combine(project.Path, ".git", "index"));
        var headBefore = ReadHead(project.Path);

        service.RestoreFile(
            project.Path,
            historical,
            "target.bin",
            overwriteWorkingChange: true);

        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllBytes(Path.Combine(project.Path, "target.bin")),
                Is.EqualTo(new byte[] { 0, 255, 13, 10 }));
            Assert.That(
                File.ReadAllBytes(Path.Combine(project.Path, ".git", "index")),
                Is.EqualTo(indexBefore));
            Assert.That(ReadHead(project.Path), Is.EqualTo(headBefore));
        });
    }

    [TestCase("")]
    [TestCase("abc")]
    [TestCase("000000000000000000000000000000000000000g")]
    public void RestoreFile_InvalidCommitId_Rejects(string commitId)
    {
        using var project = new TemporaryDirectory("restore-invalid-id");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(project.Path, commitId, "target.bin"));

        Assert.That(
            exception!.Code,
            Is.EqualTo(FolderProjectVersionControlError.InvalidCommitId));
    }

    [Test]
    public void RestoreFile_UppercaseCommitId_IsAccepted()
    {
        using var project = new TemporaryDirectory("restore-uppercase-id");
        var targetPath = Path.Combine(project.Path, "target.bin");
        File.WriteAllBytes(targetPath, [1, 2, 3]);
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        File.WriteAllBytes(targetPath, [4, 5, 6]);
        service.CommitAll(project.Path, "later");

        var restored = service.RestoreFile(
            project.Path,
            initial.Id.ToUpperInvariant(),
            "target.bin");

        Assert.Multiple(() =>
        {
            Assert.That(restored.CommitId, Is.EqualTo(initial.Id));
            Assert.That(File.ReadAllBytes(targetPath), Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void RestoreFile_MissingOrNonCommitObject_ReportsCommitNotFound()
    {
        using var project = new TemporaryDirectory("restore-noncommit");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        string blobId;
        using (var repository = new Repository(project.Path))
        using (var content = new MemoryStream([1, 2, 3]))
            blobId = repository.ObjectDatabase.CreateBlob(content).Sha;

        Assert.Multiple(() =>
        {
            Assert.That(
                Assert.Throws<FolderProjectVersionControlException>(
                    () => service.RestoreFile(
                        project.Path,
                        new string('0', 40),
                        "target.bin"))!.Code,
                Is.EqualTo(FolderProjectVersionControlError.CommitNotFound));
            Assert.That(
                Assert.Throws<FolderProjectVersionControlException>(
                    () => service.RestoreFile(
                        project.Path,
                        blobId,
                        "target.bin"))!.Code,
                Is.EqualTo(FolderProjectVersionControlError.CommitNotFound));
        });
    }

    [TestCase(@"..\outside.bin")]
    [TestCase(@"C:\outside.bin")]
    [TestCase(@".git\config")]
    [TestCase("aeproject.cn.json")]
    [TestCase(".gitignore")]
    [TestCase(".gitattributes")]
    [TestCase("file.0123456789abcdef0123456789abcdef.tmp")]
    public void RestoreFile_ReservedOrUnsafePath_Rejects(string path)
    {
        using var project = new TemporaryDirectory("restore-path");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(project.Path, initial.Id, path));

        Assert.That(
            exception!.Code,
            Is.EqualTo(FolderProjectVersionControlError.InvalidResourcePath));
    }

    [Test]
    public void RestoreFile_PathMissingInCommit_ReportsPreciseError()
    {
        using var project = new TemporaryDirectory("restore-missing-path");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(
                project.Path,
                initial.Id,
                "missing.bin"));

        Assert.That(
            exception!.Code,
            Is.EqualTo(FolderProjectVersionControlError.CommitPathNotFound));
    }

    [TestCase(Mode.NonExecutableFile)]
    [TestCase(Mode.NonExecutableGroupWritableFile)]
    [TestCase(Mode.ExecutableFile)]
    public void RestoreFile_RegularBlobModes_AreAllowed(Mode mode)
    {
        using var project = new TemporaryDirectory($"restore-mode-{mode}");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        var commitId = CreateCommitWithEntry(
            project.Path,
            "mode.bin",
            [6, 5, 4, 3],
            mode);

        service.RestoreFile(project.Path, commitId, "mode.bin");

        Assert.That(
            File.ReadAllBytes(Path.Combine(project.Path, "mode.bin")),
            Is.EqualTo(new byte[] { 6, 5, 4, 3 }));
    }

    [Test]
    public void RestoreFile_SymbolicLinkBlob_IsRejected()
    {
        using var project = new TemporaryDirectory("restore-symlink-mode");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        var commitId = CreateCommitWithEntry(
            project.Path,
            "link.bin",
            "target.bin"u8.ToArray(),
            Mode.SymbolicLink);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(
                project.Path,
                commitId,
                "link.bin"));

        Assert.That(
            exception!.Code,
            Is.EqualTo(
                FolderProjectVersionControlError.UnsupportedCommitPath));
    }

    [TestCase("directory")]
    [TestCase("gitlink")]
    public void RestoreFile_NonRegularTreeEntry_IsRejected(string entryType)
    {
        using var project = new TemporaryDirectory($"restore-{entryType}");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        string commitId;
        using (var repository = new Repository(project.Path))
        {
            var definition = TreeDefinition.From(
                repository.Head.Tip!.Tree);
            if (entryType == "directory")
            {
                using var content = new MemoryStream([1, 2, 3]);
                var blob = repository.ObjectDatabase.CreateBlob(content);
                definition.Add(
                    "entry/file.bin",
                    blob,
                    Mode.NonExecutableFile);
            }
            else
            {
                definition.AddGitLink(
                    "entry",
                    repository.Head.Tip.Id);
            }
            var tree = repository.ObjectDatabase.CreateTree(definition);
            var signature = new Signature(
                s_identity.Name,
                s_identity.Email,
                DateTimeOffset.Now);
            commitId = repository.ObjectDatabase.CreateCommit(
                    signature,
                    signature,
                    entryType,
                    tree,
                    [repository.Head.Tip],
                    prettifyMessage: false)
                .Sha;
        }

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(
                project.Path,
                commitId,
                "entry"));

        Assert.That(
            exception!.Code,
            Is.EqualTo(
                FolderProjectVersionControlError.UnsupportedCommitPath));
    }

    [TestCase("operation")]
    [TestCase("conflict")]
    [TestCase("index-lock")]
    public void RestoreFile_UnsafeRepositoryState_RejectsEvenWithOverwrite(
        string unsafeState)
    {
        using var project = CreateSwitchProject();
        var service = new FolderProjectVersionControlService();
        var initial = service.GetHistory(project.Path).Single();
        ConfigureSwitchUnsafeState(project.Path, unsafeState);
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(
                project.Path,
                initial.Id,
                "tracked.bin",
                overwriteWorkingChange: true));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                unsafeState == "index-lock"
                    ? Is.EqualTo(
                        FolderProjectVersionControlError.RepositoryBusy)
                    : Is.EqualTo(
                        FolderProjectVersionControlError
                            .UnsupportedOperationState));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [Test]
    public void RestoreFile_ReparsePointInPath_IsRejected()
    {
        using var project = new TemporaryDirectory("restore-junction");
        using var outside = new TemporaryDirectory("restore-junction-outside");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        var commitId = CreateCommitWithEntry(
            project.Path,
            "linked/file.bin",
            [1, 2, 3],
            Mode.NonExecutableFile);
        var junctionPath = Path.Combine(project.Path, "linked");
        CreateDirectoryJunction(junctionPath, outside.Path);
        try
        {
            var exception =
                Assert.Throws<FolderProjectVersionControlException>(
                    () => service.RestoreFile(
                        project.Path,
                        commitId,
                        "linked/file.bin"));

            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.InvalidResourcePath));
        }
        finally
        {
            Directory.Delete(junctionPath);
        }
    }

    [Test]
    public void RestoreFile_AtomicReplaceFailure_PreservesOriginalAndCleansTemp()
    {
        using var project = new TemporaryDirectory("restore-atomic-failure");
        var targetPath = Path.Combine(project.Path, "target.bin");
        File.WriteAllBytes(targetPath, [1, 2, 3]);
        var setup = new FolderProjectVersionControlService();
        var initial = setup.Initialize(project.Path, s_identity);
        File.WriteAllBytes(targetPath, [9, 9, 9]);
        setup.CommitAll(project.Path, "later");
        var platform = new RestoreMoveFailurePlatform();
        var service = new FolderProjectVersionControlService(platform);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(
                project.Path,
                initial.Id,
                "target.bin"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(File.ReadAllBytes(targetPath), Is.EqualTo(new byte[] { 9, 9, 9 }));
            Assert.That(platform.TemporaryPath, Is.Not.Null);
            Assert.That(File.Exists(platform.TemporaryPath!), Is.False);
        });
    }

    [Test]
    public void CreateRecoveryBranch_HistoricalCommit_OnlyCreatesLocalReference()
    {
        using var project = new TemporaryDirectory("recovery-branch");
        var targetPath = Path.Combine(project.Path, "target.bin");
        File.WriteAllBytes(targetPath, [1]);
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        File.WriteAllBytes(targetPath, [2]);
        var latest = service.CommitAll(project.Path, "latest");
        var before = CaptureRepository(project.Path);

        var branch = service.CreateRecoveryBranch(
            project.Path,
            "recovery/initial",
            initial.Id);

        var after = CaptureRepository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(branch.Name, Is.EqualTo("recovery/initial"));
            Assert.That(branch.TipCommitId, Is.EqualTo(initial.Id));
            Assert.That(branch.IsCurrent, Is.False);
            AssertRepositoryEqual(before, after);
            Assert.That(ReadHead(project.Path), Is.EqualTo(latest.Id));
            using var repository = new Repository(project.Path);
            Assert.That(
                repository.Branches["recovery/initial"]!.Tip.Sha,
                Is.EqualTo(initial.Id));
        });
    }

    [Test]
    public void GetBranches_ReturnsOnlyLocalWithCurrentFirstAndStableSort()
    {
        using var project = new TemporaryDirectory("branch-list");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "zeta");
        service.CreateBranch(project.Path, "Alpha");
        using (var repository = new Repository(project.Path))
        {
            repository.Refs.Add(
                "refs/remotes/origin/remote-only",
                initial.Id);
        }

        var branches = service.GetBranches(project.Path);

        Assert.That(
            branches.Select(branch => branch.Name),
            Is.EqualTo(new[] { "master", "Alpha", "zeta" }));
        Assert.That(branches[0].IsCurrent, Is.True);
        Assert.That(branches.Skip(1).All(branch => !branch.IsCurrent), Is.True);
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("bad name")]
    [TestCase("bad..name")]
    [TestCase("bad\\name")]
    [TestCase("CON")]
    [TestCase("safe/AUX")]
    public void CreateBranch_InvalidName_Rejects(string name)
    {
        using var project = new TemporaryDirectory("branch-invalid");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CreateBranch(project.Path, name));

        Assert.That(
            exception!.Code,
            Is.EqualTo(FolderProjectVersionControlError.InvalidBranchName));
    }

    [Test]
    public void CreateBranch_DefaultAndHistoricalStart_DoNotCheckout()
    {
        using var project = new TemporaryDirectory("branch-start");
        var targetPath = Path.Combine(project.Path, "target.bin");
        File.WriteAllBytes(targetPath, [1]);
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        File.WriteAllBytes(targetPath, [2]);
        var latest = service.CommitAll(project.Path, "latest");
        var before = CaptureRepository(project.Path);

        var currentStart = service.CreateBranch(project.Path, "current-start");
        var historicalStart = service.CreateBranch(
            project.Path,
            "historical-start",
            initial.Id.ToUpperInvariant());

        Assert.Multiple(() =>
        {
            Assert.That(currentStart.TipCommitId, Is.EqualTo(latest.Id));
            Assert.That(historicalStart.TipCommitId, Is.EqualTo(initial.Id));
            Assert.That(currentStart.IsCurrent, Is.False);
            Assert.That(historicalStart.IsCurrent, Is.False);
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [TestCase("case")]
    [TestCase("parent")]
    [TestCase("child")]
    public void CreateBranch_WindowsEquivalentOrDirectoryFileCollision_Rejects(
        string collision)
    {
        using var project = new TemporaryDirectory($"branch-collision-{collision}");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        var candidate = collision switch
        {
            "case" => CreateThen("Topic", "topic"),
            "parent" => CreateThen("foo", "foo/bar"),
            "child" => CreateThen("bar/baz", "bar"),
            _ => throw new ArgumentOutOfRangeException(nameof(collision)),
        };

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CreateBranch(project.Path, candidate));

        Assert.That(
            exception!.Code,
            Is.EqualTo(FolderProjectVersionControlError.BranchAlreadyExists));

        string CreateThen(string existing, string next)
        {
            service.CreateBranch(project.Path, existing);
            return next;
        }
    }

    [Test]
    public void RenameBranch_CurrentAndNonCurrent_PreserveTips()
    {
        using var project = new TemporaryDirectory("branch-rename");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "other");

        var other = service.RenameBranch(project.Path, "other", "renamed-other");
        var current = service.RenameBranch(project.Path, "master", "renamed-main");

        Assert.Multiple(() =>
        {
            Assert.That(other.Name, Is.EqualTo("renamed-other"));
            Assert.That(other.TipCommitId, Is.EqualTo(initial.Id));
            Assert.That(other.IsCurrent, Is.False);
            Assert.That(current.Name, Is.EqualTo("renamed-main"));
            Assert.That(current.TipCommitId, Is.EqualTo(initial.Id));
            Assert.That(current.IsCurrent, Is.True);
            using var repository = new Repository(project.Path);
            Assert.That(repository.Head.FriendlyName, Is.EqualTo("renamed-main"));
            Assert.That(repository.Branches["other"], Is.Null);
            Assert.That(repository.Branches["master"], Is.Null);
        });
    }

    [TestCase("overwrite")]
    [TestCase("case")]
    [TestCase("parent")]
    [TestCase("child")]
    public void RenameBranch_Collision_RejectsWithoutChangingReferences(
        string collision)
    {
        using var project = new TemporaryDirectory($"branch-rename-{collision}");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "source");
        var destination = collision switch
        {
            "overwrite" => CreateThen("existing", "existing"),
            "case" => "SOURCE",
            "parent" => CreateThen("foo", "foo/bar"),
            "child" => CreateThen("bar/baz", "bar"),
            _ => throw new ArgumentOutOfRangeException(nameof(collision)),
        };
        var namesBefore = service.GetBranches(project.Path)
            .Select(branch => branch.Name)
            .ToArray();

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RenameBranch(
                project.Path,
                "source",
                destination));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.BranchAlreadyExists));
            Assert.That(
                service.GetBranches(project.Path).Select(branch => branch.Name),
                Is.EqualTo(namesBefore));
        });

        string CreateThen(string existing, string next)
        {
            service.CreateBranch(project.Path, existing);
            return next;
        }
    }

    [Test]
    public void DeleteBranch_NonCurrentSucceeds_CurrentAndMissingReject()
    {
        using var project = new TemporaryDirectory("branch-delete");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "other");

        service.DeleteBranch(project.Path, "other");
        var currentException =
            Assert.Throws<FolderProjectVersionControlException>(
                () => service.DeleteBranch(project.Path, "master"));
        var missingException =
            Assert.Throws<FolderProjectVersionControlException>(
                () => service.DeleteBranch(project.Path, "missing"));

        Assert.Multiple(() =>
        {
            Assert.That(
                currentException!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.CurrentBranchProtected));
            Assert.That(
                missingException!.Code,
                Is.EqualTo(FolderProjectVersionControlError.BranchNotFound));
            Assert.That(
                service.GetBranches(project.Path).Select(branch => branch.Name),
                Is.EqualTo(new[] { "master" }));
        });
    }

    [Test]
    public void SwitchBranch_CleanBinaryWorktree_UsesNonForceCheckout()
    {
        using var project = new TemporaryDirectory("branch-switch");
        var targetPath = Path.Combine(project.Path, "target.bin");
        File.WriteAllBytes(targetPath, [0, 255, 1]);
        var setup = new FolderProjectVersionControlService();
        var initial = setup.Initialize(project.Path, s_identity);
        setup.CreateBranch(project.Path, "old", initial.Id);
        File.WriteAllBytes(targetPath, [9, 8, 7]);
        setup.CommitAll(project.Path, "latest");
        var platform = new CheckoutObservingPlatform();
        var service = new FolderProjectVersionControlService(platform);

        var branch = service.SwitchBranch(project.Path, "old");

        Assert.Multiple(() =>
        {
            Assert.That(branch.Name, Is.EqualTo("old"));
            Assert.That(branch.IsCurrent, Is.True);
            Assert.That(platform.CheckoutModifiers, Is.EqualTo(CheckoutModifiers.None));
            Assert.That(File.ReadAllBytes(targetPath), Is.EqualTo(new byte[] { 0, 255, 1 }));
            Assert.That(ReadHead(project.Path), Is.EqualTo(initial.Id));
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
        });
    }

    [TestCase("symbolic-link")]
    [TestCase("git-link")]
    public void SwitchBranch_UnsupportedTargetTreeEntry_RejectsWithoutChanges(
        string entryType)
    {
        using var project = CreateSwitchProject();
        AdvanceBranchWithUnsupportedEntry(
            project.Path,
            "other",
            entryType);
        var service = new FolderProjectVersionControlService();
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.SwitchBranch(project.Path, "other"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError
                        .UnsupportedCommitPath));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [TestCase(Mode.NonExecutableFile)]
    [TestCase(Mode.NonExecutableGroupWritableFile)]
    [TestCase(Mode.ExecutableFile)]
    public void SwitchBranch_RegularTargetTreeMode_Succeeds(Mode mode)
    {
        using var project = new TemporaryDirectory(
            $"branch-switch-mode-{mode}");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        var targetCommit = CreateCommitWithEntry(
            project.Path,
            "target.bin",
            [0, 255, 1],
            mode);
        service.CreateBranch(project.Path, "other", targetCommit);

        var branch = service.SwitchBranch(project.Path, "other");

        Assert.Multiple(() =>
        {
            Assert.That(branch.Name, Is.EqualTo("other"));
            Assert.That(branch.IsCurrent, Is.True);
            Assert.That(
                File.ReadAllBytes(
                    Path.Combine(project.Path, "target.bin")),
                Is.EqualTo(new byte[] { 0, 255, 1 }));
            Assert.That(ReadHead(project.Path), Is.EqualTo(targetCommit));
        });
    }

    [TestCase("staged")]
    [TestCase("unstaged")]
    [TestCase("untracked")]
    [TestCase("deleted")]
    public void SwitchBranch_DirtyStatus_RejectsWithoutChangingRepository(
        string dirtyState)
    {
        using var project = CreateSwitchProject();
        ConfigureSwitchDirtyState(project.Path, dirtyState);
        var service = new FolderProjectVersionControlService();
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.SwitchBranch(project.Path, "other"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.WorkingTreeNotClean));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [TestCase("detached")]
    [TestCase("operation")]
    [TestCase("conflict")]
    [TestCase("index-lock")]
    public void SwitchBranch_UnsafeRepositoryState_RejectsWithoutChanges(
        string unsafeState)
    {
        using var project = CreateSwitchProject();
        ConfigureSwitchUnsafeState(project.Path, unsafeState);
        var service = new FolderProjectVersionControlService();
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.SwitchBranch(project.Path, "other"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                unsafeState == "index-lock"
                    ? Is.EqualTo(
                        FolderProjectVersionControlError.RepositoryBusy)
                    : Is.EqualTo(
                        FolderProjectVersionControlError
                            .UnsupportedOperationState));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [Test]
    public void SwitchBranch_UnreadableWorktree_RejectsWithoutChanges()
    {
        using var project = CreateSwitchProject();
        var service = new FolderProjectVersionControlService(
            new UnreadableStatusPlatform(
                project.Path,
                "tracked.bin"));
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.SwitchBranch(project.Path, "other"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.WorkingTreeNotClean));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RestoreFile_EmptyDirectoryOccupyingTarget_RejectsAndPreservesIt(
        bool overwrite)
    {
        using var project = new TemporaryDirectory("restore-empty-directory");
        var targetPath = Path.Combine(project.Path, "target");
        File.WriteAllBytes(targetPath, [1, 2, 3]);
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        File.Delete(targetPath);
        service.CommitAll(project.Path, "delete target");
        Directory.CreateDirectory(targetPath);
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.RestoreFile(
                project.Path,
                initial.Id,
                "target",
                overwrite));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.WorkingTreeNotClean));
            Assert.That(Directory.Exists(targetPath), Is.True);
            Assert.That(Directory.EnumerateFileSystemEntries(targetPath), Is.Empty);
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [Test]
    public void DeleteBranch_UniqueCommits_RejectsWithoutChangingRepository()
    {
        using var project = new TemporaryDirectory("branch-delete-unmerged");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "other");
        AdvanceBranchWithEntry(
            project.Path,
            "other",
            "unique.bin",
            [1, 2, 3]);
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.DeleteBranch(project.Path, "other"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError
                        .BranchNotMerged));
            Assert.That(
                service.GetBranches(project.Path)
                    .Select(branch => branch.Name),
                Does.Contain("other"));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [Test]
    public void DeleteBranch_TipReachableFromAnotherLocalBranch_Succeeds()
    {
        using var project = new TemporaryDirectory("branch-delete-retained");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "source");
        AdvanceBranchWithEntry(
            project.Path,
            "source",
            "retained.bin",
            [1, 2, 3]);
        var sourceTip = service.GetBranches(project.Path)
            .Single(branch => branch.Name == "source")
            .TipCommitId;
        service.CreateBranch(project.Path, "keeper", sourceTip);
        AdvanceBranchWithEntry(
            project.Path,
            "keeper",
            "descendant.bin",
            [4, 5, 6]);
        var keeperTip = service.GetBranches(project.Path)
            .Single(branch => branch.Name == "keeper")
            .TipCommitId;
        var before = CaptureRepository(project.Path);

        service.DeleteBranch(project.Path, "source");

        Assert.Multiple(() =>
        {
            Assert.That(
                service.GetBranches(project.Path)
                    .Select(branch => branch.Name),
                Is.EquivalentTo(new[] { "master", "keeper" }));
            Assert.That(
                service.GetBranches(project.Path)
                    .Single(branch => branch.Name == "keeper")
                    .TipCommitId,
                Is.EqualTo(keeperTip));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [Test]
    public void DeleteBranch_TipReachableFromDetachedHead_Succeeds()
    {
        using var project = new TemporaryDirectory("branch-delete-detached");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "source");
        AdvanceBranchWithEntry(
            project.Path,
            "source",
            "retained.bin",
            [1, 2, 3]);
        var sourceTip = service.GetBranches(project.Path)
            .Single(branch => branch.Name == "source")
            .TipCommitId;
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, sourceTip);
        var before = CaptureRepository(project.Path);

        service.DeleteBranch(project.Path, "source");

        Assert.Multiple(() =>
        {
            Assert.That(
                service.GetBranches(project.Path)
                    .Select(branch => branch.Name),
                Is.EqualTo(new[] { "master" }));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [Test]
    public void DeleteBranch_TipOnlyReachableFromRemoteBranch_Rejects()
    {
        using var project = new TemporaryDirectory("branch-delete-remote");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "source");
        AdvanceBranchWithEntry(
            project.Path,
            "source",
            "remote-only.bin",
            [1, 2, 3]);
        var sourceTip = service.GetBranches(project.Path)
            .Single(branch => branch.Name == "source")
            .TipCommitId;
        using (var repository = new Repository(project.Path))
            repository.Refs.Add(
                "refs/remotes/origin/source-copy",
                new ObjectId(sourceTip));
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.DeleteBranch(project.Path, "source"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError
                        .BranchNotMerged));
            Assert.That(
                service.GetBranches(project.Path)
                    .Select(branch => branch.Name),
                Does.Contain("source"));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [TestCase("exact-file")]
    [TestCase("file-blocks-directory")]
    [TestCase("directory-blocks-file")]
    public void SwitchBranch_IgnoredPathCollidingWithTargetTree_Rejects(
        string collision)
    {
        using var project = new TemporaryDirectory(
            $"branch-ignored-{collision}");
        var ignoreRule = collision == "directory-blocks-file"
            ? "node/"
            : "node";
        File.WriteAllText(
            Path.Combine(project.Path, ".gitignore"),
            ignoreRule + "\n");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "target");
        var targetPath = collision switch
        {
            "exact-file" => "node",
            "file-blocks-directory" => "node/tracked.bin",
            "directory-blocks-file" => "node",
            _ => throw new ArgumentOutOfRangeException(nameof(collision)),
        };
        AdvanceBranchWithEntry(
            project.Path,
            "target",
            targetPath,
            [9, 8, 7]);
        if (collision == "directory-blocks-file")
        {
            Directory.CreateDirectory(
                Path.Combine(project.Path, "node"));
            File.WriteAllBytes(
                Path.Combine(project.Path, "node", "local.bin"),
                [1, 2, 3]);
        }
        else
        {
            File.WriteAllBytes(
                Path.Combine(project.Path, "node"),
                [1, 2, 3]);
        }
        var before = CaptureRepository(project.Path);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.SwitchBranch(project.Path, "target"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.WorkingTreeNotClean));
            AssertRepositoryEqual(
                before,
                CaptureRepository(project.Path));
        });
    }

    [TestCase("unrelated")]
    [TestCase("sibling")]
    public void SwitchBranch_NonCollidingIgnoredPath_IsPreserved(
        string ignoredLocation)
    {
        using var project = new TemporaryDirectory(
            $"branch-ignored-safe-{ignoredLocation}");
        File.WriteAllText(
            Path.Combine(project.Path, ".gitignore"),
            "*.tmp\n");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "target");
        var targetPath = ignoredLocation == "sibling"
            ? "folder/tracked.bin"
            : "target.bin";
        AdvanceBranchWithEntry(
            project.Path,
            "target",
            targetPath,
            [9, 8, 7]);
        var ignoredPath = ignoredLocation == "sibling"
            ? Path.Combine(project.Path, "folder", "local.tmp")
            : Path.Combine(project.Path, "local.tmp");
        Directory.CreateDirectory(
            Path.GetDirectoryName(ignoredPath)!);
        File.WriteAllBytes(ignoredPath, [1, 2, 3]);

        var branch = service.SwitchBranch(project.Path, "target");

        Assert.Multiple(() =>
        {
            Assert.That(branch.Name, Is.EqualTo("target"));
            Assert.That(branch.IsCurrent, Is.True);
            Assert.That(
                File.ReadAllBytes(ignoredPath),
                Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(
                File.ReadAllBytes(
                    Path.Combine(
                        project.Path,
                        targetPath.Replace(
                            '/',
                            Path.DirectorySeparatorChar))),
                Is.EqualTo(new byte[] { 9, 8, 7 }));
        });
    }

    [Test]
    public void SwitchBranch_RemoteOnlySameName_DoesNotCreateLocalBranch()
    {
        using var project = new TemporaryDirectory("branch-remote-only");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        using (var repository = new Repository(project.Path))
        {
            repository.Refs.Add(
                "refs/remotes/origin/remote-only",
                initial.Id);
        }

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.SwitchBranch(project.Path, "remote-only"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.BranchNotFound));
            using var repository = new Repository(project.Path);
            Assert.That(repository.Branches["remote-only"], Is.Null);
            Assert.That(repository.Head.FriendlyName, Is.EqualTo("master"));
        });
    }

    private static TemporaryDirectory CreateRestoreProject(
        string dirtyState,
        out string historicalCommitId)
    {
        var project = new TemporaryDirectory($"restore-{dirtyState}");
        var targetPath = Path.Combine(project.Path, "target.bin");
        File.WriteAllBytes(targetPath, [0, 255, 13, 10]);
        var service = new FolderProjectVersionControlService();
        historicalCommitId = service.Initialize(project.Path, s_identity).Id;
        switch (dirtyState)
        {
            case "staged":
                File.WriteAllBytes(targetPath, [1, 1, 1]);
                using (var repository = new Repository(project.Path))
                    Commands.Stage(repository, "target.bin");
                break;
            case "unstaged":
                File.WriteAllBytes(targetPath, [2, 2, 2]);
                break;
            case "deleted":
                File.Delete(targetPath);
                break;
            case "untracked":
            case "ignored":
                File.Delete(targetPath);
                service.CommitAll(project.Path, "delete target");
                if (dirtyState == "ignored")
                {
                    File.AppendAllText(
                        Path.Combine(project.Path, ".gitignore"),
                        "\ntarget.bin\n");
                }
                File.WriteAllBytes(targetPath, [3, 3, 3]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dirtyState));
        }

        return project;
    }

    private static TemporaryDirectory CreateSwitchProject()
    {
        var project = new TemporaryDirectory("switch");
        File.WriteAllBytes(
            Path.Combine(project.Path, "tracked.bin"),
            [1, 2, 3]);
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "other");
        return project;
    }

    private static void ConfigureSwitchDirtyState(
        string projectPath,
        string dirtyState)
    {
        var trackedPath = Path.Combine(projectPath, "tracked.bin");
        switch (dirtyState)
        {
            case "staged":
                File.WriteAllBytes(trackedPath, [9]);
                using (var repository = new Repository(projectPath))
                    Commands.Stage(repository, "tracked.bin");
                break;
            case "unstaged":
                File.WriteAllBytes(trackedPath, [8]);
                break;
            case "untracked":
                File.WriteAllBytes(
                    Path.Combine(projectPath, "untracked.bin"),
                    [7]);
                break;
            case "deleted":
                File.Delete(trackedPath);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dirtyState));
        }
    }

    private static void ConfigureSwitchUnsafeState(
        string projectPath,
        string unsafeState)
    {
        switch (unsafeState)
        {
            case "detached":
                using (var repository = new Repository(projectPath))
                    Commands.Checkout(repository, repository.Head.Tip!.Sha);
                return;
            case "operation":
                File.WriteAllText(
                    Path.Combine(projectPath, ".git", "MERGE_HEAD"),
                    ReadHead(projectPath) + "\n");
                return;
            case "conflict":
                CreateMergeConflict(projectPath);
                return;
            case "index-lock":
                File.WriteAllText(
                    Path.Combine(projectPath, ".git", "index.lock"),
                    "locked");
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(unsafeState));
        }
    }

    private static void CreateMergeConflict(string projectPath)
    {
        using var repository = new Repository(projectPath);
        var signature = new Signature(
            s_identity.Name,
            s_identity.Email,
            DateTimeOffset.Now);
        var originalBranch = repository.Head.FriendlyName;
        var conflictBranch = repository.CreateBranch("conflict-source");
        File.WriteAllText(
            Path.Combine(projectPath, "tracked.bin"),
            "main");
        Commands.Stage(repository, "tracked.bin");
        repository.Commit("main conflict side", signature, signature);
        Commands.Checkout(repository, conflictBranch);
        File.WriteAllText(
            Path.Combine(projectPath, "tracked.bin"),
            "other");
        Commands.Stage(repository, "tracked.bin");
        repository.Commit("other conflict side", signature, signature);
        Commands.Checkout(repository, originalBranch);
        var result = repository.Merge(conflictBranch, signature);
        Assert.That(result.Status, Is.EqualTo(MergeStatus.Conflicts));
    }

    private static string CreateCommitWithEntry(
        string projectPath,
        string repositoryPath,
        byte[] content,
        Mode mode)
    {
        using var repository = new Repository(projectPath);
        using var stream = new MemoryStream(content);
        var blob = repository.ObjectDatabase.CreateBlob(stream);
        Tree tree;
        if (mode == Mode.NonExecutableGroupWritableFile)
        {
            var entryPrefix = Encoding.ASCII.GetBytes(
                $"100664 {repositoryPath}\0");
            var entry = new byte[
                entryPrefix.Length + 20];
            entryPrefix.CopyTo(entry, 0);
            Convert.FromHexString(blob.Sha).CopyTo(
                entry,
                entryPrefix.Length);
            var treeId = WriteLooseObject(
                repository.Info.Path,
                "tree",
                entry);
            tree = repository.Lookup<Tree>(treeId)!;
        }
        else
        {
            var definition = TreeDefinition.From(
                repository.Head.Tip!.Tree);
            definition.Add(repositoryPath, blob, mode);
            tree = repository.ObjectDatabase.CreateTree(definition);
        }
        var signature = new Signature(
            s_identity.Name,
            s_identity.Email,
            DateTimeOffset.Now);
        return repository.ObjectDatabase.CreateCommit(
                signature,
                signature,
                $"entry {mode}",
                tree,
                [repository.Head.Tip],
                prettifyMessage: false)
            .Sha;
    }

    private static void AdvanceBranchWithEntry(
        string projectPath,
        string branchName,
        string repositoryPath,
        byte[] content)
    {
        using var repository = new Repository(projectPath);
        var branch = repository.Branches[branchName]!;
        using var stream = new MemoryStream(content);
        var blob = repository.ObjectDatabase.CreateBlob(stream);
        var definition = TreeDefinition.From(branch.Tip.Tree);
        definition.Add(
            repositoryPath,
            blob,
            Mode.NonExecutableFile);
        var tree = repository.ObjectDatabase.CreateTree(definition);
        var signature = new Signature(
            s_identity.Name,
            s_identity.Email,
            DateTimeOffset.Now);
        var commit = repository.ObjectDatabase.CreateCommit(
            signature,
            signature,
            "target entry",
            tree,
            [branch.Tip],
            prettifyMessage: false);
        repository.Refs.UpdateTarget(
            branch.CanonicalName,
            commit.Sha);
    }

    private static void AdvanceBranchWithUnsupportedEntry(
        string projectPath,
        string branchName,
        string entryType)
    {
        using var repository = new Repository(projectPath);
        var branch = repository.Branches[branchName]!;
        var definition = TreeDefinition.From(branch.Tip.Tree);
        switch (entryType)
        {
            case "symbolic-link":
                using (var stream = new MemoryStream(
                           Encoding.UTF8.GetBytes("../outside")))
                {
                    var blob =
                        repository.ObjectDatabase.CreateBlob(stream);
                    definition.Add(
                        "unsafe-entry",
                        blob,
                        Mode.SymbolicLink);
                }
                break;
            case "git-link":
                definition.AddGitLink(
                    "unsafe-entry",
                    repository.Head.Tip!.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entryType));
        }

        var tree = repository.ObjectDatabase.CreateTree(definition);
        var signature = new Signature(
            s_identity.Name,
            s_identity.Email,
            DateTimeOffset.Now);
        var commit = repository.ObjectDatabase.CreateCommit(
            signature,
            signature,
            $"target {entryType}",
            tree,
            [branch.Tip],
            prettifyMessage: false);
        repository.Refs.UpdateTarget(
            branch.CanonicalName,
            commit.Sha);
    }

    private static void CreateDirectoryJunction(
        string junctionPath,
        string targetPath)
    {
        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(
                "cmd.exe",
                $"/d /c mklink /J \"{junctionPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.That(
            process.ExitCode,
            Is.Zero,
            $"Unable to create a test junction. {output} {error}");
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
        var objectIdBytes = SHA1.HashData(objectBytes);
        var objectId = Convert.ToHexString(objectIdBytes)
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

    private static RepositorySnapshot CaptureRepository(string projectPath)
    {
        string headId;
        string headName;
        using (var repository = new Repository(projectPath))
        {
            headId = repository.Head.Tip!.Sha;
            headName = repository.Info.IsHeadDetached
                ? "(detached)"
                : repository.Head.FriendlyName;
        }

        var worktree = Directory.EnumerateFiles(
                projectPath,
                "*",
                SearchOption.AllDirectories)
            .Where(
                path => !Path.GetRelativePath(projectPath, path)
                    .Split(Path.DirectorySeparatorChar)[0]
                    .Equals(".git", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                path => Path.GetRelativePath(projectPath, path)
                    .Replace('\\', '/'),
                path => Convert.ToBase64String(File.ReadAllBytes(path)),
                StringComparer.Ordinal);
        return new RepositorySnapshot(
            headId,
            headName,
            Convert.ToBase64String(
                File.ReadAllBytes(
                    Path.Combine(projectPath, ".git", "index"))),
            worktree);
    }

    private static void AssertRepositoryEqual(
        RepositorySnapshot expected,
        RepositorySnapshot actual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.HeadId, Is.EqualTo(expected.HeadId));
            Assert.That(actual.HeadName, Is.EqualTo(expected.HeadName));
            Assert.That(actual.IndexBytes, Is.EqualTo(expected.IndexBytes));
            Assert.That(
                actual.Worktree.OrderBy(entry => entry.Key),
                Is.EqualTo(expected.Worktree.OrderBy(entry => entry.Key)));
        });
    }

    private static string ReadHead(string projectPath)
    {
        using var repository = new Repository(projectPath);
        return repository.Head.Tip!.Sha;
    }

    private sealed record RepositorySnapshot(
        string HeadId,
        string HeadName,
        string IndexBytes,
        IReadOnlyDictionary<string, string> Worktree);

    private sealed class RestoreMoveFailurePlatform :
        FolderProjectVersionControlPlatform
    {
        public string? TemporaryPath { get; private set; }

        public override void MoveFile(
            string sourcePath,
            string destinationPath,
            bool overwrite)
        {
            TemporaryPath = sourcePath;
            throw new IOException("Injected atomic replace failure.");
        }
    }

    private sealed class CheckoutObservingPlatform :
        FolderProjectVersionControlPlatform
    {
        public CheckoutModifiers? CheckoutModifiers { get; private set; }

        public override Branch CheckoutBranch(
            Repository repository,
            Branch branch,
            CheckoutOptions options)
        {
            CheckoutModifiers = options.CheckoutModifiers;
            return base.CheckoutBranch(repository, branch, options);
        }
    }

    private sealed class UnreadableStatusPlatform :
        FolderProjectVersionControlPlatform
    {
        private readonly string _unreadablePath;

        public UnreadableStatusPlatform(
            string projectRoot,
            string repositoryPath)
        {
            _unreadablePath = Path.GetFullPath(
                Path.Combine(projectRoot, repositoryPath));
        }

        public override Stream OpenReadForStatus(string path)
        {
            if (Path.GetFullPath(path).Equals(
                    _unreadablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "Injected unreadable file.");
            }

            return base.OpenReadForStatus(path);
        }
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
}
