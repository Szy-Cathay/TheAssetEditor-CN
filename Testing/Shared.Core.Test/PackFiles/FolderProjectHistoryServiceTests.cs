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
                    "CreateRestorePoint",
                    "GetRestorePointChanges",
                    "GetRestorePoints",
                    "GetStatus",
                    "Initialize",
                }));
            Assert.That(publicContract, Does.Not.Contain("Identity"));
            Assert.That(publicContract, Does.Not.Contain("Branch"));
            Assert.That(publicContract, Does.Not.Contain("Stage"));
            Assert.That(publicContract, Does.Not.Contain("Stash"));
            Assert.That(publicContract, Does.Not.Contain("Merge"));
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
}
