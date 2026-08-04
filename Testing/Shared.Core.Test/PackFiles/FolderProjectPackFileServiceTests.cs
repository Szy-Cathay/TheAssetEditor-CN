using Moq;
using Shared.ByteParsing;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Test.Shared.Core.PackFiles;

public class FolderProjectPackFileServiceTests
{
    private static readonly (string Path, string Content)[]
        s_corruptionDetectionFiles =
        [
            (
                @"!!!packfile_corruction_detection\packfile_corruction_detection_1.txt",
                "This file is here to validate that the packfile has not been corrupted while saving. This is check 1"),
            (
                @"packfile_corruction_detection\packfile_corruction_detection_2.txt",
                "This file is here to validate that the packfile has not been corrupted while saving. This is check 2"),
            (
                @"zzzz_packfile_corruction_detection\packfile_corruction_detection_3.txt",
                "This file is here to validate that the packfile has not been corrupted while saving. This is check 3"),
        ];

    [SetUp]
    public void SetUp()
    {
        new LocalizationManager().LoadLanguage();
    }

    [Test]
    public void AddContainer_BeforeCaPacks_AcceptsFolderProject()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var service = new PackFileService(null)
        {
            MessageBoxProvider = Mock.Of<ISimpleMessageBox>(),
        };

        var result = service.AddContainer(container, true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(container));
            Assert.That(service.GetEditablePack(), Is.SameAs(container));
            Assert.That(container.IsWatching, Is.True);
        });
    }

    [Test]
    public void Mutations_WriteThroughToProjectDirectory()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var service = CreateService();
        service.AddContainer(container, true);

        service.AddFilesToPack(
            container,
            [
                new NewPackFileEntry(
                    "db",
                    PackFile.CreateFromBytes("item.bin", [1, 2])),
            ]);
        var file = container.FileList[@"db\item.bin"];
        service.SaveFile(file, [3, 4]);
        service.MoveFile(container, file, @"moved");
        service.RenameFile(container, file, "renamed.bin");
        service.RenameDirectory(container, "moved", "final");

        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllBytes(
                    Path.Combine(
                        project.Path,
                        "final",
                        "renamed.bin")),
                Is.EqualTo(new byte[] { 3, 4 }));
            Assert.That(
                container.FileList.Keys,
                Is.EquivalentTo(
                    new[] { @"final\renamed.bin" }));
        });

        service.DeleteFile(container, file);
        Assert.That(
            File.Exists(
                Path.Combine(
                    project.Path,
                    "final",
                    "renamed.bin")),
            Is.False);
    }

    [Test]
    public void ApplyFileWrites_FolderProjectPublishesSinglePathAwareChangeSet()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();

        service.ApplyFileWrites(
            container,
            [
                new PackFileWrite(@"audio\first.wem", [1]),
                new PackFileWrite(@"audio\second.wem", [2]),
            ]);

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Select(
                    change => change.Path),
                Is.EqualTo(
                    new[]
                    {
                        @"audio\first.wem",
                        @"audio\second.wem",
                    }));
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Select(
                    change => change.Kind),
                Is.All.EqualTo(FolderProjectFileChangeKind.Added));
            Assert.That(
                CountPublished<PackFileContainerFilesAddedEvent>(eventHub),
                Is.Zero);
            Assert.That(
                CountPublished<PackFileContainerFilesUpdatedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void ApplyFileWrites_DuringDiskCommit_DoesNotBlockReadOnlyStateQueries()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        using var commitStarted = new ManualResetEventSlim(false);
        using var releaseCommit = new ManualResetEventSlim(false);
        container.CommitPreparedFile = (source, destination) =>
        {
            commitStarted.Set();
            if (!releaseCommit.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the commit.");
            File.Move(source, destination);
        };

        var writeTask = Task.Run(
            () => container.ApplyFileWrites(
            [
                new PackFileWrite(@"audio\item.wem", [1]),
            ]));
        try
        {
            Assert.That(
                commitStarted.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "The batch did not reach its disk commit phase.");

            var readTask = Task.Run(
                () => container.IsIgnored(@"audio\item.wem"));
            Assert.That(
                readTask.Wait(TimeSpan.FromSeconds(1)),
                Is.True,
                "A read-only query waited for the batch disk commit.");
            Assert.That(readTask.Result, Is.False);
        }
        finally
        {
            releaseCommit.Set();
        }

        Assert.That(
            writeTask.Wait(TimeSpan.FromSeconds(5)),
            Is.True,
            "The batch did not finish after the commit was released.");
        writeTask.GetAwaiter().GetResult();
    }

    [Test]
    public void AddFiles_DuringSourceRead_DoesNotBlockReadOnlyStateQueries()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        using var source = new BlockingDataSource([1]);
        var file = PackFile.CreateFromBytes("item.wem", [1]);
        file.DataSource = source;

        var writeTask = Task.Run(
            () => container.AddFiles(
            [
                new NewPackFileEntry("audio", file),
            ],
            overwriteExisting: true));
        try
        {
            Assert.That(
                source.ReadStarted.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "The add operation did not start reading its source.");

            var readTask = Task.Run(
                () => container.IsIgnored(@"audio\item.wem"));
            Assert.That(
                readTask.Wait(TimeSpan.FromSeconds(1)),
                Is.True,
                "A read-only query waited for the source read.");
            Assert.That(readTask.Result, Is.False);
        }
        finally
        {
            source.ReleaseRead.Set();
        }

        Assert.That(
            writeTask.Wait(TimeSpan.FromSeconds(5)),
            Is.True,
            "The add operation did not finish after the source was released.");
        writeTask.GetAwaiter().GetResult();
    }

    [Test]
    public void ApplyFileWrites_WhenCommitFails_RestoresEntireBatch()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"audio\first.wem", [9]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var original = container.FileList[@"audio\first.wem"];
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();
        container.CommitPreparedFile = (source, destination) =>
        {
            if (destination.EndsWith(
                    "second.wem",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("commit failed");
            }
            File.Move(source, destination);
        };

        Assert.That(
            () => service.ApplyFileWrites(
                container,
                [
                    new PackFileWrite(@"audio\first.wem", [1]),
                    new PackFileWrite(@"audio\second.wem", [2]),
                ]),
            Throws.TypeOf<IOException>());

        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllBytes(Path.Combine(
                    project.Path,
                    "audio",
                    "first.wem")),
                Is.EqualTo(new byte[] { 9 }));
            Assert.That(
                File.Exists(Path.Combine(
                    project.Path,
                    "audio",
                    "second.wem")),
                Is.False);
            Assert.That(
                container.FileList[@"audio\first.wem"],
                Is.SameAs(original));
            Assert.That(container.FileList.ContainsKey(
                @"audio\second.wem"), Is.False);
            Assert.That(
                CountPublished<FolderProjectChangedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void ApplyFileWrites_NewFilesCommitConcurrently()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        using var commitsStarted = new CountdownEvent(2);
        container.CommitPreparedFile = (source, destination) =>
        {
            commitsStarted.Signal();
            if (!commitsStarted.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException(
                    "The batch committed new files sequentially.");
            }
            File.Move(source, destination);
        };

        Assert.That(
            () => container.ApplyFileWrites(
            [
                new PackFileWrite(@"audio\first.wem", [1]),
                new PackFileWrite(@"audio\second.wem", [2]),
            ]),
            Throws.Nothing);
        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllBytes(Path.Combine(
                    project.Path,
                    "audio",
                    "first.wem")),
                Is.EqualTo(new byte[] { 1 }));
            Assert.That(
                File.ReadAllBytes(Path.Combine(
                    project.Path,
                    "audio",
                    "second.wem")),
                Is.EqualTo(new byte[] { 2 }));
        });
    }

    [Test]
    public void AddFilesToPack_WhenCommitFails_RestoresEntireBatch()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"audio\first.wem", [9]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var original = container.FileList[@"audio\first.wem"];
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();
        container.CommitPreparedFile = (source, destination) =>
        {
            if (destination.EndsWith(
                    "second.wem",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("commit failed");
            }
            File.Move(source, destination);
        };

        Assert.That(
            () => service.AddFilesToPack(
                container,
                [
                    new NewPackFileEntry(
                        "audio",
                        PackFile.CreateFromBytes("first.wem", [1])),
                    new NewPackFileEntry(
                        "audio",
                        PackFile.CreateFromBytes("second.wem", [2])),
                ],
                overwriteExisting: true),
            Throws.TypeOf<IOException>());

        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllBytes(Path.Combine(
                    project.Path,
                    "audio",
                    "first.wem")),
                Is.EqualTo(new byte[] { 9 }));
            Assert.That(
                File.Exists(Path.Combine(
                    project.Path,
                    "audio",
                    "second.wem")),
                Is.False);
            Assert.That(
                container.FileList[@"audio\first.wem"],
                Is.SameAs(original));
            Assert.That(
                CountPublished<FolderProjectChangedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void AddFilesToPack_FolderProjectPublishesPathAwareChangeSet()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();

        service.AddFilesToPack(
            container,
            [
                new NewPackFileEntry(
                    "db",
                    PackFile.CreateFromBytes("first.bin", [1])),
                new NewPackFileEntry(
                    "db",
                    PackFile.CreateFromBytes("second.bin", [2])),
            ]);

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Select(
                    change => change.Path),
                Is.EqualTo(
                    new[]
                    {
                        @"db\first.bin",
                        @"db\second.bin",
                    }));
            Assert.That(
                CountPublished<PackFileContainerFilesAddedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void SaveFile_FolderProjectPublishesPathAwareChangeSet()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();

        service.SaveFile(container.FileList[@"db\item.bin"], [2]);

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Single().Path,
                Is.EqualTo(@"db\item.bin"));
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Single().Kind,
                Is.EqualTo(FolderProjectFileChangeKind.Updated));
            Assert.That(
                CountPublished<PackFileContainerFilesUpdatedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void MoveFile_FolderProjectPublishesSingleMovedChange()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();
        var file = container.FileList[@"db\item.bin"];

        service.MoveFile(container, file, "moved");

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        var change = changeEvent.ChangeSet.FileChanges.Single();
        Assert.Multiple(() =>
        {
            Assert.That(change.Kind, Is.EqualTo(
                FolderProjectFileChangeKind.Moved));
            Assert.That(change.PreviousPath, Is.EqualTo(@"db\item.bin"));
            Assert.That(change.Path, Is.EqualTo(@"moved\item.bin"));
            Assert.That(
                CountPublished<PackFileContainerFilesRemovedEvent>(eventHub),
                Is.Zero);
            Assert.That(
                CountPublished<PackFileContainerFilesAddedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void DeleteFile_FolderProjectPublishesSingleRemovedChange()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();
        var file = container.FileList[@"db\item.bin"];

        service.DeleteFile(container, file);

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        var change = changeEvent.ChangeSet.FileChanges.Single();
        Assert.Multiple(() =>
        {
            Assert.That(change.Kind, Is.EqualTo(
                FolderProjectFileChangeKind.Removed));
            Assert.That(change.Path, Is.EqualTo(@"db\item.bin"));
            Assert.That(change.File, Is.SameAs(file));
            Assert.That(
                CountPublished<PackFileContainerFilesRemovedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void RenameFile_FolderProjectPublishesSingleMovedChange()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\item.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();
        var file = container.FileList[@"db\item.bin"];

        service.RenameFile(container, file, "renamed.bin");

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        var change = changeEvent.ChangeSet.FileChanges.Single();
        Assert.Multiple(() =>
        {
            Assert.That(change.Kind, Is.EqualTo(
                FolderProjectFileChangeKind.Moved));
            Assert.That(change.PreviousPath, Is.EqualTo(@"db\item.bin"));
            Assert.That(change.Path, Is.EqualTo(@"db\renamed.bin"));
            Assert.That(
                CountPublished<PackFileContainerFilesUpdatedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void RenameDirectory_FolderProjectPublishesSinglePathAwareChangeSet()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"old\nested\first.bin", [1]);
        project.Write(@"old\second.bin", [2]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();

        service.RenameDirectory(container, "old", "renamed");

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Select(change => change.Path),
                Is.EquivalentTo(new[]
                {
                    @"renamed\nested\first.bin",
                    @"renamed\second.bin",
                }));
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Select(
                    change => change.PreviousPath),
                Is.EquivalentTo(new[]
                {
                    @"old\nested\first.bin",
                    @"old\second.bin",
                }));
            Assert.That(
                changeEvent.ChangeSet.DirectoryChanges,
                Is.EqualTo(new[]
                {
                    new FolderProjectDirectoryChange(
                        "renamed",
                        FolderProjectDirectoryChangeKind.Moved,
                        "old"),
                }));
            Assert.That(
                CountPublished<PackFileContainerFolderRenamedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void AddFiles_PathTraversal_ThrowsWithoutWritingOutsideProject()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var service = CreateService();
        service.AddContainer(container, true);
        var outside = Path.Combine(
            Path.GetDirectoryName(project.Path)!,
            "outside.bin");

        Assert.That(
            () => service.AddFilesToPack(
                container,
                [
                    new NewPackFileEntry(
                        "..",
                        PackFile.CreateFromBytes(
                            "outside.bin",
                            [9])),
                ]),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(File.Exists(outside), Is.False);
    }

    [Test]
    public void DeleteFolder_DeletesOnlyRequestedFolder()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"delete\a.bin", [1]);
        project.Write(@"delete-more\b.bin", [2]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var service = CreateService();
        service.AddContainer(container, true);

        service.DeleteFolder(container, "delete");

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(
                    Path.Combine(project.Path, "delete")),
                Is.False);
            Assert.That(
                File.Exists(
                    Path.Combine(
                        project.Path,
                        "delete-more",
                        "b.bin")),
                Is.True);
        });
    }

    [Test]
    public void DeleteFolder_FolderProjectPublishesSinglePathAwareChangeSet()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"delete\nested\first.bin", [1]);
        project.Write(@"delete\second.bin", [2]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();

        service.DeleteFolder(container, "delete");

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Select(change => change.Path),
                Is.EquivalentTo(new[]
                {
                    @"delete\nested\first.bin",
                    @"delete\second.bin",
                }));
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Select(change => change.Kind),
                Is.All.EqualTo(FolderProjectFileChangeKind.Removed));
            Assert.That(
                changeEvent.ChangeSet.DirectoryChanges,
                Is.EqualTo(new[]
                {
                    new FolderProjectDirectoryChange(
                        "delete",
                        FolderProjectDirectoryChangeKind.Removed),
                }));
            Assert.That(
                CountPublished<PackFileContainerFolderRemovedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void ExternalBatchDelete_PublishesOneFolderProjectChangeSet()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"wwise\nested\first.wem", [1]);
        project.Write(@"wwise\second.wem", [2]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();
        Directory.Delete(Path.Combine(project.Path, "wwise"), true);

        container.ReconcileAfterWatcherEvent();

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Select(change => change.Path),
                Is.EquivalentTo(new[]
                {
                    @"wwise\nested\first.wem",
                    @"wwise\second.wem",
                }));
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Select(change => change.Kind),
                Is.All.EqualTo(FolderProjectFileChangeKind.Removed));
            Assert.That(
                CountPublished<PackFileContainerFilesRemovedEvent>(eventHub),
                Is.Zero);
            Assert.That(
                CountPublished<PackFileContainerFilesAddedEvent>(eventHub),
                Is.Zero);
            Assert.That(
                CountPublished<PackFileContainerFilesUpdatedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void AddContainer_RejectedDuplicate_DoesNotStartSecondWatcher()
    {
        using var project = new TemporaryDirectory();
        using var first = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "一" });
        using var second = FolderProjectContainer.Open(project.Path);
        var service = CreateService();

        Assert.That(service.AddContainer(first, true), Is.SameAs(first));
        Assert.That(service.AddContainer(second, true), Is.Null);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsWatching, Is.True);
            Assert.That(second.IsWatching, Is.False);
            Assert.That(service.GetEditablePack(), Is.SameAs(first));
        });
    }

    [Test]
    public void UnloadPackContainer_AfterAllowedClose_DisposesFolderProject()
    {
        using var project = new TemporaryDirectory();
        var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var service = CreateService();
        service.AddContainer(container, true);

        service.UnloadPackContainer(container);

        Assert.That(
            () => container.RefreshFromDisk(),
            Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void AddContainer_WhenAddedEventThrows_RollsBackFolderProject()
    {
        using var project = new TemporaryDirectory();
        var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var expected = new InvalidOperationException("added event failed");
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<PackFileContainerAddedEvent>()))
            .Throws(expected);
        var service = CreateService(eventHub.Object);

        var actual = Assert.Throws<InvalidOperationException>(
            () => service.AddContainer(
                container,
                0,
                true,
                PackFileContainerAddedReason.InternalReattach));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(service.GetAllPackfileContainers(), Is.Empty);
            Assert.That(service.GetEditablePack(), Is.Null);
            Assert.That(container.IsWatching, Is.False);
            Assert.That(
                () => container.RefreshFromDisk(),
                Throws.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public void AddContainer_WhenEditableEventThrows_RollsBackFolderProject()
    {
        using var project = new TemporaryDirectory();
        var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var expected = new InvalidOperationException("editable event failed");
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<
                    PackFileContainerSetAsMainEditableEvent>()))
            .Throws(expected);
        var service = CreateService(eventHub.Object);

        var actual = Assert.Throws<InvalidOperationException>(
            () => service.AddContainer(
                container,
                0,
                true,
                PackFileContainerAddedReason.InternalReattach));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(service.GetAllPackfileContainers(), Is.Empty);
            Assert.That(service.GetEditablePack(), Is.Null);
            Assert.That(container.IsWatching, Is.False);
            Assert.That(
                () => container.RefreshFromDisk(),
                Throws.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public void UnloadPackContainer_WhenCloseVetoed_KeepsProjectActive()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<BeforePackFileContainerRemovedEvent>()))
            .Callback<BeforePackFileContainerRemovedEvent>(
                e => e.AllowClose = false);
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);

        var result = service.TryUnloadPackContainer(container);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(
                service.GetAllPackfileContainers(),
                Does.Contain(container));
            Assert.That(service.GetEditablePack(), Is.SameAs(container));
            Assert.That(container.IsWatching, Is.True);
            Assert.That(
                container.RefreshFromDisk(),
                Is.EqualTo(
                    new FolderProjectReconciliation(0, 0, 0)));
            Assert.That(
                CountPublished<BeforePackFileContainerRemovedEvent>(
                    eventHub),
                Is.EqualTo(1));
            Assert.That(
                CountPublished<PackFileContainerRemovedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void TryUnloadPackContainer_NotLoaded_ReturnsFalseWithoutEventsOrStateChanges()
    {
        using var loadedProject = new TemporaryDirectory();
        using var otherProject = new TemporaryDirectory();
        using var loaded = FolderProjectContainer.Create(
            loadedProject.Path,
            new FolderProjectSettings { Name = "已加载" });
        using var notLoaded = FolderProjectContainer.Create(
            otherProject.Path,
            new FolderProjectSettings { Name = "未加载" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(loaded, true);
        eventHub.Invocations.Clear();

        var result = service.TryUnloadPackContainer(notLoaded);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(
                service.GetAllPackfileContainers(),
                Is.EqualTo(new[] { loaded }));
            Assert.That(service.GetEditablePack(), Is.SameAs(loaded));
            Assert.That(loaded.IsWatching, Is.True);
            Assert.That(notLoaded.IsWatching, Is.False);
            Assert.That(
                CountPublished<BeforePackFileContainerRemovedEvent>(
                    eventHub),
                Is.Zero);
            Assert.That(
                CountPublished<PackFileContainerRemovedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void TryUnloadPackContainer_Success_ReturnsTrueAndFinalizesExactlyOnce()
    {
        using var project = new TemporaryDirectory();
        var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var first = new PackFileContainer("first")
        {
            SystemFilePath = "first.pack",
        };
        var last = new PackFileContainer("last")
        {
            SystemFilePath = "last.pack",
        };
        var eventHub = new Mock<IGlobalEventHub>();
        var approvedCloseCount = 0;
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<BeforePackFileContainerRemovedEvent>()))
            .Callback<BeforePackFileContainerRemovedEvent>(
                e => e.SetApprovedCloseAction(
                    () =>
                    {
                        approvedCloseCount++;
                        return true;
                    }));
        var service = CreateService(eventHub.Object);
        service.AddContainer(first);
        service.AddContainer(container, true);
        service.AddContainer(last);
        eventHub.Invocations.Clear();

        var firstResult = service.TryUnloadPackContainer(container);
        var secondResult = service.TryUnloadPackContainer(container);

        Assert.Multiple(() =>
        {
            Assert.That(firstResult, Is.True);
            Assert.That(secondResult, Is.False);
            Assert.That(
                service.GetAllPackfileContainers(),
                Is.EqualTo(new[] { first, last }));
            Assert.That(service.GetEditablePack(), Is.Null);
            Assert.That(container.IsWatching, Is.False);
            Assert.That(
                () => container.RefreshFromDisk(),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                CountPublished<BeforePackFileContainerRemovedEvent>(
                    eventHub),
                Is.EqualTo(1));
            Assert.That(
                CountPublished<PackFileContainerRemovedEvent>(eventHub),
                Is.EqualTo(1));
            Assert.That(
                CountPublished<PackFileContainerSetAsMainEditableEvent>(
                    eventHub),
                Is.EqualTo(1));
            Assert.That(approvedCloseCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TryUnloadPackContainer_NonEditableContainer_DoesNotPublishEditableChange()
    {
        using var project = new TemporaryDirectory();
        var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container);
        eventHub.Invocations.Clear();

        var result = service.TryUnloadPackContainer(container);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(service.GetEditablePack(), Is.Null);
            Assert.That(
                CountPublished<PackFileContainerSetAsMainEditableEvent>(
                    eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void TryUnloadPackContainer_ApprovedCloseActionRejects_KeepsContainerLoaded()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var approvedCloseCount = 0;
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<BeforePackFileContainerRemovedEvent>()))
            .Callback<BeforePackFileContainerRemovedEvent>(
                e => e.SetApprovedCloseAction(
                    () =>
                    {
                        approvedCloseCount++;
                        return false;
                    }));
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();

        var result = service.TryUnloadPackContainer(container);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(approvedCloseCount, Is.EqualTo(1));
            Assert.That(
                service.GetAllPackfileContainers(),
                Is.EqualTo(new[] { container }));
            Assert.That(service.GetEditablePack(), Is.SameAs(container));
            Assert.That(container.IsWatching, Is.True);
            Assert.That(
                CountPublished<PackFileContainerRemovedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void TryUnloadPackContainer_LaterVeto_DoesNotRunApprovedCloseAction()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var approvedCloseCount = 0;
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<BeforePackFileContainerRemovedEvent>()))
            .Callback<BeforePackFileContainerRemovedEvent>(
                e =>
                {
                    e.SetApprovedCloseAction(
                        () =>
                        {
                            approvedCloseCount++;
                            return true;
                        });
                    e.AllowClose = false;
                    e.AllowClose = true;
                });
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();

        var result = service.TryUnloadPackContainer(container);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(approvedCloseCount, Is.Zero);
            Assert.That(
                service.GetAllPackfileContainers(),
                Is.EqualTo(new[] { container }));
            Assert.That(service.GetEditablePack(), Is.SameAs(container));
            Assert.That(container.IsWatching, Is.True);
            Assert.That(
                CountPublished<PackFileContainerRemovedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void TryUnloadPackContainer_BeforeEventThrows_LeavesContainerFullyLoaded()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<BeforePackFileContainerRemovedEvent>()))
            .Throws(new InvalidOperationException("before remove"));
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();

        Assert.That(
            () => service.TryUnloadPackContainer(container),
            Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(
                service.GetAllPackfileContainers(),
                Is.EqualTo(new[] { container }));
            Assert.That(service.GetEditablePack(), Is.SameAs(container));
            Assert.That(container.IsWatching, Is.True);
            Assert.That(
                container.RefreshFromDisk(),
                Is.EqualTo(
                    new FolderProjectReconciliation(0, 0, 0)));
            Assert.That(
                CountPublished<PackFileContainerRemovedEvent>(eventHub),
                Is.Zero);
            Assert.That(
                CountPublished<PackFileContainerSetAsMainEditableEvent>(
                    eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void TryUnloadPackContainer_RemovedEventThrows_StillFinishesUnload()
    {
        using var project = new TemporaryDirectory();
        var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<PackFileContainerRemovedEvent>()))
            .Throws(new InvalidOperationException("removed"));
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();

        Assert.That(
            () => service.TryUnloadPackContainer(container),
            Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(service.GetAllPackfileContainers(), Is.Empty);
            Assert.That(service.GetEditablePack(), Is.Null);
            Assert.That(container.IsWatching, Is.False);
            Assert.That(
                () => container.RefreshFromDisk(),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                CountPublished<PackFileContainerSetAsMainEditableEvent>(
                    eventHub),
                Is.EqualTo(1));
            Assert.That(
                CountPublished<PackFileContainerRemovedEvent>(eventHub),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void TryUnloadPackContainer_EditableEventThrows_StillPublishesRemovedAndDisposes()
    {
        using var project = new TemporaryDirectory();
        var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<PackFileContainerSetAsMainEditableEvent>()))
            .Throws(new InvalidOperationException("editable"));

        Assert.That(
            () => service.TryUnloadPackContainer(container),
            Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(service.GetAllPackfileContainers(), Is.Empty);
            Assert.That(service.GetEditablePack(), Is.Null);
            Assert.That(container.IsWatching, Is.False);
            Assert.That(
                () => container.RefreshFromDisk(),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                CountPublished<PackFileContainerSetAsMainEditableEvent>(
                    eventHub),
                Is.EqualTo(1));
            Assert.That(
                CountPublished<PackFileContainerRemovedEvent>(eventHub),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void SavePackContainer_GeneratesPackWithoutChangingProjectSources()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\keep.bin", [1, 2, 3]);
        project.Write(@"db\ignored.bin", [9]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings
            {
                Name = "工程",
                GameVersion = GameTypeEnum.Warhammer3,
                PackFileVersion = PackFileVersion.PFH5,
                DependantFiles = ["required.pack"],
                IgnoredPaths = new HashSet<string>(
                    [@"db\ignored.bin"],
                    StringComparer.OrdinalIgnoreCase),
            });
        container.FileList[@".git\config"] =
            PackFile.CreateFromBytes("config", [7]);
        container.FileList[@".gitignore"] =
            PackFile.CreateFromBytes(".gitignore", [8]);
        container.FileList[
                @"db\item.0123456789abcdef0123456789abcdef.tmp"] =
            PackFile.CreateFromBytes(
                "item.0123456789abcdef0123456789abcdef.tmp",
                [6]);
        var source = container.FileList[@"db\keep.bin"].DataSource;
        var service = CreateService();
        service.AddContainer(container, true);
        var outputPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-output-{Guid.NewGuid():N}.pack");
        PackFileContainer? loaded = null;

        try
        {
            service.SavePackContainer(
                container,
                outputPath,
                false,
                GameInformationDatabase.GetGameById(
                    GameTypeEnum.Rome2));

            using (var stream = File.OpenRead(outputPath))
            using (var reader = new BinaryReader(stream))
            {
                loaded = PackFileSerializerLoader.Load(
                    outputPath,
                    stream.Length,
                    reader,
                    new CaPackDuplicateFileResolver());
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    loaded.FileList.Keys,
                    Is.EquivalentTo(new[] { @"db\keep.bin" }));
                Assert.That(
                    loaded.FileList[@"db\keep.bin"]
                        .DataSource.ReadData(),
                    Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(
                    loaded.Header.DependantFiles,
                    Is.EqualTo(new[] { "required.pack" }));
                Assert.That(
                    container.FileList[@"db\keep.bin"].DataSource,
                    Is.SameAs(source));
                Assert.That(source, Is.TypeOf<FileSystemSource>());
                Assert.That(container.SystemFilePath, Is.EqualTo(project.Path));
                Assert.That(
                    container.ProjectSettings.OutputPackPath,
                    Is.EqualTo(outputPath));
            });
        }
        finally
        {
            if (loaded != null)
            {
                foreach (var packedSource in loaded.FileList.Values
                             .Select(file => file.DataSource)
                             .OfType<PackedFileSource>())
                {
                    packedSource.Parent.CloseStream();
                }
            }
            File.Delete(outputPath);
        }
    }

    [Test]
    public void SavePackContainer_FolderProjectDoesNotCreateBackup()
    {
        using var project = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        project.Write("keep.bin", [1, 2, 3]);
        var outputPath = System.IO.Path.Combine(
            output.Path,
            "generated.pack");
        File.WriteAllBytes(outputPath, [9, 9, 9]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings
            {
                Name = "工程",
                OutputPackPath = outputPath,
            });
        var service = CreateService();
        service.AddContainer(container, true);

        service.SavePackContainer(
            container,
            outputPath,
            false,
            GameInformationDatabase.GetGameById(
                GameTypeEnum.Warhammer3));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(
                Directory.Exists(
                    System.IO.Path.Combine(
                        output.Path,
                        "AssetEditor-BackUp")),
                Is.False);
        });
    }

    [Test]
    public void SavePackContainer_CorruptionDetectionEnabled_WritesFreshValidationFiles()
    {
        using var project = new TemporaryDirectory();
        project.Write(@"db\keep.bin", [1, 2, 3]);
        project.Write(
            s_corruptionDetectionFiles[0].Path,
            System.Text.Encoding.UTF8.GetBytes("stale"));
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings
            {
                Name = "工程",
                GameVersion = GameTypeEnum.Warhammer3,
                PackFileVersion = PackFileVersion.PFH5,
                EnablePackFileCorruptionDetection = true,
            });
        var service = CreateService();
        var outputPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-corruption-detection-{Guid.NewGuid():N}.pack");
        PackFileContainer? loaded = null;

        try
        {
            service.SavePackContainer(
                container,
                outputPath,
                false,
                GameInformationDatabase.GetGameById(
                    GameTypeEnum.Warhammer3));

            using (var stream = File.OpenRead(outputPath))
            using (var reader = new BinaryReader(stream))
            {
                loaded = PackFileSerializerLoader.Load(
                    outputPath,
                    stream.Length,
                    reader,
                    new CaPackDuplicateFileResolver());
            }

            Assert.That(
                loaded.FileList.Keys,
                Is.EquivalentTo(
                    new[]
                    {
                        @"db\keep.bin",
                        s_corruptionDetectionFiles[0].Path,
                        s_corruptionDetectionFiles[1].Path,
                        s_corruptionDetectionFiles[2].Path,
                    }));
            foreach (var validationFile in s_corruptionDetectionFiles)
            {
                Assert.That(
                    System.Text.Encoding.UTF8.GetString(
                        loaded.FileList[validationFile.Path]
                            .DataSource.ReadData()),
                    Is.EqualTo(validationFile.Content));
            }
        }
        finally
        {
            if (loaded != null)
            {
                foreach (var packedSource in loaded.FileList.Values
                             .Select(file => file.DataSource)
                             .OfType<PackedFileSource>())
                {
                    packedSource.Parent.CloseStream();
                }
            }
            File.Delete(outputPath);
        }
    }

    [Test]
    public void SavePackContainer_OutputInsideProject_IsRejected()
    {
        using var project = new TemporaryDirectory();
        project.Write("keep.bin", [1]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var service = CreateService();
        service.AddContainer(container, true);
        var outputPath = System.IO.Path.Combine(
            project.Path,
            "generated.pack");

        Assert.That(
            () => service.SavePackContainer(
                container,
                outputPath,
                false,
                GameInformationDatabase.GetGameById(
                    GameTypeEnum.Warhammer3)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(File.Exists(outputPath), Is.False);
    }

    [Test]
    public void SavePackContainer_OutputMatchesLoadedPack_RejectsWithoutOverwriting()
    {
        using var project = new TemporaryDirectory();
        project.Write("keep.bin", [1, 2, 3]);
        using var folderProject = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var outputPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-loaded-output-{Guid.NewGuid():N}.pack");
        var originalBytes = new byte[] { 9, 8, 7, 6 };
        File.WriteAllBytes(outputPath, originalBytes);
        var loadedPack = new PackFileContainer("已打开")
        {
            Header = new PFHeader(
                PackFileVersionConverter.ToString(PackFileVersion.PFH5),
                PackFileCAType.MOD),
            SystemFilePath = outputPath,
        };
        var service = CreateService();
        service.AddContainer(loadedPack);
        service.AddContainer(folderProject, true);

        try
        {
            Assert.That(
                () => service.SavePackContainer(
                    folderProject,
                    outputPath,
                    false,
                    GameInformationDatabase.GetGameById(
                        GameTypeEnum.Warhammer3)),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                File.ReadAllBytes(outputPath),
                Is.EqualTo(originalBytes));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Test]
    public void SavePackContainer_BlocksRefreshButAllowsLookupDuringSerialization()
    {
        using var project = new TemporaryDirectory();
        project.Write("keep.bin", [1, 2, 3]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        using var source = new BlockingDataSource([1, 2, 3]);
        var expectedFile = container.FileList["keep.bin"];
        expectedFile.DataSource = source;
        var service = CreateService();
        var outputPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-concurrent-save-{Guid.NewGuid():N}.pack");
        PackFileContainer? loaded = null;

        try
        {
            var saveTask = Task.Factory.StartNew(
                () => service.SavePackContainer(
                    container,
                    outputPath,
                    false,
                    GameInformationDatabase.GetGameById(
                        GameTypeEnum.Rome2)),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.That(
                source.ReadStarted.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "The save did not start reading project data.");

            using var refreshStarted = new ManualResetEventSlim(false);
            var refreshTask = Task.Factory.StartNew(
                () =>
                {
                    refreshStarted.Set();
                    return container.RefreshFromDisk();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.That(
                refreshStarted.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "The refresh task did not start.");
            var refreshCompletedDuringSave =
                refreshTask.Wait(TimeSpan.FromMilliseconds(250));

            using var lookupStarted = new ManualResetEventSlim(false);
            var lookupTask = Task.Factory.StartNew(
                () =>
                {
                    lookupStarted.Set();
                    return service.FindFile("keep.bin", container);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.That(
                lookupStarted.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "The lookup task did not start.");
            var lookupCompletedDuringSave =
                lookupTask.Wait(TimeSpan.FromMilliseconds(250));

            source.ReleaseRead.Set();
            Assert.That(
                Task.WaitAll(
                    [saveTask, refreshTask, lookupTask],
                    TimeSpan.FromSeconds(10)),
                Is.True,
                "The save, refresh, or lookup did not complete.");

            PackFileContainer loadedPack;
            using (var stream = File.OpenRead(outputPath))
            using (var reader = new BinaryReader(stream))
            {
                loadedPack = PackFileSerializerLoader.Load(
                    outputPath,
                    stream.Length,
                    reader,
                    new CaPackDuplicateFileResolver());
            }
            loaded = loadedPack;

            Assert.Multiple(() =>
            {
                Assert.That(refreshCompletedDuringSave, Is.False);
                Assert.That(lookupCompletedDuringSave, Is.True);
                Assert.That(lookupTask.Result, Is.SameAs(expectedFile));
                Assert.That(
                    loadedPack.FileList["keep.bin"]
                        .DataSource.ReadData(),
                    Is.EqualTo(new byte[] { 1, 2, 3 }));
            });
        }
        finally
        {
            source.ReleaseRead.Set();
            if (loaded != null)
            {
                foreach (var packedSource in loaded.FileList.Values
                             .Select(file => file.DataSource)
                             .OfType<PackedFileSource>())
                {
                    packedSource.Parent.CloseStream();
                }
            }
            File.Delete(outputPath);
        }
    }

    [Test]
    public void SavePackContainer_BlocksIgnoredPathMutationUntilSerializationCompletes()
    {
        using var project = new TemporaryDirectory();
        project.Write("keep.bin", [1, 2, 3]);
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        using var source = new BlockingDataSource([1, 2, 3]);
        container.FileList["keep.bin"].DataSource = source;
        var service = CreateService();
        var outputPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-concurrent-ignore-{Guid.NewGuid():N}.pack");

        try
        {
            var saveTask = Task.Factory.StartNew(
                () => service.SavePackContainer(
                    container,
                    outputPath,
                    false,
                    GameInformationDatabase.GetGameById(
                        GameTypeEnum.Rome2)),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.That(
                source.ReadStarted.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "The save did not start reading project data.");

            using var mutationStarted = new ManualResetEventSlim(false);
            var mutationTask = Task.Factory.StartNew(
                () =>
                {
                    mutationStarted.Set();
                    container.SetIgnored("keep.bin", true);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.That(
                mutationStarted.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "The ignored-path task did not start.");
            var mutationCompletedDuringSave =
                mutationTask.Wait(TimeSpan.FromMilliseconds(250));

            source.ReleaseRead.Set();
            Assert.That(
                Task.WaitAll(
                    [saveTask, mutationTask],
                    TimeSpan.FromSeconds(10)),
                Is.True,
                "The save or ignored-path update did not complete.");
            Assert.Multiple(() =>
            {
                Assert.That(mutationCompletedDuringSave, Is.False);
                Assert.That(container.IsIgnored("keep.bin"), Is.True);
            });
        }
        finally
        {
            source.ReleaseRead.Set();
            File.Delete(outputPath);
        }
    }

    [Test]
    public void TryAutoSavePackContainer_FolderProjectIsRejected()
    {
        using var project = new TemporaryDirectory();
        project.Write("keep.bin", [4, 5, 6]);
        var outputPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-autosave-{Guid.NewGuid():N}.pack");
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings
            {
                Name = "工程",
                OutputPackPath = outputPath,
                GameVersion = GameTypeEnum.Warhammer3,
            });
        var service = CreateService();
        service.AddContainer(container, true);

        Assert.Multiple(() =>
        {
            Assert.That(
                service.TryAutoSavePackContainer(
                    container,
                    outputPath,
                    GameInformationDatabase.GetGameById(
                        GameTypeEnum.Rome2)),
                Is.False);
            Assert.That(File.Exists(outputPath), Is.False);
            Assert.That(container.SystemFilePath, Is.EqualTo(project.Path));
        });
    }

    [Test]
    public void CopyFileFromOtherPackFile_WritesIntoProjectDirectory()
    {
        using var project = new TemporaryDirectory();
        using var target = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var source = new PackFileContainer("source")
        {
            Header = new PFHeader(
                PackFileVersionConverter.ToString(PackFileVersion.PFH5),
                PackFileCAType.MOD),
        };
        source.FileList[@"db\copied.bin"] =
            PackFile.CreateFromBytes("copied.bin", [7, 8]);
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(target, true);
        eventHub.Invocations.Clear();

        service.CopyFileFromOtherPackFile(
            source,
            @"db\copied.bin",
            target);

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                File.ReadAllBytes(
                    Path.Combine(project.Path, "db", "copied.bin")),
                Is.EqualTo(new byte[] { 7, 8 }));
            Assert.That(
                changeEvent.ChangeSet.FileChanges.Single().Path,
                Is.EqualTo(@"db\copied.bin"));
            Assert.That(
                CountPublished<PackFileContainerFilesAddedEvent>(eventHub),
                Is.Zero);
        });
    }

    [Test]
    public void CreateFolder_CreatesPersistentEmptyDirectory()
    {
        using var project = new TemporaryDirectory();
        using var container = FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" });
        var eventHub = new Mock<IGlobalEventHub>();
        var service = CreateService(eventHub.Object);
        service.AddContainer(container, true);
        eventHub.Invocations.Clear();

        service.CreateFolder(container, @"empty\nested");

        var changeEvent = eventHub.Invocations
            .Select(invocation => invocation.Arguments.SingleOrDefault())
            .OfType<FolderProjectChangedEvent>()
            .Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(
                    Path.Combine(project.Path, "empty", "nested")),
                Is.True);
            Assert.That(
                container.EmptyDirectories,
                Does.Contain(@"empty\nested"));
            Assert.That(
                changeEvent.ChangeSet.DirectoryChanges,
                Is.EqualTo(new[]
                {
                    new FolderProjectDirectoryChange(
                        @"empty\nested",
                        FolderProjectDirectoryChangeKind.Added),
                }));
        });
    }

    private static PackFileService CreateService(
        IGlobalEventHub? eventHub = null)
    {
        return new PackFileService(eventHub)
        {
            EnforceGameFilesMustBeLoaded = false,
            MessageBoxProvider = Mock.Of<ISimpleMessageBox>(),
        };
    }

    private static int CountPublished<T>(
        Mock<IGlobalEventHub> eventHub)
    {
        return eventHub.Invocations.Count(
            invocation =>
                invocation.Method.Name ==
                nameof(IGlobalEventHub.PublishGlobalEvent) &&
                invocation.Arguments.Count == 1 &&
                invocation.Arguments[0] is T);
    }

    private sealed class BlockingDataSource(byte[] data) :
        IDataSource,
        IDisposable
    {
        public ManualResetEventSlim ReadStarted { get; } = new(false);
        public ManualResetEventSlim ReleaseRead { get; } = new(false);

        public long Size => data.Length;
        public CompressionFormat CompressionFormat =>
            CompressionFormat.None;

        public byte[] ReadData()
        {
            ReadStarted.Set();
            if (!ReleaseRead.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the read.");
            return data.ToArray();
        }

        public byte[] PeekData(int size)
        {
            return data.Take(size).ToArray();
        }

        public ByteChunk ReadDataAsChunk()
        {
            return new ByteChunk(ReadData());
        }

        public void Dispose()
        {
            ReadStarted.Dispose();
            ReleaseRead.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-project-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Write(string relativePath, byte[] bytes)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
