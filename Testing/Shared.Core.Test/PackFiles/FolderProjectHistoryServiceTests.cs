using LibGit2Sharp;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace Test.Shared.Core.PackFiles;

public sealed class FolderProjectHistoryServiceTests
{
    [Test]
    public void PublicContract_OnlyExposesProjectHistoryCapabilities()
    {
        var methodNames = typeof(IFolderProjectHistoryService)
            .GetMethods()
            .Select(method => method.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();
        var publicContract = string.Join(
            " ",
            typeof(IFolderProjectHistoryService)
                .GetMethods()
                .Select(method => method.ToString()));

        Assert.Multiple(() =>
        {
            Assert.That(
                methodNames,
                Is.EqualTo(new[]
                {
                    "BeginDiscardChanges",
                    "BeginRecoverToSafeState",
                    "BeginRestoreFile",
                    "CompleteDiscardChanges",
                    "CompleteRecoverToSafeState",
                    "CompleteRestoreFile",
                    "CreateRestorePoint",
                    "GetDisplayStatus",
                    "GetRestoreImpactCount",
                    "GetRestorePointChanges",
                    "GetRestorePoints",
                    "GetStatus",
                    "Initialize",
                    "RecoverToSafeState",
                    "RestoreProject",
                    "RollbackDiscardChanges",
                    "RollbackProjectRestore",
                    "RollbackRecoverToSafeState",
                    "RollbackRestoreFile",
                }));
            Assert.That(publicContract, Does.Not.Contain("Identity"));
            Assert.That(publicContract, Does.Not.Contain("Branch"));
            Assert.That(publicContract, Does.Not.Contain("Stage"));
            Assert.That(publicContract, Does.Not.Contain("Stash"));
            Assert.That(publicContract, Does.Not.Contain("Merge"));
            Assert.That(
                typeof(IFolderProjectVersionControlService).IsPublic,
                Is.False);
            Assert.That(
                typeof(FolderProjectVersionControlService).IsPublic,
                Is.False);
            Assert.That(
                typeof(FolderProjectGitRepository).IsPublic,
                Is.False);
        });
    }

    [Test]
    public void Initialize_CreatesInitialRestorePointAndCleanHistory()
    {
        using var project = new TemporaryProject();
        File.WriteAllBytes(
            Path.Combine(project.Root, "db", "entry.bin"),
            [1, 2, 3]);
        var service = CreateService();

        var initial = service.Initialize(project.Root);
        var status = service.GetStatus(project.Root);
        var history = service.GetRestorePoints(project.Root);

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(Path.Combine(project.Root, ".git")),
                Is.True);
            Assert.That(
                File.ReadAllText(
                    Path.Combine(project.Root, ".gitattributes")),
                Does.Contain("* -text"));
            Assert.That(
                File.ReadAllText(Path.Combine(project.Root, ".gitignore")),
                Does.Contain(".tmp"));
            Assert.That(initial.Description, Is.EqualTo("初始还原点"));
            Assert.That(initial.IsInitial, Is.True);
            Assert.That(status.Availability, Is.EqualTo(
                FolderProjectHistoryAvailability.Ready));
            Assert.That(status.UnrecordedChanges, Is.Empty);
            Assert.That(history.Select(item => item.Id),
                Is.EqualTo(new[] { initial.Id }));
        });
    }

    [Test]
    public void GetStatus_CombinesStagedAndUnstagedStateAsOneUnrecordedChange()
    {
        using var project = new TemporaryProject();
        var path = Path.Combine(project.Root, "db", "mixed.bin");
        File.WriteAllBytes(path, [1]);
        var service = CreateService();
        service.Initialize(project.Root);

        File.WriteAllBytes(path, [2]);
        using (var repository = new Repository(project.Root))
            Commands.Stage(repository, "db/mixed.bin");
        File.WriteAllBytes(path, [3]);

        var status = service.GetStatus(project.Root);

        Assert.That(status.UnrecordedChanges, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(
                status.UnrecordedChanges[0].Path,
                Is.EqualTo("db/mixed.bin"));
            Assert.That(
                status.UnrecordedChanges[0].Kind,
                Is.EqualTo(FolderProjectUnrecordedChangeKind.Modified));
        });
    }

    [Test]
    public void GetStatus_ReportsAddedDeletedAndRenamedWithoutIndexConcepts()
    {
        using var project = new TemporaryProject();
        var deletedPath = Path.Combine(project.Root, "db", "deleted.bin");
        var renamedPath = Path.Combine(project.Root, "db", "before.bin");
        File.WriteAllBytes(deletedPath, [1]);
        File.WriteAllBytes(renamedPath, [2]);
        var service = CreateService();
        service.Initialize(project.Root);

        File.Delete(deletedPath);
        File.Move(
            renamedPath,
            Path.Combine(project.Root, "db", "after.bin"));
        File.WriteAllBytes(
            Path.Combine(project.Root, "db", "added.bin"),
            [3]);
        using (var repository = new Repository(project.Root))
            Commands.Stage(repository, "*");

        var changes = service.GetStatus(project.Root).UnrecordedChanges;

        Assert.Multiple(() =>
        {
            Assert.That(
                changes,
                Has.Some.Matches<FolderProjectUnrecordedChange>(change =>
                    change.Path == "db/added.bin" &&
                    change.Kind.HasFlag(
                        FolderProjectUnrecordedChangeKind.Added)));
            Assert.That(
                changes,
                Has.Some.Matches<FolderProjectUnrecordedChange>(change =>
                    change.Path == "db/deleted.bin" &&
                    change.Kind.HasFlag(
                        FolderProjectUnrecordedChangeKind.Deleted)));
            Assert.That(
                changes,
                Has.Some.Matches<FolderProjectUnrecordedChange>(change =>
                    change.Path == "db/after.bin" &&
                    change.PreviousPath == "db/before.bin" &&
                    change.Kind.HasFlag(
                        FolderProjectUnrecordedChangeKind.Renamed)));
        });
    }

    [Test]
    public void GetStatus_RequestsUnreadableScanAndKeepsUnreadableVisible()
    {
        var versionControl = new Mock<IFolderProjectVersionControlService>();
        versionControl.Setup(item => item.GetStatus(
                "project",
                It.IsAny<Action<FolderProjectVersionControlProgress>>(),
                true))
            .Returns(new FolderProjectRepositoryStatus(
                true,
                "master",
                "head",
                false,
                FolderProjectRepositoryOperationState.None,
                [
                    new FolderProjectWorkingChange(
                        "db/unreadable.bin",
                        FolderProjectWorkingChangeKind.Unreadable |
                        FolderProjectWorkingChangeKind.Staged),
                ]));
        var service = new FolderProjectHistoryService(
            versionControl.Object,
            LoadLocalization());

        var status = service.GetStatus("project");

        Assert.That(
            status.UnrecordedChanges.Single().Kind,
            Is.EqualTo(FolderProjectUnrecordedChangeKind.Unreadable));
        versionControl.Verify(item => item.GetStatus(
            "project",
            It.IsAny<Action<FolderProjectVersionControlProgress>>(),
            true), Times.Once);
    }

    [Test]
    public void GetDisplayStatus_SkipsUnreadableScan()
    {
        var versionControl = new Mock<IFolderProjectVersionControlService>();
        versionControl.Setup(item => item.GetStatus(
                "project",
                It.IsAny<Action<FolderProjectVersionControlProgress>>(),
                false))
            .Returns(new FolderProjectRepositoryStatus(
                true,
                "master",
                "head",
                false,
                FolderProjectRepositoryOperationState.None,
                [
                    new FolderProjectWorkingChange(
                        "db/changed.bin",
                        FolderProjectWorkingChangeKind.Modified),
                ]));
        var service = new FolderProjectHistoryService(
            versionControl.Object,
            LoadLocalization());

        var status = service.GetDisplayStatus("project");

        Assert.That(
            status.UnrecordedChanges.Single().Path,
            Is.EqualTo("db/changed.bin"));
        versionControl.Verify(item => item.GetStatus(
            "project",
            It.IsAny<Action<FolderProjectVersionControlProgress>>(),
            false), Times.Once);
    }

    [Test]
    public void GetStatus_ReportsRealProgressAndKeepsUnreadableFilesVisible()
    {
        using var project = new TemporaryProject();
        var expectedPath = Path.Combine(project.Root, "db", "entry.bin");
        File.WriteAllBytes(expectedPath, [1, 2, 3]);
        var platform = new UnreadableFilePlatform();
        var service = new FolderProjectHistoryService(
            new FolderProjectVersionControlService(platform),
            LoadLocalization());
        service.Initialize(project.Root);
        File.WriteAllBytes(expectedPath, [1, 2, 3, 4]);
        platform.UnreadablePath = expectedPath;
        var progress = new List<FolderProjectHistoryProgress>();

        var status = service.GetStatus(project.Root, progress.Add);

        var fileProgress = progress.Where(item =>
                item.Stage == FolderProjectHistoryProgressStage
                    .ScanningUnrecordedChanges &&
                item.Total > 0)
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(fileProgress, Is.Not.Empty);
            Assert.That(fileProgress.Last().Completed,
                Is.EqualTo(fileProgress.Last().Total));
            Assert.That(fileProgress,
                Has.Some.Property("Detail").EqualTo("db/entry.bin"));
            Assert.That(status.UnrecordedChanges,
                Has.Some.Matches<FolderProjectUnrecordedChange>(change =>
                    change.Path == "db/entry.bin" &&
                    change.Kind.HasFlag(
                        FolderProjectUnrecordedChangeKind.Unreadable)));
            Assert.That(
                progress,
                Has.Some.Matches<FolderProjectHistoryProgress>(item =>
                    item.Stage == FolderProjectHistoryProgressStage
                        .ProcessingUnrecordedChanges &&
                    item.Detail == "db/entry.bin" &&
                    item.Completed == item.Total &&
                    item.Total == 1));
        });
    }

    [Test]
    public void GetCommitChanges_ThrottlesProgressAndReportsExactFinalCount()
    {
        using var project = new TemporaryProject();
        var audioDirectory = Path.Combine(project.Root, "audio");
        Directory.CreateDirectory(audioDirectory);
        for (var index = 0; index < 512; index++)
        {
            File.WriteAllBytes(
                Path.Combine(audioDirectory, $"voice-{index:D4}.wem"),
                [(byte)(index % 251)]);
        }
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(
            project.Root,
            new FolderProjectGitIdentity(
                "AssetEditor.CN 本地用户",
                "local@asseteditor.cn"));
        var progress = new List<FolderProjectVersionControlProgress>();

        var changes = service.GetCommitChanges(
            project.Root,
            initial.Id,
            progress.Add);

        var fileProgress = progress.Where(item => item.Stage ==
                FolderProjectVersionControlProgressStage
                    .ProcessingCommitChanges)
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(fileProgress, Is.Not.Empty);
            Assert.That(fileProgress.Count, Is.LessThan(changes.Count));
            Assert.That(fileProgress.Last().Completed, Is.EqualTo(changes.Count));
            Assert.That(
                fileProgress.Last().Total,
                Is.EqualTo(changes.Count));
        });
    }

    [TestCase("plain.txt", false)]
    [TestCase("texture.dds", true)]
    public void GetCommitChanges_ClassifiesWithoutReadingBlobContent(
        string relativePath,
        bool expectedBinary)
    {
        using var project = new TemporaryProject();
        File.WriteAllBytes(
            Path.Combine(project.Root, relativePath),
            [0, 1, 2, 3]);
        var service = new FolderProjectVersionControlService();
        var initial = service.Initialize(
            project.Root,
            new FolderProjectGitIdentity(
                "AssetEditor.CN 本地用户",
                "local@asseteditor.cn"));
        string blobObjectPath;
        using (var repository = new Repository(project.Root))
        {
            var commit = repository.Lookup<Commit>(initial.Id)!;
            var blobId = commit.Tree[relativePath].Target.Id.Sha;
            blobObjectPath = Path.Combine(
                repository.Info.Path,
                "objects",
                blobId[..2],
                blobId[2..]);
        }
        File.SetAttributes(blobObjectPath, FileAttributes.Normal);
        File.Delete(blobObjectPath);

        var change = service.GetCommitChanges(project.Root, initial.Id)
            .Single(item => item.RepositoryPath == relativePath);

        Assert.That(change.IsBinary, Is.EqualTo(expectedBinary));
    }

    [Test]
    public void GetStatus_AfterRestartOpensOnlyAddedAndModifiedFiles()
    {
        using var project = new TemporaryProject();
        var stablePath = Path.Combine(project.Root, "db", "stable.bin");
        var changedPath = Path.Combine(project.Root, "db", "changed.bin");
        var deletedPath = Path.Combine(project.Root, "db", "deleted.bin");
        File.WriteAllBytes(stablePath, [1]);
        File.WriteAllBytes(changedPath, [2]);
        File.WriteAllBytes(deletedPath, [3]);
        CreateService().Initialize(project.Root);
        File.WriteAllBytes(changedPath, [2, 4]);
        var addedPath = Path.Combine(project.Root, "db", "added.bin");
        File.WriteAllBytes(addedPath, [5]);
        File.Delete(deletedPath);
        var platform = new RecordingFileOpenPlatform(project.Root);
        var restartedService = new FolderProjectHistoryService(
            new FolderProjectVersionControlService(platform),
            LoadLocalization());

        var status = restartedService.GetStatus(project.Root);

        Assert.Multiple(() =>
        {
            Assert.That(platform.OpenedPaths,
                Is.EquivalentTo(new[]
                {
                    "db/added.bin",
                    "db/changed.bin",
                }));
            Assert.That(status.UnrecordedChanges.Select(change => change.Path),
                Is.EquivalentTo(new[]
                {
                    "db/added.bin",
                    "db/changed.bin",
                    "db/deleted.bin",
                }));
        });
    }

    [Test]
    public void GetStatus_ClassifiesUnsafeLegacyStatesForRecovery()
    {
        var cases = new[]
        {
            (
                new FolderProjectRepositoryStatus(
                    true,
                    null,
                    "head",
                    true,
                    FolderProjectRepositoryOperationState.None,
                    []),
                FolderProjectHistoryRecoveryReason.DetachedHistory,
                true),
            (
                new FolderProjectRepositoryStatus(
                    true,
                    "main",
                    "head",
                    false,
                    FolderProjectRepositoryOperationState.Merge,
                    [
                        new FolderProjectWorkingChange(
                            "conflict.bin",
                            FolderProjectWorkingChangeKind.Conflicted),
                    ]),
                FolderProjectHistoryRecoveryReason.UnfinishedOperation,
                true),
            (
                new FolderProjectRepositoryStatus(
                    true,
                    "main",
                    "head",
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []) { IsBusy = true },
                FolderProjectHistoryRecoveryReason.RepositoryBusy,
                false),
        };

        foreach (var testCase in cases)
        {
            var versionControl = new Mock<
                IFolderProjectVersionControlService>();
            versionControl.Setup(item => item.GetStatus(
                    "project",
                    It.IsAny<Action<FolderProjectVersionControlProgress>>(),
                    true))
                .Returns(testCase.Item1);
            var service = new FolderProjectHistoryService(
                versionControl.Object,
                LoadLocalization());

            var status = service.GetStatus("project");

            Assert.Multiple(() =>
            {
                Assert.That(
                    status.Availability,
                    Is.EqualTo(
                        FolderProjectHistoryAvailability.RecoveryRequired));
                Assert.That(status.RecoveryReason,
                    Is.EqualTo(testCase.Item2));
                Assert.That(status.CanRecover, Is.EqualTo(testCase.Item3));
            });
        }
    }

    [Test]
    public void RecoverToSafeState_UsesNarrowHistoryContract()
    {
        var versionControl = new Mock<
            IFolderProjectVersionControlService>();
        versionControl.Setup(item => item.RecoverToSafeState("project"))
            .Returns(new FolderProjectRepositoryStatus(
                true,
                "main",
                "head",
                false,
                FolderProjectRepositoryOperationState.None,
                []));
        var progress = new List<FolderProjectHistoryProgress>();
        var service = new FolderProjectHistoryService(
            versionControl.Object,
            LoadLocalization());

        var status = service.RecoverToSafeState("project", progress.Add);

        Assert.Multiple(() =>
        {
            Assert.That(status.Availability,
                Is.EqualTo(FolderProjectHistoryAvailability.Ready));
            Assert.That(progress.Select(item => item.Stage),
                Does.Contain(
                    FolderProjectHistoryProgressStage.RecoveringHistory));
        });
    }

    [Test]
    public void CreateRestorePoint_RecordsAllDiskChangesAndEmptyDirectories()
    {
        using var project = new TemporaryProject();
        var trackedPath = Path.Combine(project.Root, "db", "tracked.bin");
        File.WriteAllBytes(trackedPath, [1]);
        var service = CreateService();
        service.Initialize(project.Root);

        File.WriteAllBytes(trackedPath, [2]);
        using (var repository = new Repository(project.Root))
            Commands.Stage(repository, "db/tracked.bin");
        File.WriteAllBytes(trackedPath, [3]);
        File.WriteAllBytes(
            Path.Combine(project.Root, "db", "added.bin"),
            [4]);
        project.Container.CreateDirectoryOnDisk("empty\\nested");

        var restorePoint = service.CreateRestorePoint(project.Root, "");
        var status = service.GetStatus(project.Root);
        var changes = service.GetRestorePointChanges(
            project.Root,
            restorePoint.Id);
        var reopenedSettings = FolderProjectSettings.Load(project.Root);
        using var committedRepository = new Repository(project.Root);
        var committedBytes = ReadBlobBytes(
            committedRepository.Head.Tip!,
            "db/tracked.bin");

        Assert.Multiple(() =>
        {
            Assert.That(
                restorePoint.Description,
                Is.EqualTo("记录工程当前状态"));
            Assert.That(status.UnrecordedChanges, Is.Empty);
            Assert.That(
                changes.Select(change => change.Path),
                Does.Contain("db/tracked.bin"));
            Assert.That(
                changes.Select(change => change.Path),
                Does.Contain("db/added.bin"));
            Assert.That(
                changes.Select(change => change.Path),
                Does.Contain(FolderProjectSettings.CnFileName));
            Assert.That(
                reopenedSettings.EmptyDirectories,
                Does.Contain("empty\\nested"));
            Assert.That(committedBytes, Is.EqualTo(new byte[] { 3 }));
        });
    }

    [Test]
    public void CreateRestorePoint_ReportsEachIndexedPathWithExactTotal()
    {
        using var project = new TemporaryProject();
        var modifiedPath = Path.Combine(project.Root, "db", "modified.bin");
        File.WriteAllBytes(modifiedPath, [1]);
        var service = CreateService();
        service.Initialize(project.Root);
        File.WriteAllBytes(modifiedPath, [2]);
        File.WriteAllBytes(
            Path.Combine(project.Root, "db", "added.bin"),
            [3]);
        var progress = new List<FolderProjectHistoryProgress>();

        service.CreateRestorePoint(
            project.Root,
            "记录两项修改",
            progress.Add);

        var indexedFiles = progress.Where(item =>
                item.Stage == FolderProjectHistoryProgressStage.UpdatingHistory &&
                item.Total > 0)
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(indexedFiles, Is.Not.Empty);
            Assert.That(indexedFiles.Last().Completed, Is.EqualTo(2));
            Assert.That(indexedFiles.Last().Total, Is.EqualTo(2));
            Assert.That(
                indexedFiles.Select(item => item.Detail),
                Does.Contain("db/modified.bin"));
            Assert.That(
                indexedFiles.Select(item => item.Detail),
                Does.Contain("db/added.bin"));
        });
    }

    [Test]
    public void CreateRestorePoint_StillReportsExactRenames()
    {
        using var project = new TemporaryProject();
        var originalPath = Path.Combine(project.Root, "db", "before.bin");
        var renamedPath = Path.Combine(project.Root, "db", "after.bin");
        File.WriteAllBytes(originalPath, [1, 2, 3]);
        var service = CreateService();
        service.Initialize(project.Root);
        File.Move(originalPath, renamedPath);

        var restorePoint = service.CreateRestorePoint(
            project.Root,
            "重命名资源");
        var change = service.GetRestorePointChanges(
                project.Root,
                restorePoint.Id)
            .Single(item => item.Kind ==
                FolderProjectRestorePointChangeKind.Renamed);

        Assert.Multiple(() =>
        {
            Assert.That(change.Path, Is.EqualTo("db/after.bin"));
            Assert.That(change.PreviousPath, Is.EqualTo("db/before.bin"));
        });
    }

    [Test]
    public void CreateRestorePoint_WithoutDiskChanges_DoesNotAddHistory()
    {
        using var project = new TemporaryProject();
        var service = CreateService();
        service.Initialize(project.Root);
        var originalHistory = service.GetRestorePoints(project.Root);

        Assert.That(
            () => service.CreateRestorePoint(project.Root, "重复记录"),
            Throws.TypeOf<FolderProjectHistoryException>()
                .With.Property(nameof(FolderProjectHistoryException.Code))
                .EqualTo(FolderProjectHistoryError.NoUnrecordedChanges));
        Assert.That(
            service.GetRestorePoints(project.Root).Select(item => item.Id),
            Is.EqualTo(originalHistory.Select(item => item.Id)));
    }

    [Test]
    public void RestoreProject_ReportsEachWrittenPathWithExactProgress()
    {
        using var project = new TemporaryProject();
        var firstPath = Path.Combine(project.Root, "db", "first.bin");
        var secondPath = Path.Combine(project.Root, "db", "second.bin");
        File.WriteAllBytes(firstPath, [1]);
        File.WriteAllBytes(secondPath, [2]);
        var service = CreateService();
        var target = service.Initialize(project.Root);
        File.WriteAllBytes(firstPath, [3]);
        File.WriteAllBytes(secondPath, [4]);
        service.CreateRestorePoint(project.Root, "后续状态");
        var progress = new List<FolderProjectHistoryProgress>();

        service.RestoreProject(project.Root, target, progress.Add);

        var writtenFiles = progress.Where(item =>
                item.Stage == FolderProjectHistoryProgressStage
                    .WritingProjectFiles &&
                item.Total > 0)
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(writtenFiles, Is.Not.Empty);
            Assert.That(
                writtenFiles.Last().Completed,
                Is.EqualTo(writtenFiles.Last().Total));
            Assert.That(
                writtenFiles.Select(item => item.Detail),
                Has.Some.EqualTo("db/first.bin")
                    .Or.EqualTo("db/second.bin"));
        });
    }

    [Test]
    public void GetRestoreImpactCount_IncludesHistoryAndUnrecordedDiskChanges()
    {
        using var project = new TemporaryProject();
        var path = Path.Combine(project.Root, "db", "entry.bin");
        File.WriteAllBytes(path, [1]);
        var service = CreateService();
        var target = service.Initialize(project.Root);
        File.WriteAllBytes(path, [2]);
        service.CreateRestorePoint(project.Root, "后续状态");
        File.WriteAllBytes(
            Path.Combine(project.Root, "db", "unrecorded.bin"),
            [3]);

        var count = service.GetRestoreImpactCount(project.Root, target.Id);

        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public void RestoreProject_DoesNotRewriteUnchangedFiles()
    {
        using var project = new TemporaryProject();
        var unchangedPath = Path.Combine(project.Root, "db", "unchanged.bin");
        var changedPath = Path.Combine(project.Root, "db", "changed.bin");
        File.WriteAllBytes(unchangedPath, [1]);
        File.WriteAllBytes(changedPath, [1]);
        var service = CreateService();
        var target = service.Initialize(project.Root);
        File.WriteAllBytes(changedPath, [2]);
        service.CreateRestorePoint(project.Root, "后续状态");
        var unchangedWriteTime = DateTime.UtcNow.AddYears(-5);
        File.SetLastWriteTimeUtc(unchangedPath, unchangedWriteTime);

        service.RestoreProject(project.Root, target.Id);

        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllBytes(changedPath),
                Is.EqualTo(new byte[] { 1 }));
            Assert.That(
                File.GetLastWriteTimeUtc(unchangedPath),
                Is.EqualTo(unchangedWriteTime));
        });
    }

    [Test]
    public void RestoreProject_PreservesHistoryAndRecordsSafetyAndRestorePoints()
    {
        using var project = new TemporaryProject();
        var modifiedPath = Path.Combine(project.Root, "db", "modified.bin");
        var deletedPath = Path.Combine(project.Root, "db", "deleted.bin");
        var renamedPath = Path.Combine(project.Root, "db", "before.bin");
        File.WriteAllBytes(modifiedPath, [1]);
        File.WriteAllBytes(deletedPath, [2]);
        File.WriteAllBytes(renamedPath, [3]);
        project.Container.CreateDirectoryOnDisk("empty\\original");
        var service = CreateService();
        var target = service.Initialize(project.Root);

        File.WriteAllBytes(modifiedPath, [4]);
        File.Delete(deletedPath);
        File.Move(
            renamedPath,
            Path.Combine(project.Root, "db", "after.bin"));
        File.WriteAllBytes(
            Path.Combine(project.Root, "db", "added.bin"),
            [5]);
        Directory.Delete(Path.Combine(project.Root, "empty", "original"));
        project.Container.EmptyDirectories.Remove("empty\\original");
        project.Container.ProjectSettings.EmptyDirectories.Remove(
            "empty\\original");
        project.Container.SaveSettings();
        project.Container.CreateDirectoryOnDisk("empty\\later");
        var later = service.CreateRestorePoint(project.Root, "后续状态");

        File.WriteAllBytes(modifiedPath, [6]);
        File.WriteAllBytes(
            Path.Combine(project.Root, "db", "unrecorded.bin"),
            [7]);

        var result = service.RestoreProject(project.Root, target.Id);
        var history = service.GetRestorePoints(project.Root);
        var status = service.GetStatus(project.Root);
        var settings = FolderProjectSettings.Load(project.Root);

        Assert.Multiple(() =>
        {
            Assert.That(result.SafetyRestorePoint, Is.Not.Null);
            Assert.That(
                result.SafetyRestorePoint!.Description,
                Does.Contain("恢复前"));
            Assert.That(result.RestorePoint.Description, Does.Contain("初始还原点"));
            Assert.That(File.ReadAllBytes(modifiedPath), Is.EqualTo(new byte[] { 1 }));
            Assert.That(File.ReadAllBytes(deletedPath), Is.EqualTo(new byte[] { 2 }));
            Assert.That(File.ReadAllBytes(renamedPath), Is.EqualTo(new byte[] { 3 }));
            Assert.That(
                File.Exists(Path.Combine(project.Root, "db", "after.bin")),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(project.Root, "db", "added.bin")),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(project.Root, "db", "unrecorded.bin")),
                Is.False);
            Assert.That(settings.EmptyDirectories, Does.Contain("empty\\original"));
            Assert.That(settings.EmptyDirectories, Does.Not.Contain("empty\\later"));
            Assert.That(status.IsClean, Is.True);
            Assert.That(history[0].Id, Is.EqualTo(result.RestorePoint.Id));
            Assert.That(history.Select(item => item.Id), Does.Contain(target.Id));
            Assert.That(history.Select(item => item.Id), Does.Contain(later.Id));
            Assert.That(later.PreviousRestorePointId, Is.EqualTo(target.Id));
            Assert.That(
                history.Select(item => item.Id),
                Does.Contain(result.SafetyRestorePoint.Id));
        });
    }

    [Test]
    public void RestoreProject_WriteFailureRollsBackDiskHistoryAndIndex()
    {
        using var project = new TemporaryProject();
        var path = Path.Combine(project.Root, "db", "entry.bin");
        File.WriteAllBytes(path, [1]);
        var setup = CreateService();
        var target = setup.Initialize(project.Root);
        File.WriteAllBytes(path, [2]);
        setup.CreateRestorePoint(project.Root, "后续状态");
        File.WriteAllBytes(path, [3]);
        using (var repository = new Repository(project.Root))
            Commands.Stage(repository, "db/entry.bin");
        File.WriteAllBytes(path, [4]);
        string originalHead;
        using (var repository = new Repository(project.Root))
            originalHead = repository.Head.Tip!.Sha;
        var originalHistory = setup.GetRestorePoints(project.Root)
            .Select(item => item.Id)
            .ToArray();
        var service = new FolderProjectHistoryService(
            new FolderProjectVersionControlService(
                new FailFirstRestoreResetPlatform()),
            LoadLocalization());

        Assert.That(
            () => service.RestoreProject(project.Root, target.Id),
            Throws.TypeOf<FolderProjectHistoryException>());

        using var restoredRepository = new Repository(project.Root);
        var status = restoredRepository.RetrieveStatus();
        Assert.Multiple(() =>
        {
            Assert.That(restoredRepository.Head.Tip!.Sha, Is.EqualTo(originalHead));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 4 }));
            Assert.That(
                status["db/entry.bin"].State.HasFlag(
                    FileStatus.ModifiedInIndex),
                Is.True);
            Assert.That(
                status["db/entry.bin"].State.HasFlag(
                    FileStatus.ModifiedInWorkdir),
                Is.True);
            Assert.That(
                service.GetRestorePoints(project.Root).Select(item => item.Id),
                Is.EqualTo(originalHistory));
        });
    }

    [Test]
    public void RestoreProject_ExplicitRollbackRestoresDirtyDiskHistoryAndIndex()
    {
        using var project = new TemporaryProject();
        var path = Path.Combine(project.Root, "db", "entry.bin");
        File.WriteAllBytes(path, [1]);
        var service = CreateService();
        var target = service.Initialize(project.Root);
        File.WriteAllBytes(path, [2]);
        service.CreateRestorePoint(project.Root, "后续状态");
        File.WriteAllBytes(path, [3]);
        using (var repository = new Repository(project.Root))
            Commands.Stage(repository, "db/entry.bin");
        File.WriteAllBytes(path, [4]);
        string originalHead;
        using (var repository = new Repository(project.Root))
            originalHead = repository.Head.Tip!.Sha;

        var result = service.RestoreProject(project.Root, target.Id);
        service.RollbackProjectRestore(project.Root, result);

        using var restoredRepository = new Repository(project.Root);
        var status = restoredRepository.RetrieveStatus();
        Assert.Multiple(() =>
        {
            Assert.That(restoredRepository.Head.Tip!.Sha, Is.EqualTo(originalHead));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 4 }));
            Assert.That(status["db/entry.bin"].State.HasFlag(
                FileStatus.ModifiedInIndex), Is.True);
            Assert.That(status["db/entry.bin"].State.HasFlag(
                FileStatus.ModifiedInWorkdir), Is.True);
        });
    }

    [Test]
    public void RestoreFile_FromInitialPointLeavesUnrecordedDiskChange()
    {
        using var project = new TemporaryProject();
        var path = Path.Combine(project.Root, "db", "deleted.bin");
        File.WriteAllBytes(path, [1, 2, 3]);
        var service = CreateService();
        var initial = service.Initialize(project.Root);
        File.Delete(path);
        service.CreateRestorePoint(project.Root, "删除文件");

        var operation = service.BeginRestoreFile(
            project.Root,
            initial.Id,
            "db/deleted.bin");
        service.CompleteRestoreFile(operation);

        var history = service.GetRestorePoints(project.Root);
        var status = service.GetStatus(project.Root);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(history, Has.Count.EqualTo(2));
            Assert.That(
                status.UnrecordedChanges,
                Has.Some.Matches<FolderProjectUnrecordedChange>(change =>
                    change.Path == "db/deleted.bin" &&
                    change.Kind.HasFlag(
                        FolderProjectUnrecordedChangeKind.Added)));
        });
    }

    [Test]
    public void BeginRestoreFile_PreservesIncompleteRollbackSignal()
    {
        var versionControl = new Mock<IFolderProjectVersionControlService>();
        versionControl.Setup(service => service.BeginRestoreFile(
                "project",
                "restore-point",
                "db/file.bin",
                false))
            .Throws(new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryFailure,
                "rollback failed",
                isRollbackIncomplete: true));
        var service = new FolderProjectHistoryService(
            versionControl.Object,
            LoadLocalization());

        var exception = Assert.Throws<FolderProjectHistoryException>(
            () => service.BeginRestoreFile(
                "project",
                "restore-point",
                "db/file.bin"));

        Assert.That(exception!.IsRollbackIncomplete, Is.True);
    }

    [Test]
    public void DiscardChanges_RestoresTrackedAndRemovesSelectedUntrackedFile()
    {
        using var project = new TemporaryProject();
        var trackedPath = Path.Combine(project.Root, "db", "tracked.bin");
        var addedPath = Path.Combine(project.Root, "db", "added.bin");
        var keptPath = Path.Combine(project.Root, "db", "kept.bin");
        File.WriteAllBytes(trackedPath, [1]);
        var service = CreateService();
        service.Initialize(project.Root);
        File.WriteAllBytes(trackedPath, [2]);
        File.WriteAllBytes(addedPath, [3]);
        File.WriteAllBytes(keptPath, [4]);
        var progressStages = new List<FolderProjectHistoryProgressStage>();

        var result = service.BeginDiscardChanges(
            project.Root,
            ["db/tracked.bin", "db/added.bin"],
            progress => progressStages.Add(progress.Stage));
        service.CompleteDiscardChanges(result);

        var status = service.GetStatus(project.Root);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(trackedPath), Is.EqualTo(new byte[] { 1 }));
            Assert.That(File.Exists(addedPath), Is.False);
            Assert.That(File.ReadAllBytes(keptPath), Is.EqualTo(new byte[] { 4 }));
            Assert.That(
                status.UnrecordedChanges.Select(change => change.Path),
                Is.EqualTo(new[] { "db/kept.bin" }));
            Assert.That(
                progressStages,
                Is.EqualTo(new[]
                {
                    FolderProjectHistoryProgressStage.UpdatingHistory,
                    FolderProjectHistoryProgressStage.WritingProjectFiles,
                }));
        });
    }

    [Test]
    public void DiscardChanges_ExplicitRollbackRestoresFilesAndIndex()
    {
        using var project = new TemporaryProject();
        var trackedPath = Path.Combine(project.Root, "db", "tracked.bin");
        var addedPath = Path.Combine(project.Root, "db", "added.bin");
        File.WriteAllBytes(trackedPath, [1]);
        var service = CreateService();
        service.Initialize(project.Root);
        File.WriteAllBytes(trackedPath, [2]);
        using (var repository = new Repository(project.Root))
            Commands.Stage(repository, "db/tracked.bin");
        File.WriteAllBytes(trackedPath, [3]);
        File.WriteAllBytes(addedPath, [4]);

        var result = service.BeginDiscardChanges(
            project.Root,
            ["db/tracked.bin", "db/added.bin"],
            _ => { });
        service.RollbackDiscardChanges(project.Root, result);

        using var restoredRepository = new Repository(project.Root);
        var status = restoredRepository.RetrieveStatus();
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(trackedPath), Is.EqualTo(new byte[] { 3 }));
            Assert.That(File.ReadAllBytes(addedPath), Is.EqualTo(new byte[] { 4 }));
            Assert.That(status["db/tracked.bin"].State.HasFlag(
                FileStatus.ModifiedInIndex), Is.True);
            Assert.That(status["db/tracked.bin"].State.HasFlag(
                FileStatus.ModifiedInWorkdir), Is.True);
        });
    }

    [Test]
    public void GetRestorePoints_IncludesSummaryWithoutReadingDetailedChanges()
    {
        var versionControl = new Mock<IFolderProjectVersionControlService>();
        var commit = new FolderProjectCommitSummary(
            new string('1', 40),
            "调整单位数据",
            "AssetEditor.CN 本地用户",
            "local@asseteditor.cn",
            DateTimeOffset.Parse("2026-08-13T08:00:00+08:00"),
            [new string('0', 40)]);
        versionControl.Setup(item => item.GetHistory("project", 100))
            .Returns([commit]);
        versionControl.Setup(item => item.GetCommitChangeSummary(
                "project",
                commit.Id))
            .Returns(new FolderProjectCommitChangeSummary(
                1,
                2,
                3,
                4,
                5));
        var service = new FolderProjectHistoryService(
            versionControl.Object,
            LoadLocalization());

        var history = service.GetRestorePoints("project");

        Assert.That(
            history.Single().ChangeSummary,
            Is.EqualTo(new FolderProjectRestorePointChangeSummary(
                1,
                2,
                3,
                4,
                5)));
        versionControl.Verify(item => item.GetCommitChanges(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
        versionControl.Verify(item => item.GetCommitChanges(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Action<FolderProjectVersionControlProgress>>()),
            Times.Never);
    }

    [Test]
    public void Restart_ReusesPersistedRestorePointSummaryAndChanges()
    {
        using var project = new TemporaryProject();
        Directory.CreateDirectory(Path.Combine(project.Root, ".git"));
        var commit = new FolderProjectCommitSummary(
            new string('1', 40),
            "调整单位数据",
            "AssetEditor.CN 本地用户",
            "local@asseteditor.cn",
            DateTimeOffset.Parse("2026-08-13T08:00:00+08:00"),
            [new string('0', 40)]);
        var summary = new FolderProjectCommitChangeSummary(1, 0, 0, 0, 0);
        var change = new FolderProjectCommitChange(
            "db/entry.bin",
            null,
            FolderProjectCommitChangeKind.Added,
            true);
        var firstVersionControl = new Mock<IFolderProjectVersionControlService>();
        firstVersionControl.Setup(item => item.GetHistory(project.Root, 100))
            .Returns([commit]);
        firstVersionControl.Setup(item => item.GetCommitChangeSummary(
                project.Root,
                commit.Id))
            .Returns(summary);
        firstVersionControl.Setup(item => item.GetCommitChanges(
                project.Root,
                commit.Id,
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns([change]);
        var firstService = new FolderProjectHistoryService(
            firstVersionControl.Object,
            LoadLocalization());
        firstService.GetRestorePoints(project.Root);
        firstService.GetRestorePointChanges(project.Root, commit.Id);

        var restartedVersionControl =
            new Mock<IFolderProjectVersionControlService>();
        restartedVersionControl.Setup(item => item.GetHistory(project.Root, 100))
            .Returns([commit]);
        restartedVersionControl.Setup(item => item.GetCommitChangeSummary(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new AssertionException(
                "A persisted summary must not be recomputed after restart."));
        restartedVersionControl.Setup(item => item.GetCommitChanges(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Throws(new AssertionException(
                "Persisted changes must not be recomputed after restart."));
        var restartedService = new FolderProjectHistoryService(
            restartedVersionControl.Object,
            LoadLocalization());

        var history = restartedService.GetRestorePoints(project.Root);
        var changes = restartedService.GetRestorePointChanges(
            project.Root,
            commit.Id);

        Assert.Multiple(() =>
        {
            Assert.That(history.Single().ChangeSummary.Total, Is.EqualTo(1));
            Assert.That(changes.Single().Path, Is.EqualTo("db/entry.bin"));
        });
    }

    [Test]
    public void CorruptPersistentCache_RecomputesWithoutChangingHistory()
    {
        using var project = new TemporaryProject();
        Directory.CreateDirectory(Path.Combine(project.Root, ".git"));
        var commit = new FolderProjectCommitSummary(
            new string('1', 40),
            "调整单位数据",
            "AssetEditor.CN 本地用户",
            "local@asseteditor.cn",
            DateTimeOffset.Parse("2026-08-13T08:00:00+08:00"),
            [new string('0', 40)]);
        var change = new FolderProjectCommitChange(
            "db/entry.bin",
            null,
            FolderProjectCommitChangeKind.Modified,
            true);
        var firstVersionControl = new Mock<IFolderProjectVersionControlService>();
        firstVersionControl.Setup(item => item.GetHistory(project.Root, 100))
            .Returns([commit]);
        firstVersionControl.Setup(item => item.GetCommitChangeSummary(
                project.Root,
                commit.Id))
            .Returns(new FolderProjectCommitChangeSummary(0, 1, 0, 0, 0));
        firstVersionControl.Setup(item => item.GetCommitChanges(
                project.Root,
                commit.Id,
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns([change]);
        var firstService = new FolderProjectHistoryService(
            firstVersionControl.Object,
            LoadLocalization());
        firstService.GetRestorePoints(project.Root);
        firstService.GetRestorePointChanges(project.Root, commit.Id);
        foreach (var cachePath in Directory.EnumerateFiles(
                     Path.Combine(project.Root, ".git"),
                     $"{commit.Id}.*.json",
                     SearchOption.AllDirectories))
        {
            File.WriteAllText(cachePath, "not json");
        }

        var recoveringVersionControl =
            new Mock<IFolderProjectVersionControlService>();
        recoveringVersionControl.Setup(item => item.GetHistory(
                project.Root,
                100))
            .Returns([commit]);
        recoveringVersionControl.Setup(item => item.GetCommitChangeSummary(
                project.Root,
                commit.Id))
            .Returns(new FolderProjectCommitChangeSummary(0, 1, 0, 0, 0));
        recoveringVersionControl.Setup(item => item.GetCommitChanges(
                project.Root,
                commit.Id,
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns([change]);
        var recoveringService = new FolderProjectHistoryService(
            recoveringVersionControl.Object,
            LoadLocalization());

        var history = recoveringService.GetRestorePoints(project.Root);
        var changes = recoveringService.GetRestorePointChanges(
            project.Root,
            commit.Id);

        Assert.Multiple(() =>
        {
            Assert.That(history.Single().Id, Is.EqualTo(commit.Id));
            Assert.That(history.Single().ChangeSummary.Modified, Is.EqualTo(1));
            Assert.That(changes.Single().Path, Is.EqualTo("db/entry.bin"));
        });
    }

    [Test]
    public void Initialize_PersistsChangesForFirstSelectionAfterRestart()
    {
        using var project = new TemporaryProject();
        Directory.CreateDirectory(Path.Combine(project.Root, ".git"));
        var commit = new FolderProjectCommitSummary(
            new string('1', 40),
            "Initial folder project commit",
            "AssetEditor.CN 本地用户",
            "local@asseteditor.cn",
            DateTimeOffset.Parse("2026-08-13T08:00:00+08:00"),
            []);
        var change = new FolderProjectCommitChange(
            "db/entry.bin",
            null,
            FolderProjectCommitChangeKind.Added,
            true);
        var firstVersionControl = new Mock<IFolderProjectVersionControlService>();
        firstVersionControl.Setup(item => item.Initialize(
                project.Root,
                It.IsAny<FolderProjectGitIdentity>(),
                "master",
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns(commit);
        firstVersionControl.Setup(item => item.GetCommitChangeSummary(
                project.Root,
                commit.Id))
            .Returns(new FolderProjectCommitChangeSummary(1, 0, 0, 0, 0));
        firstVersionControl.Setup(item => item.GetCommitChanges(
                project.Root,
                commit.Id,
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns([change]);
        var firstService = new FolderProjectHistoryService(
            firstVersionControl.Object,
            LoadLocalization());

        firstService.Initialize(project.Root);

        var restartedVersionControl =
            new Mock<IFolderProjectVersionControlService>();
        restartedVersionControl.Setup(item => item.GetCommitChanges(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Throws(new AssertionException(
                "Initialization changes must already be persisted."));
        var restartedService = new FolderProjectHistoryService(
            restartedVersionControl.Object,
            LoadLocalization());

        var changes = restartedService.GetRestorePointChanges(
            project.Root,
            commit.Id);

        Assert.That(changes.Single().Path, Is.EqualTo("db/entry.bin"));
    }

    [Test]
    public void CreateRestorePoint_PersistsChangesForFirstSelectionAfterRestart()
    {
        using var project = new TemporaryProject();
        Directory.CreateDirectory(Path.Combine(project.Root, ".git"));
        var commit = new FolderProjectCommitSummary(
            new string('1', 40),
            "记录当前状态",
            "AssetEditor.CN 本地用户",
            "local@asseteditor.cn",
            DateTimeOffset.Parse("2026-08-13T08:00:00+08:00"),
            [new string('0', 40)]);
        var change = new FolderProjectCommitChange(
            "db/entry.bin",
            null,
            FolderProjectCommitChangeKind.Modified,
            true);
        var firstVersionControl = new Mock<IFolderProjectVersionControlService>();
        firstVersionControl.Setup(item => item.CommitAll(
                project.Root,
                "记录当前状态",
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns(commit);
        firstVersionControl.Setup(item => item.GetCommitChangeSummary(
                project.Root,
                commit.Id))
            .Returns(new FolderProjectCommitChangeSummary(0, 1, 0, 0, 0));
        firstVersionControl.Setup(item => item.GetCommitChanges(
                project.Root,
                commit.Id,
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns([change]);
        var firstService = new FolderProjectHistoryService(
            firstVersionControl.Object,
            LoadLocalization());

        firstService.CreateRestorePoint(project.Root, "记录当前状态");

        var restartedVersionControl =
            new Mock<IFolderProjectVersionControlService>();
        restartedVersionControl.Setup(item => item.GetCommitChanges(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Throws(new AssertionException(
                "New restore-point changes must already be persisted."));
        var restartedService = new FolderProjectHistoryService(
            restartedVersionControl.Object,
            LoadLocalization());

        var changes = restartedService.GetRestorePointChanges(
            project.Root,
            commit.Id);

        Assert.That(changes.Single().Path, Is.EqualTo("db/entry.bin"));
    }

    [Test]
    public void Initialize_IncludesSummaryAndPersistsDetailedChanges()
    {
        var versionControl = new Mock<IFolderProjectVersionControlService>();
        var commit = new FolderProjectCommitSummary(
            new string('1', 40),
            "Initial folder project commit",
            "AssetEditor.CN 本地用户",
            "local@asseteditor.cn",
            DateTimeOffset.Parse("2026-08-13T08:00:00+08:00"),
            []);
        versionControl.Setup(item => item.Initialize(
                "project",
                It.IsAny<FolderProjectGitIdentity>(),
                "master",
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns(commit);
        versionControl.Setup(item => item.GetCommitChanges(
                "project",
                commit.Id,
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns(
            [
                new FolderProjectCommitChange(
                    "db/entry.bin",
                    null,
                    FolderProjectCommitChangeKind.Added,
                    true),
            ]);
        var service = new FolderProjectHistoryService(
            versionControl.Object,
            LoadLocalization());

        var restorePoint = service.Initialize("project");

        Assert.That(
            restorePoint.ChangeSummary,
            Is.EqualTo(new FolderProjectRestorePointChangeSummary(
                1,
                0,
                0,
                0,
                0)));
        versionControl.Verify(item => item.GetCommitChanges(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
        versionControl.Verify(item => item.GetCommitChanges(
                "project",
                commit.Id,
                It.IsAny<Action<FolderProjectVersionControlProgress>>()),
            Times.Once);
    }

    [Test]
    public void CreateRestorePoint_IncludesSummaryAndPersistsDetailedChanges()
    {
        var versionControl = new Mock<IFolderProjectVersionControlService>();
        var commit = new FolderProjectCommitSummary(
            new string('1', 40),
            "记录当前状态",
            "AssetEditor.CN 本地用户",
            "local@asseteditor.cn",
            DateTimeOffset.Parse("2026-08-13T08:00:00+08:00"),
            [new string('0', 40)]);
        versionControl.Setup(item => item.CommitAll(
                "project",
                "记录当前状态",
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns(commit);
        versionControl.Setup(item => item.GetCommitChanges(
                "project",
                commit.Id,
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns(
            [
                new FolderProjectCommitChange(
                    "db/entry.bin",
                    null,
                    FolderProjectCommitChangeKind.Modified,
                    true),
            ]);
        var service = new FolderProjectHistoryService(
            versionControl.Object,
            LoadLocalization());

        var restorePoint = service.CreateRestorePoint(
            "project",
            "记录当前状态");

        Assert.That(
            restorePoint.ChangeSummary,
            Is.EqualTo(new FolderProjectRestorePointChangeSummary(
                0,
                1,
                0,
                0,
                0)));
        versionControl.Verify(item => item.GetCommitChanges(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
        versionControl.Verify(item => item.GetCommitChanges(
                "project",
                commit.Id,
                It.IsAny<Action<FolderProjectVersionControlProgress>>()),
            Times.Once);
    }

    private sealed class TemporaryProject : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"ae-project-history-{Guid.NewGuid():N}");

        public FolderProjectContainer Container { get; }

        public TemporaryProject()
        {
            Directory.CreateDirectory(Root);
            Container = FolderProjectContainer.Create(
                Root,
                new FolderProjectSettings { Name = "工程历史测试" });
            Directory.CreateDirectory(Path.Combine(Root, "db"));
        }

        public void Dispose()
        {
            Container.Dispose();
            if (!Directory.Exists(Root))
                return;

            foreach (var file in Directory.EnumerateFiles(
                         Root,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Root, true);
        }
    }

    private static FolderProjectHistoryService CreateService() =>
        new(LoadLocalization());

    private static LocalizationManager LoadLocalization()
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        return localization;
    }

    private static byte[] ReadBlobBytes(Commit commit, string path)
    {
        var blob = (Blob)commit[path].Target;
        using var content = blob.GetContentStream();
        using var memory = new MemoryStream();
        content.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class FailFirstRestoreResetPlatform :
        FolderProjectVersionControlPlatform
    {
        private bool _failed;

        public override void Reset(
            Repository repository,
            Commit commit,
            CheckoutOptions options)
        {
            if (!_failed)
            {
                _failed = true;
                throw new IOException("Injected restore write failure.");
            }

            base.Reset(repository, commit, options);
        }
    }

    private sealed class UnreadableFilePlatform :
        FolderProjectVersionControlPlatform
    {
        public string? UnreadablePath { get; set; }

        public override Stream OpenReadForStatus(string path)
        {
            if (string.Equals(
                    Path.GetFullPath(path),
                    UnreadablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "Injected unreadable project file.");
            }
            return base.OpenReadForStatus(path);
        }
    }

    private sealed class RecordingFileOpenPlatform(string projectRoot) :
        FolderProjectVersionControlPlatform
    {
        public List<string> OpenedPaths { get; } = [];

        public override Stream OpenReadForStatus(string path)
        {
            OpenedPaths.Add(Path.GetRelativePath(projectRoot, path)
                .Replace('\\', '/'));
            return base.OpenReadForStatus(path);
        }
    }
}
