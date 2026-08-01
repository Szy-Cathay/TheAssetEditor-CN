using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Shared.Core;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;

namespace Test.Shared.Core.PackFiles;

public class FolderProjectVersionControlServiceTests
{
    private static readonly FolderProjectGitIdentity s_identity =
        new("AE 用户", "ae-user@example.invalid");

    [Test]
    public void GetStatus_UninitializedProject_ReturnsNotInitialized()
    {
        using var project = new TemporaryDirectory("未初始化 工程");
        var service = new FolderProjectVersionControlService();

        var status = service.GetStatus(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsInitialized, Is.False);
            Assert.That(status.IsClean, Is.True);
            Assert.That(status.Changes, Is.Empty);
        });
    }

    [Test]
    public void Initialize_ChineseSpaceProject_CreatesSingleInitialCommit()
    {
        using var project = new TemporaryDirectory("中文 工程");
        FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" }).Dispose();
        var binaryPath = Path.Combine(project.Path, "db", "item.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(binaryPath)!);
        var binaryBytes = new byte[] { 0, 13, 10, 255, 42 };
        File.WriteAllBytes(binaryPath, binaryBytes);
        var service = new FolderProjectVersionControlService();

        var commit = service.Initialize(project.Path, s_identity);

        using var repository = new Repository(project.Path);
        var commits = repository.Commits.ToList();
        var committedBlob =
            (Blob)commits[0].Tree["db/item.bin"].Target;
        using var stream = committedBlob.GetContentStream();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        Assert.Multiple(() =>
        {
            Assert.That(commit.Message, Is.EqualTo("初始化文件夹工程"));
            Assert.That(commits, Has.Count.EqualTo(1));
            Assert.That(commits[0].MessageShort, Is.EqualTo("初始化文件夹工程"));
            Assert.That(commits[0].Author.Name, Is.EqualTo(s_identity.Name));
            Assert.That(commits[0].Author.Email, Is.EqualTo(s_identity.Email));
            Assert.That(
                repository.Config.Get<string>(
                    "user.name",
                    ConfigurationLevel.Local)?.Value,
                Is.EqualTo(s_identity.Name));
            Assert.That(
                repository.Config.Get<string>(
                    "user.email",
                    ConfigurationLevel.Local)?.Value,
                Is.EqualTo(s_identity.Email));
            Assert.That(
                repository.Index[FolderProjectSettings.CnFileName],
                Is.Not.Null);
            Assert.That(repository.Index[".gitignore"], Is.Not.Null);
            Assert.That(repository.Index[".gitattributes"], Is.Not.Null);
            Assert.That(output.ToArray(), Is.EqualTo(binaryBytes));
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
        });
    }

    [Test]
    public void Initialize_CustomPrimaryBranch_UsesAndRecordsRequestedName()
    {
        using var project = new TemporaryDirectory("custom-primary");
        var service = new FolderProjectVersionControlService();

        service.Initialize(project.Path, s_identity, "main");

        using var repository = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(repository.Head.FriendlyName, Is.EqualTo("main"));
            Assert.That(repository.Branches["master"], Is.Null);
            Assert.That(
                service.GetBranches(project.Path).Single().IsPrimary,
                Is.True);
            Assert.That(service.GetHistory(project.Path, "main"), Is.Empty);
        });
    }

    [TestCase("", "ae-user@example.invalid")]
    [TestCase(" ", "ae-user@example.invalid")]
    [TestCase("AE User", "")]
    [TestCase("AE User", " ")]
    public void Initialize_InvalidIdentity_RejectsBeforeCreatingRepository(
        string name,
        string email)
    {
        using var project = new TemporaryDirectory("invalid-identity");
        var service = new FolderProjectVersionControlService();

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.Initialize(
                project.Path,
                new FolderProjectGitIdentity(name, email)));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.InvalidIdentity));
            Assert.That(
                Directory.Exists(Path.Combine(project.Path, ".git")),
                Is.False);
        });
    }

    [TestCase(
        "io",
        FolderProjectVersionControlError.RepositoryFailure)]
    [TestCase(
        "unauthorized",
        FolderProjectVersionControlError.RepositoryFailure)]
    [TestCase(
        "invalid-operation",
        FolderProjectVersionControlError.UnsupportedRepository)]
    [TestCase(
        "locked",
        FolderProjectVersionControlError.RepositoryBusy)]
    public void Initialize_FoundationFailure_MapsPreciseDomainError(
        string failureType,
        FolderProjectVersionControlError expectedError)
    {
        using var project = new TemporaryDirectory("foundation-failure");
        Exception failure = failureType switch
        {
            "io" => new IOException("Injected IO failure."),
            "unauthorized" => new UnauthorizedAccessException(
                "Injected access failure."),
            "invalid-operation" => new InvalidOperationException(
                "Injected repository structure failure."),
            "locked" => new LockedFileException(
                "Injected repository lock."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(failureType)),
        };
        var service = new FolderProjectVersionControlService(
            new FoundationFailurePlatform(failure));

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.Initialize(project.Path, s_identity));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(expectedError));
            Assert.That(
                Directory.Exists(Path.Combine(project.Path, ".git")),
                Is.False);
        });
    }

    [Test]
    public void Initialize_Repeated_DoesNotChangeHistoryOrHead()
    {
        using var project = new TemporaryDirectory("idempotent");
        File.WriteAllText(Path.Combine(project.Path, "resource.txt"), "one");
        var service = new FolderProjectVersionControlService();

        var first = service.Initialize(project.Path, s_identity);
        var second = service.Initialize(project.Path, s_identity);

        Assert.Multiple(() =>
        {
            Assert.That(second.Id, Is.EqualTo(first.Id));
            Assert.That(service.GetHistory(project.Path), Is.Empty);
            Assert.That(
                service.GetStatus(project.Path).HeadCommitId,
                Is.EqualTo(first.Id));
        });
    }

    [Test]
    public void Initialize_DetachedHead_RejectsWithoutChangingIdentity()
    {
        using var project = new TemporaryDirectory("detached-initialize");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, initial.Id);
        var replacement =
            new FolderProjectGitIdentity("Other", "other@example.invalid");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.Initialize(project.Path, replacement));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError
                        .UnsupportedOperationState));
            Assert.That(service.GetIdentity(project.Path), Is.EqualTo(s_identity));
        });
    }

    [TestCase("detached")]
    [TestCase("merge")]
    [TestCase("index-lock")]
    public void Initialize_ExistingUnsafeRepository_RejectsBeforePolicyWrites(
        string unsafeState)
    {
        using var project = new TemporaryDirectory(
            $"initialize-preflight-{unsafeState}");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        var resourcePath = Path.Combine(project.Path, "resource.txt");
        File.WriteAllText(resourcePath, "working change");
        ConfigureUnsafeState(project.Path, initial.Id, unsafeState);
        var attributesPath = Path.Combine(project.Path, ".gitattributes");
        var ignorePath = Path.Combine(project.Path, ".gitignore");
        File.Delete(attributesPath);
        File.Delete(ignorePath);
        var indexBefore = CaptureFile(
            Path.Combine(project.Path, ".git", "index"));
        var headBefore = ReadHeadId(project.Path);
        var replacement =
            new FolderProjectGitIdentity("Other", "other@example.invalid");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.Initialize(project.Path, replacement));

        var indexAfter = CaptureFile(
            Path.Combine(project.Path, ".git", "index"));
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    unsafeState == "index-lock"
                        ? FolderProjectVersionControlError.RepositoryBusy
                        : FolderProjectVersionControlError
                            .UnsupportedOperationState));
            Assert.That(File.Exists(attributesPath), Is.False);
            Assert.That(File.Exists(ignorePath), Is.False);
            Assert.That(service.GetIdentity(project.Path), Is.EqualTo(s_identity));
            Assert.That(ReadHeadId(project.Path), Is.EqualTo(headBefore));
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(
                File.ReadAllText(resourcePath),
                Is.EqualTo("working change"));
        });
    }

    [TestCase("detached")]
    [TestCase("merge")]
    [TestCase("index-lock")]
    public void Initialize_PostflightUnsafeState_RestoresExistingPolicyFiles(
        string unsafeState)
    {
        using var project = new TemporaryDirectory(
            $"initialize-postflight-{unsafeState}");
        var setupService = new FolderProjectVersionControlService();
        setupService.Initialize(project.Path, s_identity);
        var policyPaths = PreparePolicyFilesForFoundationUpdate(project.Path);
        var policiesBefore = policyPaths
            .Select(CaptureFile)
            .ToList();
        var platform = new PostInitializeUnsafeStatePlatform(unsafeState);
        var service = new FolderProjectVersionControlService(platform);
        var replacement =
            new FolderProjectGitIdentity("Other", "other@example.invalid");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.Initialize(project.Path, replacement));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    unsafeState == "index-lock"
                        ? FolderProjectVersionControlError.RepositoryBusy
                        : FolderProjectVersionControlError
                            .UnsupportedOperationState));
            Assert.That(
                policyPaths.Select(CaptureFile),
                Is.EqualTo(policiesBefore));
            Assert.That(
                service.GetIdentity(project.Path),
                Is.EqualTo(s_identity));
        });
    }

    [Test]
    public void Initialize_PostflightRollbackFailure_ReportsBothFailures()
    {
        using var project = new TemporaryDirectory(
            "initialize-postflight-rollback-failure");
        var setupService = new FolderProjectVersionControlService();
        setupService.Initialize(project.Path, s_identity);
        var policyPaths = PreparePolicyFilesForFoundationUpdate(project.Path);
        using var platform = new PostInitializeUnsafeStatePlatform(
            "index-lock",
            policyPaths[0]);
        var service = new FolderProjectVersionControlService(platform);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.Initialize(project.Path, s_identity));
        var aggregate = exception!.InnerException as AggregateException;

        Assert.Multiple(() =>
        {
            Assert.That(
                exception.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(aggregate, Is.Not.Null);
            Assert.That(
                aggregate!.Flatten().InnerExceptions.Any(
                    item =>
                        item is FolderProjectVersionControlException
                        {
                            Code: FolderProjectVersionControlError.RepositoryBusy,
                        }),
                Is.True);
            Assert.That(
                aggregate.Flatten().InnerExceptions.Any(
                    item => item is IOException),
                Is.True);
        });
    }

    [Test]
    public void Initialize_FreshPostflightFailure_DoesNotDeleteNewRepository()
    {
        using var project = new TemporaryDirectory(
            "initialize-fresh-postflight");
        var service = new FolderProjectVersionControlService(
            new PostInitializeUnsafeStatePlatform("index-lock"));

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.Initialize(project.Path, s_identity));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.RepositoryBusy));
            Assert.That(
                Directory.Exists(Path.Combine(project.Path, ".git")),
                Is.True);
            Assert.That(
                File.Exists(Path.Combine(project.Path, ".gitignore")),
                Is.True);
            Assert.That(
                File.Exists(Path.Combine(project.Path, ".gitattributes")),
                Is.True);
        });
    }

    [Test]
    public void GetAndSetIdentity_UseRepositoryLocalConfiguration()
    {
        using var project = new TemporaryDirectory("local-identity");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        var replacement =
            new FolderProjectGitIdentity("另一位用户", "other@example.invalid");

        service.SetIdentity(project.Path, replacement);
        var actual = service.GetIdentity(project.Path);

        using var repository = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(replacement));
            Assert.That(
                repository.Config.Get<string>(
                    "user.name",
                    ConfigurationLevel.Local)?.Value,
                Is.EqualTo(replacement.Name));
            Assert.That(
                repository.Config.Get<string>(
                    "user.email",
                    ConfigurationLevel.Local)?.Value,
                Is.EqualTo(replacement.Email));
        });
    }

    [Test]
    public void SetIdentity_InvalidIdentity_DoesNotReplaceExistingIdentity()
    {
        using var project = new TemporaryDirectory("identity-rollback");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.SetIdentity(
                project.Path,
                new FolderProjectGitIdentity(
                    "invalid\nname",
                    "other@example.invalid")));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.InvalidIdentity));
            Assert.That(service.GetIdentity(project.Path), Is.EqualTo(s_identity));
        });
    }

    [Test]
    public void GetStatus_WorkingChanges_MapsKindsAndOmitsIgnoredFiles()
    {
        using var project = new TemporaryDirectory("status");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "modified.txt"), "one");
        File.WriteAllText(Path.Combine(project.Path, "deleted.txt"), "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(Path.Combine(project.Path, "modified.txt"), "two");
        File.Delete(Path.Combine(project.Path, "deleted.txt"));
        File.WriteAllText(Path.Combine(project.Path, "untracked.txt"), "new");
        File.WriteAllText(
            Path.Combine(project.Path, ".gitignore"),
            File.ReadAllText(Path.Combine(project.Path, ".gitignore")) +
            "ignored.txt\r\n");
        File.WriteAllText(Path.Combine(project.Path, "ignored.txt"), "ignored");

        var status = service.GetStatus(project.Path);
        var changes = status.Changes.ToDictionary(
            change => change.RepositoryPath,
            change => change.Kind);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsInitialized, Is.True);
            Assert.That(status.IsClean, Is.False);
            Assert.That(status.CurrentBranch, Is.Not.Empty);
            Assert.That(status.HeadCommitId, Has.Length.EqualTo(40));
            Assert.That(
                changes["modified.txt"],
                Is.EqualTo(
                    FolderProjectWorkingChangeKind.Modified |
                    FolderProjectWorkingChangeKind.Unstaged));
            Assert.That(
                changes["deleted.txt"],
                Is.EqualTo(
                    FolderProjectWorkingChangeKind.Deleted |
                    FolderProjectWorkingChangeKind.Unstaged));
            Assert.That(
                changes["untracked.txt"],
                Is.EqualTo(
                    FolderProjectWorkingChangeKind.Added |
                    FolderProjectWorkingChangeKind.Untracked |
                    FolderProjectWorkingChangeKind.Unstaged));
            Assert.That(changes, Does.Not.ContainKey("ignored.txt"));
        });
    }

    [Test]
    public void GetStatus_StagedChange_ReportsStagedFlag()
    {
        using var project = new TemporaryDirectory("staged-status");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(Path.Combine(project.Path, "staged.txt"), "new");
        using (var repository = new Repository(project.Path))
            Commands.Stage(repository, "staged.txt");

        var change = service.GetStatus(project.Path).Changes
            .Single(item => item.RepositoryPath == "staged.txt");

        Assert.That(
            change.Kind,
            Is.EqualTo(
                    FolderProjectWorkingChangeKind.Added |
                    FolderProjectWorkingChangeKind.Staged));
    }

    [TestCase("enumerate", "blocked")]
    [TestCase("attributes", "unreadable.bin")]
    [TestCase("open", "unreadable.bin")]
    public void GetStatus_FileSystemAccessFailure_ReportsUnreadable(
        string failurePoint,
        string repositoryPath)
    {
        using var project = new TemporaryDirectory("unreadable-status");
        var setupService = new FolderProjectVersionControlService();
        if (failurePoint == "enumerate")
        {
            Directory.CreateDirectory(
                Path.Combine(project.Path, repositoryPath));
            File.WriteAllText(
                Path.Combine(project.Path, repositoryPath, "child.bin"),
                "content");
        }
        else
        {
            File.WriteAllText(
                Path.Combine(project.Path, repositoryPath),
                "content");
        }
        setupService.Initialize(project.Path, s_identity);
        Assert.That(setupService.GetStatus(project.Path).IsClean, Is.True);
        var platform = new StatusFileSystemPlatform(
            project.Path,
            failurePoint,
            repositoryPath);
        var service = new FolderProjectVersionControlService(platform);

        var status = service.GetStatus(project.Path);
        var change = status.Changes.Single(
            item => item.RepositoryPath == repositoryPath);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsClean, Is.False);
            Assert.That(
                change.Kind,
                Is.EqualTo(FolderProjectWorkingChangeKind.Unreadable));
            Assert.That(platform.AccessedFailurePath, Is.True);
        });
    }

    [Test]
    public void StageAndUnstageChanges_OnlyUpdatesSelectedFiles()
    {
        using var project = new TemporaryDirectory("stage-selection");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "first.txt"), "one");
        File.WriteAllText(Path.Combine(project.Path, "second.txt"), "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(Path.Combine(project.Path, "first.txt"), "two");
        File.WriteAllText(Path.Combine(project.Path, "second.txt"), "two");

        service.StageChanges(project.Path, ["first.txt"]);

        var staged = service.GetStatus(project.Path).Changes;
        Assert.Multiple(() =>
        {
            Assert.That(
                staged.Single(change => change.RepositoryPath == "first.txt")
                    .Kind.HasFlag(FolderProjectWorkingChangeKind.Staged),
                Is.True);
            Assert.That(
                staged.Single(change => change.RepositoryPath == "second.txt")
                    .Kind.HasFlag(FolderProjectWorkingChangeKind.Staged),
                Is.False);
        });

        service.UnstageChanges(project.Path, ["first.txt"]);

        var unstaged = service.GetStatus(project.Path).Changes;
        Assert.That(
            unstaged.All(
                change =>
                    change.Kind.HasFlag(
                        FolderProjectWorkingChangeKind.Unstaged) &&
                    !change.Kind.HasFlag(
                        FolderProjectWorkingChangeKind.Staged)),
            Is.True);
    }

    [Test]
    public void StageAndDiscardChanges_AllowsTrackedProjectControlFile()
    {
        using var project = new TemporaryDirectory("stage-project-control");
        var service = new FolderProjectVersionControlService();
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        File.WriteAllText(settingsPath, "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(settingsPath, "two");

        service.StageChanges(
            project.Path,
            [FolderProjectSettings.CnFileName]);

        var staged = service.GetStatus(project.Path).Changes.Single();
        Assert.That(
            staged.Kind.HasFlag(FolderProjectWorkingChangeKind.Staged),
            Is.True);

        service.DiscardChanges(
            project.Path,
            [FolderProjectSettings.CnFileName]);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(settingsPath), Is.EqualTo("one"));
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
        });
    }

    [Test]
    public void CommitStaged_LeavesUnstagedFilesOutOfCommit()
    {
        using var project = new TemporaryDirectory("commit-staged");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "staged.txt"), "one");
        File.WriteAllText(Path.Combine(project.Path, "working.txt"), "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(Path.Combine(project.Path, "staged.txt"), "two");
        File.WriteAllText(Path.Combine(project.Path, "working.txt"), "two");
        service.StageChanges(project.Path, ["staged.txt"]);

        var commit = service.CommitStaged(project.Path, "只提交暂存内容");

        using var repository = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(repository.Head.Tip!.Sha, Is.EqualTo(commit.Id));
            Assert.That(
                ((Blob)repository.Head.Tip.Tree["staged.txt"].Target)
                    .GetContentText(),
                Is.EqualTo("two"));
            Assert.That(
                ((Blob)repository.Head.Tip.Tree["working.txt"].Target)
                    .GetContentText(),
                Is.EqualTo("one"));
            Assert.That(File.ReadAllText(
                Path.Combine(project.Path, "working.txt")),
                Is.EqualTo("two"));
            Assert.That(
                service.GetStatus(project.Path).Changes.Single()
                    .RepositoryPath,
                Is.EqualTo("working.txt"));
        });
    }

    [Test]
    public void DiscardChanges_RestoresTrackedFilesAndRemovesAddedFile()
    {
        using var project = new TemporaryDirectory("discard-changes");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "modified.txt"), "one");
        File.WriteAllText(Path.Combine(project.Path, "deleted.txt"), "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(Path.Combine(project.Path, "modified.txt"), "two");
        File.Delete(Path.Combine(project.Path, "deleted.txt"));
        File.WriteAllText(Path.Combine(project.Path, "added.txt"), "new");
        service.StageChanges(
            project.Path,
            ["modified.txt", "deleted.txt", "added.txt"]);

        service.DiscardChanges(
            project.Path,
            ["modified.txt", "deleted.txt", "added.txt"]);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(
                Path.Combine(project.Path, "modified.txt")),
                Is.EqualTo("one"));
            Assert.That(File.ReadAllText(
                Path.Combine(project.Path, "deleted.txt")),
                Is.EqualTo("one"));
            Assert.That(
                File.Exists(Path.Combine(project.Path, "added.txt")),
                Is.False);
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
        });
    }

    [Test]
    public void DiscardChanges_RestoresBothSidesOfRename()
    {
        using var project = new TemporaryDirectory("discard-rename");
        var service = new FolderProjectVersionControlService();
        var originalPath = Path.Combine(project.Path, "original.txt");
        var renamedPath = Path.Combine(project.Path, "renamed.txt");
        File.WriteAllText(originalPath, "content");
        service.Initialize(project.Path, s_identity);
        File.Move(originalPath, renamedPath);
        var rename = service.GetStatus(project.Path).Changes.Single();

        service.DiscardChanges(project.Path, [rename.RepositoryPath]);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(originalPath), Is.EqualTo("content"));
            Assert.That(File.Exists(renamedPath), Is.False);
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
        });
    }

    [Test]
    public void UndoLatestCommit_RemovesHistoryAndKeepsChangesUnstaged()
    {
        using var project = new TemporaryDirectory("revert-to-working");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "modified.txt"), "one");
        File.WriteAllText(Path.Combine(project.Path, "deleted.txt"), "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(Path.Combine(project.Path, "modified.txt"), "two");
        File.Delete(Path.Combine(project.Path, "deleted.txt"));
        File.WriteAllText(Path.Combine(project.Path, "added.txt"), "new");
        var committed = service.CommitAll(project.Path, "待重新修改");

        service.UndoLatestCommit(
            project.Path,
            committed.Id,
            FolderProjectCommitUndoMode.KeepChanges);

        var status = service.GetStatus(project.Path);
        var history = new FolderProjectVersionControlService()
            .GetHistory(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                status.HeadCommitId,
                Is.EqualTo(committed.ParentIds[0]));
            Assert.That(history, Is.Empty);
            Assert.That(
                status.OperationState,
                Is.EqualTo(FolderProjectRepositoryOperationState.None));
            Assert.That(
                service.GetMergeState(project.Path).Phase,
                Is.EqualTo(FolderProjectMergePhase.None));
            Assert.That(File.ReadAllText(
                Path.Combine(project.Path, "modified.txt")),
                Is.EqualTo("two"));
            Assert.That(
                File.Exists(Path.Combine(project.Path, "deleted.txt")),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(project.Path, "added.txt")),
                Is.True);
            Assert.That(status.Changes, Has.Count.EqualTo(3));
            Assert.That(
                status.Changes.All(
                    change =>
                        change.Kind.HasFlag(
                            FolderProjectWorkingChangeKind.Unstaged) &&
                        !change.Kind.HasFlag(
                            FolderProjectWorkingChangeKind.Staged)),
                Is.True);
        });

        var changedPaths = status.Changes
            .Select(change => change.RepositoryPath)
            .ToList();
        service.StageChanges(project.Path, changedPaths);
        Assert.That(
            service.GetStatus(project.Path).Changes.All(
                change => change.Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Staged)),
            Is.True);

        service.UnstageChanges(project.Path, changedPaths);
        service.DiscardChanges(project.Path, changedPaths);
        Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
    }

    [Test]
    public void EditLatestCommitChanges_DiscardRemovesOnlySelectedChange()
    {
        using var project = new TemporaryDirectory("discard-commit-file");
        var service = new FolderProjectVersionControlService();
        var firstPath = Path.Combine(project.Path, "first.txt");
        var secondPath = Path.Combine(project.Path, "second.txt");
        File.WriteAllText(firstPath, "first before");
        File.WriteAllText(secondPath, "second before");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(firstPath, "first in A");
        File.WriteAllText(secondPath, "second in A");
        var original = service.CommitAll(project.Path, "提交 A");

        service.EditLatestCommitChanges(
            project.Path,
            original.Id,
            ["first.txt"],
            FolderProjectCommitChangeEditMode.Discard);

        var history = service.GetHistory(project.Path);
        var changes = service.GetCommitChanges(project.Path, history[0].Id);
        Assert.Multiple(() =>
        {
            Assert.That(history[0].Message, Is.EqualTo("提交 A"));
            Assert.That(history[0].Id, Is.Not.EqualTo(original.Id));
            Assert.That(
                changes.Select(change => change.RepositoryPath),
                Is.EqualTo(new[] { "second.txt" }));
            Assert.That(File.ReadAllText(firstPath), Is.EqualTo("first before"));
            Assert.That(File.ReadAllText(secondPath), Is.EqualTo("second in A"));
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
        });
    }

    [Test]
    public void EditLatestCommitChanges_StageAndCompleteRewritesSameCommit()
    {
        using var project = new TemporaryDirectory("edit-commit-file");
        var service = new FolderProjectVersionControlService();
        var firstPath = Path.Combine(project.Path, "first.txt");
        var secondPath = Path.Combine(project.Path, "second.txt");
        File.WriteAllText(firstPath, "first before");
        File.WriteAllText(secondPath, "second before");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(firstPath, "first in A");
        File.WriteAllText(secondPath, "second in A");
        var original = service.CommitAll(project.Path, "提交 A");

        var editSession = service.EditLatestCommitChanges(
            project.Path,
            original.Id,
            ["first.txt"],
            FolderProjectCommitChangeEditMode.StageForEdit);

        var stagedStatus = service.GetStatus(project.Path);
        var rewrittenChanges = service.GetCommitChanges(
            project.Path,
            editSession.ExpectedHeadCommitId);
        Assert.Multiple(() =>
        {
            Assert.That(editSession.CanReturnToOriginalCommit, Is.True);
            Assert.That(
                rewrittenChanges.Select(change => change.RepositoryPath),
                Is.EqualTo(new[] { "second.txt" }));
            Assert.That(stagedStatus.Changes, Has.Count.EqualTo(1));
            Assert.That(
                stagedStatus.Changes[0].RepositoryPath,
                Is.EqualTo("first.txt"));
            Assert.That(
                stagedStatus.Changes[0].Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Staged),
                Is.True);
        });

        File.WriteAllText(firstPath, "first edited");
        service.StageChanges(project.Path, ["first.txt"]);
        var completed = service.CompleteLatestCommitEdit(
            project.Path,
            editSession);

        var completedChanges = service.GetCommitChanges(
            project.Path,
            completed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(completed.Message, Is.EqualTo("提交 A"));
            Assert.That(
                completedChanges.Select(change => change.RepositoryPath),
                Is.EqualTo(new[] { "first.txt", "second.txt" }));
            Assert.That(File.ReadAllText(firstPath), Is.EqualTo("first edited"));
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
        });
    }

    [Test]
    public void EditLatestCommitChanges_AllChangesRemovedCanOnlyCreateNewCommit()
    {
        using var project = new TemporaryDirectory("edit-entire-commit");
        var service = new FolderProjectVersionControlService();
        var filePath = Path.Combine(project.Path, "only.txt");
        File.WriteAllText(filePath, "before");
        var parent = service.Initialize(project.Path, s_identity);
        File.WriteAllText(filePath, "in A");
        var original = service.CommitAll(project.Path, "提交 A");

        var editSession = service.EditLatestCommitChanges(
            project.Path,
            original.Id,
            ["only.txt"],
            FolderProjectCommitChangeEditMode.StageForEdit);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CompleteLatestCommitEdit(
                project.Path,
                editSession));
        Assert.Multiple(() =>
        {
            Assert.That(editSession.CanReturnToOriginalCommit, Is.False);
            Assert.That(editSession.ExpectedHeadCommitId, Is.EqualTo(parent.Id));
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.CommitNotFound));
            Assert.That(service.GetHistory(project.Path), Is.Empty);
            Assert.That(
                service.GetStatus(project.Path).Changes.Single().Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Staged),
                Is.True);
        });

        var newCommit = service.CommitStaged(project.Path, "新提交");

        Assert.Multiple(() =>
        {
            Assert.That(newCommit.Message, Is.EqualTo("新提交"));
            Assert.That(service.GetHistory(project.Path), Has.Count.EqualTo(1));
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
        });
    }

    [Test]
    public void EditLatestCommitChanges_DiscardHandlesAddDeleteAndRename()
    {
        using var project = new TemporaryDirectory("discard-file-kinds");
        var service = new FolderProjectVersionControlService();
        var deletedPath = Path.Combine(project.Path, "deleted.txt");
        var oldPath = Path.Combine(project.Path, "old-name.txt");
        var newPath = Path.Combine(project.Path, "new-name.txt");
        var addedPath = Path.Combine(project.Path, "added.txt");
        File.WriteAllText(deletedPath, "delete me");
        File.WriteAllText(oldPath, "rename me");
        var parent = service.Initialize(project.Path, s_identity);
        File.Delete(deletedPath);
        File.Move(oldPath, newPath);
        File.WriteAllText(addedPath, "add me");
        var original = service.CommitAll(project.Path, "提交 A");
        var changedPaths = service.GetCommitChanges(project.Path, original.Id)
            .Select(change => change.RepositoryPath)
            .ToList();

        var result = service.EditLatestCommitChanges(
            project.Path,
            original.Id,
            changedPaths,
            FolderProjectCommitChangeEditMode.Discard);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExpectedHeadCommitId, Is.EqualTo(parent.Id));
            Assert.That(result.CanReturnToOriginalCommit, Is.False);
            Assert.That(File.Exists(addedPath), Is.False);
            Assert.That(File.ReadAllText(deletedPath), Is.EqualTo("delete me"));
            Assert.That(File.ReadAllText(oldPath), Is.EqualTo("rename me"));
            Assert.That(File.Exists(newPath), Is.False);
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
        });
    }

    [Test]
    public void GetMergeState_StaleRevertPreservesInverseAndReturnsToIdle()
    {
        using var project = new TemporaryDirectory("stale-revert");
        var service = new FolderProjectVersionControlService();
        var filePath = Path.Combine(project.Path, "modified.txt");
        File.WriteAllText(filePath, "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(filePath, "two");
        var original = service.CommitAll(project.Path, "待重新修改");
        using (var repository = new Repository(project.Path))
        {
            repository.Revert(
                repository.Head.Tip!,
                new Signature(
                    s_identity.Name,
                    s_identity.Email,
                    DateTimeOffset.Now),
                new RevertOptions { CommitOnSuccess = false });
            Assert.That(
                repository.Info.CurrentOperation,
                Is.EqualTo(CurrentOperation.Revert));
        }

        var mergeState = service.GetMergeState(project.Path);
        var status = service.GetStatus(project.Path);
        var history = service.GetHistory(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                mergeState.Phase,
                Is.EqualTo(FolderProjectMergePhase.None));
            Assert.That(
                status.OperationState,
                Is.EqualTo(FolderProjectRepositoryOperationState.None));
            Assert.That(File.ReadAllText(filePath), Is.EqualTo("one"));
            Assert.That(history[0].Id, Is.EqualTo(original.Id));
            Assert.That(status.Changes, Has.Count.EqualTo(1));
            Assert.That(
                status.Changes[0].Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Unstaged),
                Is.True);
            Assert.That(
                status.Changes[0].Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Staged),
                Is.False);
        });
    }

    [Test]
    public void UndoLatestCommit_DiscardChangesRemovesCommitAndChanges()
    {
        using var project = new TemporaryDirectory("undo-and-discard");
        var service = new FolderProjectVersionControlService();
        var filePath = Path.Combine(project.Path, "modified.txt");
        var addedPath = Path.Combine(project.Path, "added.txt");
        File.WriteAllText(filePath, "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(filePath, "two");
        File.WriteAllText(addedPath, "new");
        var original = service.CommitAll(project.Path, "原提交");

        service.UndoLatestCommit(
            project.Path,
            original.Id,
            FolderProjectCommitUndoMode.DiscardChanges);

        var history = service.GetHistory(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(history, Is.Empty);
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
            Assert.That(File.ReadAllText(filePath), Is.EqualTo("one"));
            Assert.That(File.Exists(addedPath), Is.False);
        });
    }

    [Test]
    public void UndoLatestCommit_DirtyRepositoryIsUnchanged()
    {
        using var project = new TemporaryDirectory("revert-dirty");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "committed.txt"), "one");
        File.WriteAllText(Path.Combine(project.Path, "working.txt"), "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(Path.Combine(project.Path, "committed.txt"), "two");
        var committed = service.CommitAll(project.Path, "待撤销");
        File.WriteAllText(Path.Combine(project.Path, "working.txt"), "dirty");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.UndoLatestCommit(
                project.Path,
                committed.Id,
                FolderProjectCommitUndoMode.KeepChanges));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.WorkingTreeNotClean));
            Assert.That(
                service.GetStatus(project.Path).HeadCommitId,
                Is.EqualTo(committed.Id));
            Assert.That(File.ReadAllText(
                Path.Combine(project.Path, "committed.txt")),
                Is.EqualTo("two"));
            Assert.That(File.ReadAllText(
                Path.Combine(project.Path, "working.txt")),
                Is.EqualTo("dirty"));
        });
    }

    [Test]
    public void UndoLatestCommit_NonLatestCommitIsUnchanged()
    {
        using var project = new TemporaryDirectory("revert-conflict");
        var service = new FolderProjectVersionControlService();
        var filePath = Path.Combine(project.Path, "conflict.txt");
        File.WriteAllText(filePath, "base");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(filePath, "target");
        var target = service.CommitAll(project.Path, "目标提交");
        File.WriteAllText(filePath, "later");
        var head = service.CommitAll(project.Path, "后续提交");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.UndoLatestCommit(
                project.Path,
                target.Id,
                FolderProjectCommitUndoMode.KeepChanges));

        var status = service.GetStatus(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(FolderProjectVersionControlError.CommitIsNotLatest));
            Assert.That(status.HeadCommitId, Is.EqualTo(head.Id));
            Assert.That(status.IsClean, Is.True);
            Assert.That(
                status.OperationState,
                Is.EqualTo(FolderProjectRepositoryOperationState.None));
            Assert.That(File.ReadAllText(filePath), Is.EqualTo("later"));
        });
    }

    [Test]
    public void UndoLatestCommit_InvalidModeIsUnchanged()
    {
        using var project = new TemporaryDirectory("undo-invalid-mode");
        var service = new FolderProjectVersionControlService();
        var filePath = Path.Combine(project.Path, "file.txt");
        File.WriteAllText(filePath, "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(filePath, "two");
        var committed = service.CommitAll(project.Path, "最新提交");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.UndoLatestCommit(
                project.Path,
                committed.Id,
                (FolderProjectCommitUndoMode)int.MaxValue));

        Assert.Multiple(() =>
        {
            Assert.That(
                service.GetStatus(project.Path).HeadCommitId,
                Is.EqualTo(committed.Id));
            Assert.That(File.ReadAllText(filePath), Is.EqualTo("two"));
        });
    }

    [Test]
    public void GetStatus_UnreadableModifiedFile_MergesStatusFlags()
    {
        using var project = new TemporaryDirectory("unreadable-modified");
        var repositoryPath = "modified.bin";
        var fullPath = Path.Combine(project.Path, repositoryPath);
        var setupService = new FolderProjectVersionControlService();
        File.WriteAllText(fullPath, "one");
        setupService.Initialize(project.Path, s_identity);
        File.WriteAllText(fullPath, "two");
        var platform = new StatusFileSystemPlatform(
            project.Path,
            "open",
            repositoryPath);
        var service = new FolderProjectVersionControlService(platform);

        var change = service.GetStatus(project.Path).Changes.Single(
            item => item.RepositoryPath == repositoryPath);

        Assert.That(
            change.Kind,
            Is.EqualTo(
                FolderProjectWorkingChangeKind.Modified |
                FolderProjectWorkingChangeKind.Unstaged |
                FolderProjectWorkingChangeKind.Unreadable));
    }

    [Test]
    public void GetStatus_IgnoredUnreadableFile_IsNotScannedOrReported()
    {
        using var project = new TemporaryDirectory("ignored-unreadable");
        var repositoryPath = "ignored.bin";
        var setupService = new FolderProjectVersionControlService();
        setupService.Initialize(project.Path, s_identity);
        File.AppendAllText(
            Path.Combine(project.Path, ".gitignore"),
            repositoryPath + "\r\n");
        File.WriteAllText(
            Path.Combine(project.Path, repositoryPath),
            "ignored");
        setupService.CommitAll(project.Path, "忽略测试资源");
        var platform = new StatusFileSystemPlatform(
            project.Path,
            "open",
            repositoryPath);
        var service = new FolderProjectVersionControlService(platform);

        var status = service.GetStatus(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsClean, Is.True);
            Assert.That(status.Changes, Is.Empty);
            Assert.That(platform.AccessedFailurePath, Is.False);
        });
    }

    [TestCase("tracked.bin", "tracked.bin")]
    [TestCase("dir/child.bin", "dir/")]
    public void GetStatus_TrackedFileLaterIgnored_IsStillScanned(
        string repositoryPath,
        string ignoreRule)
    {
        using var project = new TemporaryDirectory("tracked-later-ignored");
        var fullPath = Path.Combine(
            project.Path,
            repositoryPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "tracked");
        var setupService = new FolderProjectVersionControlService();
        setupService.Initialize(project.Path, s_identity);
        File.AppendAllText(
            Path.Combine(project.Path, ".gitignore"),
            ignoreRule + "\r\n");
        setupService.CommitAll(project.Path, "添加后续忽略规则");
        var platform = new StatusFileSystemPlatform(
            project.Path,
            "open",
            repositoryPath);
        var service = new FolderProjectVersionControlService(platform);

        var status = service.GetStatus(project.Path);
        var change = status.Changes.Single(
            item => item.RepositoryPath == repositoryPath);

        Assert.Multiple(() =>
        {
            Assert.That(
                change.Kind,
                Is.EqualTo(FolderProjectWorkingChangeKind.Unreadable));
            Assert.That(platform.AccessedFailurePath, Is.True);
        });
    }

    [TestCase("attributes")]
    [TestCase("open")]
    public void GetStatus_FileMatchingOnlyDirectoryIgnoreRule_IsScanned(
        string failurePoint)
    {
        using var project = new TemporaryDirectory(
            "file-directory-ignore-rule");
        var repositoryPath = "cache";
        var setupService = new FolderProjectVersionControlService();
        setupService.Initialize(project.Path, s_identity);
        File.AppendAllText(
            Path.Combine(project.Path, ".gitignore"),
            repositoryPath + "/\r\n");
        setupService.CommitAll(project.Path, "添加目录忽略规则");
        File.WriteAllText(
            Path.Combine(project.Path, repositoryPath),
            "ordinary file");
        var platform = new StatusFileSystemPlatform(
            project.Path,
            failurePoint,
            repositoryPath);
        var service = new FolderProjectVersionControlService(platform);

        var change = service.GetStatus(project.Path).Changes.Single(
            item => item.RepositoryPath == repositoryPath);

        Assert.Multiple(() =>
        {
            Assert.That(
                change.Kind.HasFlag(
                    FolderProjectWorkingChangeKind.Unreadable),
                Is.True);
            Assert.That(platform.AccessedFailurePath, Is.True);
        });
    }

    [Test]
    public void GetStatus_DirectoryMatchingDirectoryIgnoreRule_IsNotScanned()
    {
        using var project = new TemporaryDirectory(
            "directory-ignore-rule");
        var directoryPath = "cache";
        var childPath = directoryPath + "/child.bin";
        var setupService = new FolderProjectVersionControlService();
        setupService.Initialize(project.Path, s_identity);
        File.AppendAllText(
            Path.Combine(project.Path, ".gitignore"),
            directoryPath + "/\r\n");
        setupService.CommitAll(project.Path, "添加目录忽略规则");
        Directory.CreateDirectory(
            Path.Combine(project.Path, directoryPath));
        File.WriteAllText(
            Path.Combine(project.Path, "cache", "child.bin"),
            "ignored");
        var platform = new StatusFileSystemPlatform(
            project.Path,
            "open",
            childPath);
        var service = new FolderProjectVersionControlService(platform);

        var status = service.GetStatus(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsClean, Is.True);
            Assert.That(platform.AccessedFailurePath, Is.False);
        });
    }

    [Test]
    public void GetStatus_GitMetadataDirectory_IsNotScanned()
    {
        using var project = new TemporaryDirectory("git-not-scanned");
        var setupService = new FolderProjectVersionControlService();
        setupService.Initialize(project.Path, s_identity);
        var platform = new StatusFileSystemPlatform(project.Path);
        var service = new FolderProjectVersionControlService(platform);

        var status = service.GetStatus(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsClean, Is.True);
            Assert.That(platform.GitMetadataEnumerated, Is.False);
        });
    }

    [Test]
    public void GetStatus_NestedGitMetadataDirectory_IsNotScanned()
    {
        using var project = new TemporaryDirectory("nested-git-not-scanned");
        var nestedPath = Path.Combine(project.Path, "nested");
        Directory.CreateDirectory(nestedPath);
        File.WriteAllText(
            Path.Combine(nestedPath, "resource.bin"),
            "content");
        var setupService = new FolderProjectVersionControlService();
        setupService.Initialize(project.Path, s_identity);
        Directory.CreateDirectory(Path.Combine(nestedPath, ".git"));
        File.WriteAllText(
            Path.Combine(nestedPath, ".git", "secret"),
            "metadata");
        var platform = new StatusFileSystemPlatform(project.Path);
        var service = new FolderProjectVersionControlService(platform);

        _ = service.GetStatus(project.Path);

        Assert.That(platform.GitMetadataEnumerated, Is.False);
    }

    [Test]
    public void MapWorkingChange_Unreadable_RemainsVisible()
    {
        var kind = FolderProjectVersionControlService.MapWorkingChange(
            FileStatus.Unreadable);

        Assert.That(
            kind,
            Is.EqualTo(FolderProjectWorkingChangeKind.Unreadable));
    }

    [Test]
    public void GetStatus_DetachedAndMergeState_ReportsReadOnlyState()
    {
        using var project = new TemporaryDirectory("operation-status");
        var service = new FolderProjectVersionControlService();
        var commit = service.Initialize(project.Path, s_identity);
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, commit.Id);
        File.WriteAllText(
            Path.Combine(project.Path, ".git", "MERGE_HEAD"),
            commit.Id + "\n");

        var status = service.GetStatus(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsDetached, Is.True);
            Assert.That(status.CurrentBranch, Is.Null);
            Assert.That(
                status.OperationState,
                Is.EqualTo(FolderProjectRepositoryOperationState.Merge));
        });
    }

    [Test]
    public void GetStatus_NestedInsideParentRepository_DoesNotDiscoverParent()
    {
        using var parent = new TemporaryDirectory("status-parent");
        Repository.Init(parent.Path);
        var nested = Path.Combine(parent.Path, "nested", "工程");
        Directory.CreateDirectory(nested);
        var service = new FolderProjectVersionControlService();

        var status = service.GetStatus(nested);

        Assert.That(status.IsInitialized, Is.False);
    }

    [Test]
    public void GetStatus_GitFile_RejectsUnsupportedRepository()
    {
        using var project = new TemporaryDirectory("status-git-file");
        File.WriteAllText(
            Path.Combine(project.Path, ".git"),
            "gitdir: elsewhere");
        var service = new FolderProjectVersionControlService();

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.GetStatus(project.Path));

        Assert.That(
            exception!.Code,
            Is.EqualTo(
                FolderProjectVersionControlError.UnsupportedRepository));
    }

    [Test]
    public void GetStatus_GitJunction_RejectsUnsupportedRepository()
    {
        using var external = new TemporaryDirectory(
            "status-external-repository");
        Repository.Init(external.Path);
        using var project = new TemporaryDirectory("status-git-junction");
        var junctionPath = Path.Combine(project.Path, ".git");
        CreateDirectoryJunction(
            junctionPath,
            Path.Combine(external.Path, ".git"));
        try
        {
            var service = new FolderProjectVersionControlService();

            var exception =
                Assert.Throws<FolderProjectVersionControlException>(
                    () => service.GetStatus(project.Path));

            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.UnsupportedRepository));
        }
        finally
        {
            if (Directory.Exists(junctionPath))
                Directory.Delete(junctionPath);
        }
    }

    [Test]
    public void GetStatus_BareRepository_RejectsUnsupportedRepository()
    {
        using var project = new TemporaryDirectory("status-bare");
        Repository.Init(project.Path, isBare: true);
        var service = new FolderProjectVersionControlService();

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.GetStatus(project.Path));

        Assert.That(
            exception!.Code,
            Is.EqualTo(
                FolderProjectVersionControlError.UnsupportedRepository));
    }

    [Test]
    public void CommitAll_TrackedUntrackedDeletedAndBinary_CommitsEverything()
    {
        using var project = new TemporaryDirectory("commit-all");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "modified.txt"), "one");
        File.WriteAllText(Path.Combine(project.Path, "deleted.txt"), "one");
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(Path.Combine(project.Path, "modified.txt"), "two");
        File.Delete(Path.Combine(project.Path, "deleted.txt"));
        var binary = new byte[] { 0, 13, 10, 255, 1, 2, 3 };
        File.WriteAllBytes(Path.Combine(project.Path, "added.bin"), binary);
        File.AppendAllText(
            Path.Combine(project.Path, ".gitignore"),
            "ignored.bin\r\n");
        File.WriteAllBytes(Path.Combine(project.Path, "ignored.bin"), [9]);

        var commit = service.CommitAll(project.Path, "保存全部改动");

        using var repository = new Repository(project.Path);
        var blob = (Blob)repository.Head.Tip!.Tree["added.bin"].Target;
        using var stream = blob.GetContentStream();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        Assert.Multiple(() =>
        {
            Assert.That(commit.Message, Is.EqualTo("保存全部改动"));
            Assert.That(repository.Head.Tip.Sha, Is.EqualTo(commit.Id));
            Assert.That(
                ((Blob)repository.Head.Tip.Tree["modified.txt"].Target)
                    .GetContentText(),
                Is.EqualTo("two"));
            Assert.That(
                repository.Head.Tip.Tree["deleted.txt"],
                Is.Null);
            Assert.That(output.ToArray(), Is.EqualTo(binary));
            Assert.That(repository.Index["ignored.bin"], Is.Null);
            Assert.That(service.GetStatus(project.Path).IsClean, Is.True);
        });
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\r\n")]
    public void CommitAll_EmptyMessage_RejectsBeforeStaging(
        string message)
    {
        using var project = new TemporaryDirectory("empty-message");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        var pendingPath = Path.Combine(project.Path, "pending.txt");
        File.WriteAllText(pendingPath, "pending");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, message));

        using var repository = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.EmptyCommitMessage));
            Assert.That(repository.Head.Tip!.Sha, Is.EqualTo(initial.Id));
            Assert.That(repository.Index["pending.txt"], Is.Null);
            Assert.That(File.ReadAllText(pendingPath), Is.EqualTo("pending"));
        });
    }

    [Test]
    public void CommitAll_MissingLocalIdentity_RejectsBeforeStaging()
    {
        using var project = new TemporaryDirectory("missing-identity");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        using (var repository = new Repository(project.Path))
        {
            repository.Config.Unset(
                "user.name",
                ConfigurationLevel.Local);
            repository.Config.Unset(
                "user.email",
                ConfigurationLevel.Local);
        }
        var pendingPath = Path.Combine(project.Path, "pending.txt");
        File.WriteAllText(pendingPath, "pending");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "commit"));

        using var reopened = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.IdentityMissing));
            Assert.That(reopened.Head.Tip!.Sha, Is.EqualTo(initial.Id));
            Assert.That(reopened.Index["pending.txt"], Is.Null);
            Assert.That(File.ReadAllText(pendingPath), Is.EqualTo("pending"));
        });
    }

    [Test]
    public void CommitAll_NoChanges_RejectsWithoutChangingHead()
    {
        using var project = new TemporaryDirectory("nothing-to-commit");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "no changes"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.NothingToCommit));
            Assert.That(
                service.GetStatus(project.Path).HeadCommitId,
                Is.EqualTo(initial.Id));
        });
    }

    [Test]
    public void CommitAll_IgnoredOnlyChange_ReturnsNothingToCommit()
    {
        using var project = new TemporaryDirectory("ignored-only");
        File.WriteAllText(
            Path.Combine(project.Path, ".gitignore"),
            "ignored.bin\r\n");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        File.WriteAllBytes(Path.Combine(project.Path, "ignored.bin"), [1, 2]);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "ignored"));

        using var repository = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.NothingToCommit));
            Assert.That(repository.Head.Tip!.Sha, Is.EqualTo(initial.Id));
            Assert.That(repository.Index["ignored.bin"], Is.Null);
            Assert.That(
                File.ReadAllBytes(
                    Path.Combine(project.Path, "ignored.bin")),
                Is.EqualTo(new byte[] { 1, 2 }));
        });
    }

    [Test]
    public void CommitAll_IndexLock_ReturnsRepositoryBusyBeforeStaging()
    {
        using var project = new TemporaryDirectory("repository-busy");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        var pendingPath = Path.Combine(project.Path, "pending.txt");
        File.WriteAllText(pendingPath, "pending");
        var lockPath = Path.Combine(project.Path, ".git", "index.lock");
        File.WriteAllText(lockPath, "locked");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "busy"));

        File.Delete(lockPath);
        using var repository = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.RepositoryBusy));
            Assert.That(repository.Head.Tip!.Sha, Is.EqualTo(initial.Id));
            Assert.That(repository.Index["pending.txt"], Is.Null);
            Assert.That(File.ReadAllText(pendingPath), Is.EqualTo("pending"));
        });
    }

    [Test]
    public void CommitAll_DetachedHead_RejectsBeforeStaging()
    {
        using var project = new TemporaryDirectory("detached-commit");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, initial.Id);
        var pendingPath = Path.Combine(project.Path, "pending.txt");
        File.WriteAllText(pendingPath, "pending");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "detached"));

        using var reopened = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError
                        .UnsupportedOperationState));
            Assert.That(reopened.Head.Tip!.Sha, Is.EqualTo(initial.Id));
            Assert.That(reopened.Index["pending.txt"], Is.Null);
            Assert.That(File.ReadAllText(pendingPath), Is.EqualTo("pending"));
        });
    }

    [Test]
    public void CommitAll_MergeInProgress_RejectsBeforeStaging()
    {
        using var project = new TemporaryDirectory("merge-operation");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        File.WriteAllText(
            Path.Combine(project.Path, ".git", "MERGE_HEAD"),
            initial.Id + "\n");
        var pendingPath = Path.Combine(project.Path, "pending.txt");
        File.WriteAllText(pendingPath, "pending");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "merge"));

        using var repository = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError
                        .UnsupportedOperationState));
            Assert.That(repository.Head.Tip!.Sha, Is.EqualTo(initial.Id));
            Assert.That(repository.Index["pending.txt"], Is.Null);
            Assert.That(File.ReadAllText(pendingPath), Is.EqualTo("pending"));
        });
    }

    [Test]
    public void CommitAll_Conflict_RejectsBeforeStagingUnrelatedFile()
    {
        using var project = new TemporaryDirectory("conflict");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "conflict.txt"), "base");
        service.Initialize(project.Path, s_identity);
        CreateMergeConflict(project.Path);
        var pendingPath = Path.Combine(project.Path, "pending.txt");
        File.WriteAllText(pendingPath, "pending");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "conflict"));

        using var repository = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError
                        .UnsupportedOperationState));
            Assert.That(repository.Index["pending.txt"], Is.Null);
            Assert.That(File.ReadAllText(pendingPath), Is.EqualTo("pending"));
            Assert.That(repository.Index.Conflicts, Is.Not.Empty);
        });
    }

    [Test]
    public void GetHistory_ReturnsNewestFirstWithLimitAndShortId()
    {
        using var project = new TemporaryDirectory("history");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        File.WriteAllText(Path.Combine(project.Path, "one.txt"), "one");
        var second = service.CommitAll(project.Path, "第二次");
        File.WriteAllText(Path.Combine(project.Path, "two.txt"), "two");
        var third = service.CommitAll(project.Path, "第三次");

        var history = service.GetHistory(project.Path, 2);

        Assert.Multiple(() =>
        {
            Assert.That(
                history.Select(commit => commit.Id),
                Is.EqualTo(new[] { third.Id, second.Id }));
            Assert.That(history[0].ShortId, Is.EqualTo(third.Id[..7]));
            Assert.That(history[0].ParentIds, Is.EqualTo(new[] { second.Id }));
        });
    }

    [Test]
    public void GetHistory_HidesInitialCommitFromEveryBranch()
    {
        using var project = new TemporaryDirectory("history-hide-initial");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        service.CreateBranch(project.Path, "feature");
        File.WriteAllText(Path.Combine(project.Path, "main.txt"), "main");
        var main = service.CommitAll(project.Path, "主支修改");
        service.SwitchBranch(project.Path, "feature");
        File.WriteAllText(Path.Combine(project.Path, "feature.txt"), "feature");
        var feature = service.CommitAll(project.Path, "分支修改");

        var mainHistory = service.GetHistory(project.Path, "master");
        var featureHistory = service.GetHistory(project.Path, "feature");

        Assert.Multiple(() =>
        {
            Assert.That(
                mainHistory.Select(commit => commit.Id),
                Is.EqualTo(new[] { main.Id }));
            Assert.That(
                featureHistory.Select(commit => commit.Id),
                Is.EqualTo(new[] { feature.Id }));
            Assert.That(
                mainHistory.Concat(featureHistory),
                Has.None.Matches<FolderProjectCommitSummary>(
                    commit => commit.Id == initial.Id));
        });
    }

    [Test]
    public void GetHistory_SplitsTitleAndDescriptionAndReportsMasterMergeStatus()
    {
        using var project = new TemporaryDirectory("history-details");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        using (var repository = new Repository(project.Path))
            repository.CreateBranch("feature");
        File.WriteAllText(Path.Combine(project.Path, "main.txt"), "main");
        service.CommitAll(project.Path, "主线标题\n\n主线说明");
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, "feature");
        File.WriteAllText(Path.Combine(project.Path, "feature.txt"), "feature");
        var feature = service.CommitAll(
            project.Path,
            "功能标题\n\n第一行说明\n第二行说明");

        var history = service.GetHistory(project.Path, "feature", 100);
        var featureSummary = history.Single(commit => commit.Id == feature.Id);

        Assert.Multiple(() =>
        {
            Assert.That(featureSummary.Title, Is.EqualTo("功能标题"));
            Assert.That(
                featureSummary.Description,
                Is.EqualTo("第一行说明\n第二行说明"));
            Assert.That(
                featureSummary.MergeStatus,
                Is.EqualTo(FolderProjectCommitMergeStatus.NotMerged));
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void GetHistory_NonPositiveLimit_Rejects(int maxCount)
    {
        using var project = new TemporaryDirectory("history-limit");
        var service = new FolderProjectVersionControlService();

        Assert.That(
            () => service.GetHistory(project.Path, maxCount),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void GetHistory_UninitializedRepository_ReturnsDomainError()
    {
        using var project = new TemporaryDirectory("history-uninitialized");
        var service = new FolderProjectVersionControlService();

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.GetHistory(project.Path));

        Assert.That(
            exception!.Code,
            Is.EqualTo(
                FolderProjectVersionControlError.RepositoryNotInitialized));
    }

    [Test]
    public void GetHistory_UsesOnlyCommitsReachableFromCurrentHead()
    {
        using var project = new TemporaryDirectory("history-head");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        using (var repository = new Repository(project.Path))
            repository.CreateBranch("other");
        File.WriteAllText(Path.Combine(project.Path, "main.txt"), "main");
        var mainCommit = service.CommitAll(project.Path, "main");
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, "other");
        File.WriteAllText(Path.Combine(project.Path, "other.txt"), "other");
        var otherCommit = service.CommitAll(project.Path, "other");

        var branchHistory = service.GetHistory(project.Path);
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, initial.Id);
        var detachedHistory = service.GetHistory(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                branchHistory.Select(commit => commit.Id),
                Is.EqualTo(new[] { otherCommit.Id }));
            Assert.That(
                branchHistory.Select(commit => commit.Id),
                Does.Not.Contain(mainCommit.Id));
            Assert.That(
                detachedHistory.Select(commit => commit.Id),
                Is.Empty);
        });
    }

    [Test]
    public void GetHistory_SpecifiedBranch_DoesNotCheckoutBranch()
    {
        using var project = new TemporaryDirectory("history-branch");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        using (var repository = new Repository(project.Path))
            repository.CreateBranch("feature");
        File.WriteAllText(Path.Combine(project.Path, "main.txt"), "main");
        var mainCommit = service.CommitAll(project.Path, "main");
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, "feature");
        File.WriteAllText(Path.Combine(project.Path, "feature.txt"), "feature");
        var featureCommit = service.CommitAll(project.Path, "feature");
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, "master");

        var history = service.GetHistory(project.Path, "feature", 100);

        using var verification = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                history.Select(commit => commit.Id),
                Is.EqualTo(new[] { featureCommit.Id }));
            Assert.That(
                history.Select(commit => commit.Id),
                Does.Not.Contain(mainCommit.Id));
            Assert.That(
                verification.Head.FriendlyName,
                Is.EqualTo("master"));
        });
    }

    [Test]
    public void GetCommitChanges_RootCommit_ReportsAllFilesAsAdded()
    {
        using var project = new TemporaryDirectory("root-diff");
        var binary = new byte[] { 0, 255, 13, 10, 42 };
        File.WriteAllBytes(Path.Combine(project.Path, "root.bin"), binary);
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);

        var changes = service.GetCommitChanges(project.Path, initial.Id);

        Assert.Multiple(() =>
        {
            Assert.That(
                changes.Select(change => change.RepositoryPath),
                Does.Contain("root.bin"));
            Assert.That(
                changes,
                Has.All.Property("Kind")
                    .EqualTo(FolderProjectCommitChangeKind.Added));
            Assert.That(
                changes.Single(
                    change => change.RepositoryPath == "root.bin")
                    .IsBinary,
                Is.True);
        });
    }

    [Test]
    public void GetCommitChanges_LaterCommit_MapsModifyDeleteAndRename()
    {
        using var project = new TemporaryDirectory("later-diff");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "modified.txt"), "one");
        File.WriteAllText(Path.Combine(project.Path, "deleted.txt"), "one");
        File.WriteAllText(
            Path.Combine(project.Path, "rename-old.txt"),
            "rename payload");
        service.Initialize(project.Path, s_identity);
        using (var repository = new Repository(project.Path))
        {
            repository.Config.Set(
                "diff.renames",
                false,
                ConfigurationLevel.Local);
        }
        File.WriteAllText(Path.Combine(project.Path, "modified.txt"), "two");
        File.Delete(Path.Combine(project.Path, "deleted.txt"));
        File.Move(
            Path.Combine(project.Path, "rename-old.txt"),
            Path.Combine(project.Path, "rename-new.txt"));
        var commit = service.CommitAll(project.Path, "later changes");

        var changes = service.GetCommitChanges(project.Path, commit.Id);

        Assert.Multiple(() =>
        {
            Assert.That(
                changes.Single(
                    change => change.RepositoryPath == "modified.txt").Kind,
                Is.EqualTo(FolderProjectCommitChangeKind.Modified));
            Assert.That(
                changes.Single(
                    change => change.RepositoryPath == "deleted.txt").Kind,
                Is.EqualTo(FolderProjectCommitChangeKind.Deleted));
            var renamed = changes.Single(
                change => change.Kind ==
                          FolderProjectCommitChangeKind.Renamed);
            Assert.That(renamed.RepositoryPath, Is.EqualTo("rename-new.txt"));
            Assert.That(
                renamed.PreviousRepositoryPath,
                Is.EqualTo("rename-old.txt"));
        });
    }

    [Test]
    public void GetCommitChanges_TypeChangedCommit_MapsTypeChange()
    {
        using var project = new TemporaryDirectory("type-change");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "type-item"), "file");
        service.Initialize(project.Path, s_identity);
        var commitId = CreateTypeChangeCommit(project.Path, "type-item");

        var changes = service.GetCommitChanges(project.Path, commitId);

        Assert.That(
            changes.Single(
                change => change.RepositoryPath == "type-item").Kind,
            Is.EqualTo(FolderProjectCommitChangeKind.TypeChanged));
    }

    [Test]
    public void GetCommitChanges_MergeCommit_ComparesWithFirstParent()
    {
        using var project = new TemporaryDirectory("merge-diff");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);
        string mainBranchName;
        using (var repository = new Repository(project.Path))
        {
            mainBranchName = repository.Head.FriendlyName;
            repository.CreateBranch("other");
        }
        File.WriteAllText(Path.Combine(project.Path, "main.txt"), "main");
        service.CommitAll(project.Path, "main");
        using (var repository = new Repository(project.Path))
            Commands.Checkout(repository, "other");
        File.WriteAllText(Path.Combine(project.Path, "other.txt"), "other");
        service.CommitAll(project.Path, "other");
        string mergeCommitId;
        using (var repository = new Repository(project.Path))
        {
            Commands.Checkout(repository, mainBranchName);
            var signature = new Signature(
                s_identity.Name,
                s_identity.Email,
                DateTimeOffset.Now);
            var result = repository.Merge(
                repository.Branches["other"],
                signature,
                new MergeOptions
                {
                    FastForwardStrategy = FastForwardStrategy.NoFastForward,
                });
            Assert.That(result.Status, Is.EqualTo(MergeStatus.NonFastForward));
            mergeCommitId = repository.Head.Tip!.Sha;
        }

        var changes = service.GetCommitChanges(
            project.Path,
            mergeCommitId);

        Assert.Multiple(() =>
        {
            Assert.That(changes, Has.Count.EqualTo(1));
            Assert.That(
                changes[0].RepositoryPath,
                Is.EqualTo("other.txt"));
            Assert.That(
                changes[0].Kind,
                Is.EqualTo(FolderProjectCommitChangeKind.Added));
        });
    }

    [TestCase("")]
    [TestCase("abc1234")]
    [TestCase("HEAD")]
    [TestCase("HEAD~1")]
    [TestCase("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void GetCommitChanges_InvalidFullCommitId_Rejects(string commitId)
    {
        using var project = new TemporaryDirectory("invalid-commit-id");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.GetCommitChanges(project.Path, commitId));

        Assert.That(
            exception!.Code,
            Is.EqualTo(FolderProjectVersionControlError.InvalidCommitId));
    }

    [Test]
    public void GetCommitChanges_UppercaseFullCommitId_Succeeds()
    {
        using var project = new TemporaryDirectory("uppercase-commit-id");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);

        var changes = service.GetCommitChanges(
            project.Path,
            initial.Id.ToUpperInvariant());

        Assert.That(changes, Is.Not.Empty);
    }

    [Test]
    public void GetCommitChanges_UnknownFullCommitId_ReturnsCommitNotFound()
    {
        using var project = new TemporaryDirectory("unknown-commit");
        var service = new FolderProjectVersionControlService();
        service.Initialize(project.Path, s_identity);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.GetCommitChanges(
                project.Path,
                new string('0', 40)));

        Assert.That(
            exception!.Code,
            Is.EqualTo(FolderProjectVersionControlError.CommitNotFound));
    }

    [Test]
    public void DependencyInjection_ResolvesVersionControlServiceAsSingleton()
    {
        var services = new ServiceCollection();
        new DependencyInjectionContainer().Register(services);
        using var provider = services.BuildServiceProvider();

        var first =
            provider.GetRequiredService<IFolderProjectVersionControlService>();
        var second =
            provider.GetRequiredService<IFolderProjectVersionControlService>();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf<FolderProjectVersionControlService>());
            Assert.That(second, Is.SameAs(first));
        });
    }

    [Test]
    public void CommitAll_PostStageFailure_RestoresExactOriginalIndex()
    {
        using var project = new TemporaryDirectory("commit-index-rollback");
        var setupService = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "tracked.txt"), "initial");
        var initial = setupService.Initialize(project.Path, s_identity);
        var trackedPath = Path.Combine(project.Path, "tracked.txt");
        File.WriteAllText(trackedPath, "staged before call");
        using (var repository = new Repository(project.Path))
            Commands.Stage(repository, "tracked.txt");
        var untrackedPath = Path.Combine(project.Path, "untracked.txt");
        File.WriteAllText(untrackedPath, "working only");
        var indexPath = Path.Combine(project.Path, ".git", "index");
        var indexBefore = CaptureFile(indexPath);
        var platform = new FailCommitPlatform();
        var service = new FolderProjectVersionControlService(platform);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "must fail"));

        var indexAfter = CaptureFile(indexPath);
        using var reopened = new Repository(project.Path);
        var status = reopened.RetrieveStatus();
        Assert.Multiple(() =>
        {
            Assert.That(platform.SawUntrackedFileStaged, Is.True);
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(indexAfter, Is.EqualTo(indexBefore));
            Assert.That(reopened.Head.Tip!.Sha, Is.EqualTo(initial.Id));
            Assert.That(File.ReadAllText(trackedPath), Is.EqualTo(
                "staged before call"));
            Assert.That(File.ReadAllText(untrackedPath), Is.EqualTo(
                "working only"));
            Assert.That(
                status["tracked.txt"].State.HasFlag(
                    FileStatus.ModifiedInIndex),
                Is.True);
            Assert.That(
                status["untracked.txt"].State.HasFlag(
                    FileStatus.NewInWorkdir),
                Is.True);
            Assert.That(
                status["untracked.txt"].State.HasFlag(
                    FileStatus.NewInIndex),
                Is.False);
        });
    }

    [Test]
    public void CommitAll_CommitSucceedsThenThrows_DoesNotRestoreOldIndex()
    {
        using var project = new TemporaryDirectory(
            "commit-succeeded-then-threw");
        var setupService = new FolderProjectVersionControlService();
        var trackedPath = Path.Combine(project.Path, "tracked.txt");
        File.WriteAllText(trackedPath, "initial");
        var initial = setupService.Initialize(project.Path, s_identity);
        File.WriteAllText(trackedPath, "committed");
        var platform = new CommitThenThrowPlatform();
        var service = new FolderProjectVersionControlService(platform);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "committed before failure"));

        using var repository = new Repository(project.Path);
        var status = repository.RetrieveStatus();
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(platform.CommittedId, Is.Not.Null);
            Assert.That(
                repository.Head.Tip!.Sha,
                Is.EqualTo(platform.CommittedId));
            Assert.That(repository.Head.Tip.Sha, Is.Not.EqualTo(initial.Id));
            Assert.That(status, Is.Empty);
            Assert.That(
                repository.Head.Tip.Tree["tracked.txt"],
                Is.Not.Null);
        });
    }

    [Test]
    public void CommitAll_IndexReplaceFailure_DoesNotTruncateIndexOrLeaveTemp()
    {
        using var project = new TemporaryDirectory(
            "commit-index-atomic-failure");
        var setupService = new FolderProjectVersionControlService();
        var trackedPath = Path.Combine(project.Path, "tracked.txt");
        File.WriteAllText(trackedPath, "initial");
        var initial = setupService.Initialize(project.Path, s_identity);
        File.WriteAllText(trackedPath, "changed");
        File.WriteAllText(
            Path.Combine(project.Path, "untracked.txt"),
            "untracked");
        var indexPath = Path.Combine(project.Path, ".git", "index");
        var indexBefore = CaptureFile(indexPath);
        var platform = new FailIndexReplacePlatform();
        var service = new FolderProjectVersionControlService(platform);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "must fail atomically"));

        var indexAfter = CaptureFile(indexPath);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(exception.InnerException, Is.TypeOf<AggregateException>());
            Assert.That(platform.IndexBytesAtReplaceFailure, Is.Not.Null);
            Assert.That(
                indexAfter.BytesBase64,
                Is.EqualTo(
                    Convert.ToBase64String(
                        platform.IndexBytesAtReplaceFailure!)));
            Assert.That(indexAfter, Is.Not.EqualTo(indexBefore));
            Assert.That(platform.TemporaryPath, Is.Not.Null);
            Assert.That(File.Exists(platform.TemporaryPath), Is.False);
            Assert.That(ReadHeadId(project.Path), Is.EqualTo(initial.Id));
        });
    }

    [Test]
    public void CommitAll_InvalidStoredIdentity_RejectsBeforeStaging()
    {
        using var project = new TemporaryDirectory("invalid-stored-identity");
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(project.Path, s_identity);
        using (var repository = new Repository(project.Path))
        {
            repository.Config.Set(
                "user.name",
                "Bad\nName",
                ConfigurationLevel.Local);
        }
        var pendingPath = Path.Combine(project.Path, "pending.txt");
        File.WriteAllText(pendingPath, "pending");
        var indexPath = Path.Combine(project.Path, ".git", "index");
        var indexBefore = CaptureFile(indexPath);

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.CommitAll(project.Path, "invalid identity"));

        using var reopened = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.InvalidIdentity));
            Assert.That(CaptureFile(indexPath), Is.EqualTo(indexBefore));
            Assert.That(reopened.Head.Tip!.Sha, Is.EqualTo(initial.Id));
            Assert.That(reopened.Index["pending.txt"], Is.Null);
            Assert.That(File.ReadAllText(pendingPath), Is.EqualTo("pending"));
        });
    }

    [TestCase("detached")]
    [TestCase("merge")]
    [TestCase("conflict")]
    [TestCase("index-lock")]
    public void SetIdentity_UnsafeRepository_RejectsWithoutChangingConfig(
        string unsafeState)
    {
        using var project = new TemporaryDirectory(
            $"identity-guard-{unsafeState}");
        var service = new FolderProjectVersionControlService();
        File.WriteAllText(Path.Combine(project.Path, "conflict.txt"), "base");
        var initial = service.Initialize(project.Path, s_identity);
        ConfigureUnsafeState(project.Path, initial.Id, unsafeState);
        var replacement =
            new FolderProjectGitIdentity("Other", "other@example.invalid");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.SetIdentity(project.Path, replacement));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    unsafeState == "index-lock"
                        ? FolderProjectVersionControlError.RepositoryBusy
                        : FolderProjectVersionControlError
                            .UnsupportedOperationState));
            Assert.That(service.GetIdentity(project.Path), Is.EqualTo(s_identity));
        });
    }

    [Test]
    public void SetIdentity_SecondConfigWriteFails_RestoresBothLocalKeys()
    {
        using var project = new TemporaryDirectory("identity-transaction");
        var setupService = new FolderProjectVersionControlService();
        setupService.Initialize(project.Path, s_identity);
        var service = new FolderProjectVersionControlService(
            new FailSecondIdentityWritePlatform());
        var replacement =
            new FolderProjectGitIdentity("Other", "other@example.invalid");

        var exception = Assert.Throws<FolderProjectVersionControlException>(
            () => service.SetIdentity(project.Path, replacement));

        using var repository = new Repository(project.Path);
        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Code,
                Is.EqualTo(
                    FolderProjectVersionControlError.RepositoryFailure));
            Assert.That(
                repository.Config.Get<string>(
                    "user.name",
                    ConfigurationLevel.Local)?.Value,
                Is.EqualTo(s_identity.Name));
            Assert.That(
                repository.Config.Get<string>(
                    "user.email",
                    ConfigurationLevel.Local)?.Value,
                Is.EqualTo(s_identity.Email));
        });
    }

    private static void ConfigureUnsafeState(
        string projectPath,
        string commitId,
        string unsafeState)
    {
        if (unsafeState == "detached")
        {
            using var repository = new Repository(projectPath);
            Commands.Checkout(repository, commitId);
            return;
        }
        if (unsafeState == "conflict")
        {
            CreateMergeConflict(projectPath);
            return;
        }

        var metadataPath = unsafeState switch
        {
            "merge" => "MERGE_HEAD",
            "index-lock" => "index.lock",
            _ => throw new ArgumentOutOfRangeException(nameof(unsafeState)),
        };
        File.WriteAllText(
            Path.Combine(projectPath, ".git", metadataPath),
            commitId + "\n");
    }

    private static CapturedFile CaptureFile(string path)
    {
        if (!File.Exists(path))
            return new CapturedFile(false, "", FileAttributes.Normal);

        return new CapturedFile(
            true,
            Convert.ToBase64String(File.ReadAllBytes(path)),
            File.GetAttributes(path));
    }

    private static IReadOnlyList<string>
        PreparePolicyFilesForFoundationUpdate(string projectPath)
    {
        var paths = new[]
        {
            Path.Combine(projectPath, ".gitignore"),
            Path.Combine(projectPath, ".gitattributes"),
            Path.Combine(projectPath, ".git", "info", "exclude"),
            Path.Combine(projectPath, ".git", "info", "attributes"),
        };
        File.WriteAllText(
            paths[0],
            "用户忽略规则\r\n",
            System.Text.Encoding.Unicode);
        File.WriteAllText(
            paths[1],
            "用户属性规则\r\n",
            System.Text.Encoding.Unicode);
        File.WriteAllText(paths[2], "custom exclude\r\n");
        File.WriteAllText(paths[3], "custom attributes\r\n");
        return paths;
    }

    private static string ReadHeadId(string projectPath)
    {
        using var repository = new Repository(projectPath);
        return repository.Head.Tip!.Sha;
    }

    private static void CreateDirectoryJunction(
        string junctionPath,
        string targetPath)
    {
        using var process = Process.Start(
            new ProcessStartInfo(
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

    private static string CreateTypeChangeCommit(
        string projectPath,
        string repositoryPath)
    {
        using var repository = new Repository(projectPath);
        var parent = repository.Head.Tip!;
        var definition = TreeDefinition.From(parent.Tree);
        definition.Remove(repositoryPath);
        definition.AddGitLink(repositoryPath, parent.Id);
        var tree = repository.ObjectDatabase.CreateTree(definition);
        var signature = new Signature(
            s_identity.Name,
            s_identity.Email,
            DateTimeOffset.Now);
        var commit = repository.ObjectDatabase.CreateCommit(
            signature,
            signature,
            "type change",
            tree,
            [parent],
            prettifyMessage: false);
        repository.Refs.UpdateTarget(
            repository.Head.CanonicalName,
            commit.Sha);
        return commit.Sha;
    }

    private static void CreateMergeConflict(string projectPath)
    {
        using var repository = new Repository(projectPath);
        var signature = new Signature(
            s_identity.Name,
            s_identity.Email,
            DateTimeOffset.Now);
        var originalBranchName = repository.Head.FriendlyName;
        var otherBranch = repository.CreateBranch("other");
        File.WriteAllText(
            Path.Combine(projectPath, "conflict.txt"),
            "main");
        Commands.Stage(repository, "conflict.txt");
        repository.Commit("main", signature, signature);
        Commands.Checkout(repository, otherBranch);
        File.WriteAllText(
            Path.Combine(projectPath, "conflict.txt"),
            "other");
        Commands.Stage(repository, "conflict.txt");
        repository.Commit("other", signature, signature);
        Commands.Checkout(repository, originalBranchName);

        var result = repository.Merge(otherBranch, signature);

        Assert.That(result.Status, Is.EqualTo(MergeStatus.Conflicts));
    }

    private sealed record CapturedFile(
        bool Exists,
        string BytesBase64,
        FileAttributes Attributes);

    private sealed class FailSecondIdentityWritePlatform :
        FolderProjectVersionControlPlatform
    {
        private int _writeCount;

        public override void SetLocalConfig(
            Repository repository,
            string key,
            string value)
        {
            _writeCount++;
            if (_writeCount == 2)
                throw new IOException("Injected second config write failure.");

            base.SetLocalConfig(repository, key, value);
        }
    }

    private sealed class FailCommitPlatform :
        FolderProjectVersionControlPlatform
    {
        public bool SawUntrackedFileStaged { get; private set; }

        public override Commit Commit(
            Repository repository,
            string message,
            Signature signature)
        {
            SawUntrackedFileStaged =
                repository.Index["untracked.txt"] != null;
            throw new IOException("Injected commit failure.");
        }
    }

    private sealed class FoundationFailurePlatform :
        FolderProjectVersionControlPlatform
    {
        private readonly Exception _failure;

        public FoundationFailurePlatform(Exception failure)
        {
            _failure = failure;
        }

        public override void InitializeRepository(string projectRoot)
        {
            throw _failure;
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
            var commit = base.Commit(
                repository,
                message,
                signature);
            CommittedId = commit.Sha;
            throw new IOException("Injected post-commit failure.");
        }
    }

    private sealed class FailIndexReplacePlatform :
        FolderProjectVersionControlPlatform
    {
        public byte[]? IndexBytesAtReplaceFailure { get; private set; }

        public string? TemporaryPath { get; private set; }

        public override Commit Commit(
            Repository repository,
            string message,
            Signature signature)
        {
            throw new IOException("Injected commit failure.");
        }

        public override void MoveFile(
            string sourcePath,
            string destinationPath,
            bool overwrite)
        {
            TemporaryPath = sourcePath;
            IndexBytesAtReplaceFailure =
                File.ReadAllBytes(destinationPath);
            throw new IOException("Injected atomic replace failure.");
        }
    }

    private sealed class PostInitializeUnsafeStatePlatform :
        FolderProjectVersionControlPlatform,
        IDisposable
    {
        private readonly string _unsafeState;
        private readonly string? _policyPathToLock;
        private FileStream? _policyLock;

        public PostInitializeUnsafeStatePlatform(
            string unsafeState,
            string? policyPathToLock = null)
        {
            _unsafeState = unsafeState;
            _policyPathToLock = policyPathToLock;
        }

        public override void InitializeRepository(string projectRoot)
        {
            base.InitializeRepository(projectRoot);
            if (_policyPathToLock != null)
            {
                _policyLock = new FileStream(
                    _policyPathToLock,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
            }

            string? headId;
            using (var repository = new Repository(projectRoot))
                headId = repository.Head.Tip?.Sha;
            var metadataPath = Path.Combine(projectRoot, ".git");
            switch (_unsafeState)
            {
                case "detached":
                    File.WriteAllText(
                        Path.Combine(metadataPath, "HEAD"),
                        headId + "\n");
                    break;
                case "merge":
                    File.WriteAllText(
                        Path.Combine(metadataPath, "MERGE_HEAD"),
                        headId + "\n");
                    break;
                case "index-lock":
                    File.WriteAllText(
                        Path.Combine(metadataPath, "index.lock"),
                        "locked");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(_unsafeState));
            }
        }

        public void Dispose()
        {
            _policyLock?.Dispose();
        }
    }

    private sealed class StatusFileSystemPlatform :
        FolderProjectVersionControlPlatform
    {
        private readonly string _projectRoot;
        private readonly string? _failurePoint;
        private readonly string? _failureRepositoryPath;

        public bool AccessedFailurePath { get; private set; }

        public bool GitMetadataEnumerated { get; private set; }

        public StatusFileSystemPlatform(
            string projectRoot,
            string? failurePoint = null,
            string? failureRepositoryPath = null)
        {
            _projectRoot = Path.GetFullPath(projectRoot);
            _failurePoint = failurePoint;
            _failureRepositoryPath = failureRepositoryPath;
        }

        public override IReadOnlyList<string> GetFileSystemEntries(
            string path)
        {
            var repositoryPath = GetRepositoryPath(path);
            if (repositoryPath.Equals(
                    ".git",
                    StringComparison.OrdinalIgnoreCase) ||
                repositoryPath.Contains(
                    "/.git/",
                    StringComparison.OrdinalIgnoreCase) ||
                repositoryPath.EndsWith(
                    "/.git",
                    StringComparison.OrdinalIgnoreCase))
            {
                GitMetadataEnumerated = true;
            }
            if (ShouldFail("enumerate", repositoryPath))
                throw new UnauthorizedAccessException("Injected failure.");

            return base.GetFileSystemEntries(path);
        }

        public override FileAttributes GetAttributes(string path)
        {
            var repositoryPath = GetRepositoryPath(path);
            if (ShouldFail("attributes", repositoryPath))
                throw new UnauthorizedAccessException("Injected failure.");

            return base.GetAttributes(path);
        }

        public override Stream OpenReadForStatus(string path)
        {
            var repositoryPath = GetRepositoryPath(path);
            if (ShouldFail("open", repositoryPath))
                throw new UnauthorizedAccessException("Injected failure.");

            return base.OpenReadForStatus(path);
        }

        private bool ShouldFail(
            string failurePoint,
            string repositoryPath)
        {
            if (!string.Equals(
                    _failurePoint,
                    failurePoint,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    _failureRepositoryPath,
                    repositoryPath,
                    StringComparison.Ordinal))
            {
                return false;
            }

            AccessedFailurePath = true;
            return true;
        }

        private string GetRepositoryPath(string path)
        {
            var relativePath = Path.GetRelativePath(
                _projectRoot,
                Path.GetFullPath(path));
            return relativePath == "."
                ? ""
                : relativePath.Replace('\\', '/');
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; }

        public TemporaryDirectory(string suffix)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ae-folder-vcs-{suffix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                foreach (var file in Directory.EnumerateFiles(
                             Path,
                             "*",
                             SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(Path, true);
            }
        }
    }
}
