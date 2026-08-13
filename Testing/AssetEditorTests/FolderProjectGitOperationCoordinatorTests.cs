using System.Windows.Threading;
using AssetEditor.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace AssetEditorTests;

public class FolderProjectGitOperationCoordinatorTests
{
    [Test]
    public void Execute_WhenUnloadIsVetoed_DoesNotRunOperation()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = CreateProject(projectRoot.Path);
        var containers = new List<PackFileContainer> { project };
        var packFileService = CreatePackFileServiceMock(containers);
        packFileService.Setup(
                service => service.TryUnloadPackContainer(project))
            .Returns(false);
        var versionControl =
            new Mock<IFolderProjectVersionControlService>(
                MockBehavior.Strict);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService.Object,
            Mock.Of<IFolderProjectFactory>(),
            versionControl.Object);
        var operationCalls = 0;

        NUnitAssert.Throws<FolderProjectGitOperationCanceledException>(
            () => coordinator.Execute(
                projectRoot.Path,
                () => ++operationCalls));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(operationCalls, Is.Zero);
            NUnitAssert.That(
                containers,
                Is.EqualTo(new PackFileContainer[] { project }));
        });
        versionControl.Verify(
            item => item.GetMergeState(Normalize(projectRoot.Path)),
            Times.Never);
    }

    [Test]
    public void Execute_WhenMergeStateIsNone_ReattachesNewInstanceAtOriginalPosition()
    {
        using var projectRoot = new TemporaryDirectory();
        var packFileService = CreateRealPackFileService();
        var first = CreatePack("first");
        var original = CreateProject(projectRoot.Path);
        var last = CreatePack("last");
        packFileService.AddContainer(first);
        packFileService.AddContainer(
            original,
            1,
            true,
            PackFileContainerAddedReason.UserOpen);
        packFileService.AddContainer(last);
        FolderProjectContainer? reattached = null;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl =
            new Mock<IFolderProjectVersionControlService>(
                MockBehavior.Strict);
        versionControl.Setup(
                item => item.GetMergeState(Normalize(projectRoot.Path)))
            .Returns(MergeState(FolderProjectMergePhase.None));
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);

        var result = coordinator.Execute(projectRoot.Path, () =>
        {
            NUnitAssert.That(
                packFileService.GetAllPackfileContainers(),
                Does.Not.Contain(original));
            NUnitAssert.That(original.IsWatching, Is.False);
            NUnitAssert.Throws<ObjectDisposedException>(
                () => original.RefreshFromDisk());
            return 42;
        });

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(result, Is.EqualTo(42));
                NUnitAssert.That(reattached, Is.Not.Null);
                NUnitAssert.That(reattached, Is.Not.SameAs(original));
                NUnitAssert.That(
                    packFileService.GetAllPackfileContainers(),
                    Is.EqualTo(new PackFileContainer[]
                    {
                        first,
                        reattached!,
                        last,
                    }));
                NUnitAssert.That(
                    packFileService.GetEditablePack(),
                    Is.SameAs(reattached));
                NUnitAssert.That(reattached!.IsWatching, Is.True);
            });
        }
        finally
        {
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public void Execute_WhenMergeRemainsPending_ReattachesAfterLaterCompletion()
    {
        using var projectRoot = new TemporaryDirectory();
        var packFileService = CreateRealPackFileService();
        var original = CreateProject(projectRoot.Path);
        packFileService.AddContainer(original);
        FolderProjectContainer? reattached = null;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl = new Mock<IFolderProjectVersionControlService>(
            MockBehavior.Strict);
        versionControl.SetupSequence(
                item => item.GetMergeState(Normalize(projectRoot.Path)))
            .Returns(MergeState(FolderProjectMergePhase.Conflicts))
            .Returns(MergeState(FolderProjectMergePhase.None));
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);

        coordinator.Execute(projectRoot.Path, () => 1);
        NUnitAssert.That(
            packFileService.GetAllPackfileContainers(),
            Is.Empty);

        coordinator.Execute(projectRoot.Path, () => 2);

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(reattached, Is.Not.Null);
                NUnitAssert.That(
                    packFileService.GetAllPackfileContainers(),
                    Is.EqualTo(
                        new PackFileContainer[] { reattached! }));
            });
        }
        finally
        {
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public void Execute_WhenInitiallyUnloaded_OpensOnlyWhenRequested()
    {
        using var projectRoot = new TemporaryDirectory();
        using (var project = CreateProject(projectRoot.Path))
        {
        }

        var packFileService = CreateRealPackFileService();
        FolderProjectContainer? opened = null;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                opened = FolderProjectContainer.Open(projectRoot.Path);
                return opened;
            });
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);

        coordinator.Execute(projectRoot.Path, () => 1);
        NUnitAssert.That(
            packFileService.GetAllPackfileContainers(),
            Is.Empty);

        coordinator.Execute(
            projectRoot.Path,
            () => 2,
            openWhenComplete: true);

        try
        {
            NUnitAssert.That(
                packFileService.GetAllPackfileContainers(),
                Is.EqualTo(new PackFileContainer[] { opened! }));
        }
        finally
        {
            if (opened != null)
                packFileService.TryUnloadPackContainer(opened);
        }
    }

    [Test]
    public void Execute_WhenOperationThrows_RestoresThenRethrowsOriginalException()
    {
        using var projectRoot = new TemporaryDirectory();
        var packFileService = CreateRealPackFileService();
        var original = CreateProject(projectRoot.Path);
        packFileService.AddContainer(original);
        FolderProjectContainer? reattached = null;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);
        var expected = new InvalidOperationException("operation failed");

        var actual = NUnitAssert.Throws<InvalidOperationException>(
            () => coordinator.Execute<int>(
                projectRoot.Path,
                () => throw expected));

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(actual, Is.SameAs(expected));
                NUnitAssert.That(reattached, Is.Not.Null);
                NUnitAssert.That(
                    packFileService.GetAllPackfileContainers(),
                    Is.EqualTo(
                        new PackFileContainer[] { reattached! }));
            });
        }
        finally
        {
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public void Execute_WhenReopenFails_ThrowsHostExceptionAndNeverReusesOldInstance()
    {
        using var projectRoot = new TemporaryDirectory();
        var packFileService = CreateRealPackFileService();
        var original = CreateProject(projectRoot.Path);
        packFileService.AddContainer(original);
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Throws(new IOException("reopen failed"));
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);

        var exception = NUnitAssert.Throws<FolderProjectGitHostException>(
            () => coordinator.Execute(projectRoot.Path, () => 1));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(exception!.InnerException, Is.TypeOf<IOException>());
            NUnitAssert.That(
                packFileService.GetAllPackfileContainers(),
                Is.Empty);
            NUnitAssert.That(original.IsWatching, Is.False);
            NUnitAssert.Throws<ObjectDisposedException>(
                () => original.RefreshFromDisk());
        });
    }

    [Test]
    public async Task ExecuteTransactionalAsync_WhenFirstReopenFails_RollsBackAndReopensOriginalState()
    {
        using var projectRoot = new TemporaryDirectory();
        var packFileService = CreateRealPackFileService();
        var original = CreateProject(projectRoot.Path);
        packFileService.AddContainer(original);
        FolderProjectContainer? reattached = null;
        var openCalls = 0;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                openCalls++;
                if (openCalls == 1)
                    throw new IOException("first reopen failed");
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);
        var rollbackCalls = 0;
        var completeCalls = 0;

        var exception = NUnitAssert.ThrowsAsync<FolderProjectGitHostException>(
            async () => await coordinator.ExecuteTransactionalAsync(
                projectRoot.Path,
                () => 42,
                _ => completeCalls++,
                result =>
                {
                    NUnitAssert.That(result, Is.EqualTo(42));
                    rollbackCalls++;
                }));

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(exception!.HostException, Is.TypeOf<IOException>());
                NUnitAssert.That(rollbackCalls, Is.EqualTo(1));
                NUnitAssert.That(completeCalls, Is.Zero);
                NUnitAssert.That(openCalls, Is.EqualTo(2));
                NUnitAssert.That(reattached, Is.Not.Null);
                NUnitAssert.That(
                    packFileService.GetAllPackfileContainers(),
                    Is.EqualTo(new PackFileContainer[] { reattached! }));
            });
        }
        finally
        {
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public async Task ExecuteTransactionalAsync_WhenFinalizationFails_RollsBackAndReopensOriginalState()
    {
        using var projectRoot = new TemporaryDirectory();
        var packFileService = CreateRealPackFileService();
        var original = CreateProject(projectRoot.Path);
        packFileService.AddContainer(original);
        FolderProjectContainer? reattached = null;
        var openCalls = 0;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                openCalls++;
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);
        var rollbackCalls = 0;
        var expected = new IOException("history refresh failed");

        var exception = NUnitAssert.ThrowsAsync<FolderProjectGitHostException>(
            async () => await coordinator.ExecuteTransactionalAsync(
                projectRoot.Path,
                () => 42,
                _ => throw expected,
                result =>
                {
                    NUnitAssert.That(result, Is.EqualTo(42));
                    rollbackCalls++;
                }));

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(exception!.HostException, Is.SameAs(expected));
                NUnitAssert.That(rollbackCalls, Is.EqualTo(1));
                NUnitAssert.That(openCalls, Is.EqualTo(2));
                NUnitAssert.That(reattached, Is.Not.Null);
                NUnitAssert.That(
                    packFileService.GetAllPackfileContainers(),
                    Is.EqualTo(new PackFileContainer[] { reattached! }));
            });
        }
        finally
        {
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public async Task ExecuteTransactionalAsync_WhenRollbackFails_RemainsDetached()
    {
        using var projectRoot = new TemporaryDirectory();
        var packFileService = CreateRealPackFileService();
        var original = CreateProject(projectRoot.Path);
        packFileService.AddContainer(original);
        FolderProjectContainer? reattached = null;
        var openCalls = 0;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                openCalls++;
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);
        var expectedCompletion = new IOException("history refresh failed");
        var expectedRollback = new IOException("staging cleanup failed");

        var exception = NUnitAssert.ThrowsAsync<FolderProjectGitHostException>(
            async () => await coordinator.ExecuteTransactionalAsync(
                projectRoot.Path,
                () => 42,
                _ => throw expectedCompletion,
                _ => throw expectedRollback));

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(exception!.OperationException,
                    Is.SameAs(expectedCompletion));
                NUnitAssert.That(exception.HostException,
                    Is.SameAs(expectedRollback));
                NUnitAssert.That(openCalls, Is.EqualTo(1));
                NUnitAssert.That(
                    packFileService.GetAllPackfileContainers(),
                    Is.Empty);
            });
        }
        finally
        {
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public async Task ExecuteTransactionalAsync_WhenRollbackPreparationFails_RemainsDetached()
    {
        using var projectRoot = new TemporaryDirectory();
        var packFileService = CreateRealPackFileService();
        var original = CreateProject(projectRoot.Path);
        packFileService.AddContainer(original);
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() => FolderProjectContainer.Open(projectRoot.Path));
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object,
            new FailRollbackPreparationPlatform());
        var rollbackCalls = 0;

        var exception = NUnitAssert.ThrowsAsync<FolderProjectGitHostException>(
            async () => await coordinator.ExecuteTransactionalAsync(
                projectRoot.Path,
                () => 42,
                _ => throw new IOException("history refresh failed"),
                _ => rollbackCalls++));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(exception!.HostException,
                Is.TypeOf<IOException>());
            NUnitAssert.That(rollbackCalls, Is.EqualTo(1));
            NUnitAssert.That(
                packFileService.GetAllPackfileContainers(),
                Is.Empty);
        });
    }

    [Test]
    public void Execute_OnlyTemporarilyRemovesSafeRegisteredEmptyDirectories()
    {
        using var projectRoot = new TemporaryDirectory();
        var emptyPath = Path.Combine(projectRoot.Path, "empty");
        var nonEmptyPath = Path.Combine(projectRoot.Path, "nonempty");
        var reparsePath = Path.Combine(projectRoot.Path, "reparse");
        Directory.CreateDirectory(emptyPath);
        Directory.CreateDirectory(nonEmptyPath);
        Directory.CreateDirectory(reparsePath);
        File.WriteAllText(Path.Combine(nonEmptyPath, "keep.bin"), "keep");
        var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings { Name = "工程" });
        var packFileService = CreateRealPackFileService();
        packFileService.AddContainer(project);
        project.ProjectSettings.EmptyDirectories.UnionWith(
            ["empty", "nonempty", "reparse"]);
        project.ProjectSettings.Save(projectRoot.Path);
        FolderProjectContainer? reattached = null;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object,
            new ReparsePointPlatform(reparsePath));

        coordinator.Execute(projectRoot.Path, () =>
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(Directory.Exists(emptyPath), Is.False);
                NUnitAssert.That(Directory.Exists(nonEmptyPath), Is.True);
                NUnitAssert.That(
                    File.Exists(Path.Combine(nonEmptyPath, "keep.bin")),
                    Is.True);
                NUnitAssert.That(Directory.Exists(reparsePath), Is.True);
            });
            return 1;
        });

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(Directory.Exists(emptyPath), Is.True);
                NUnitAssert.That(Directory.Exists(nonEmptyPath), Is.True);
                NUnitAssert.That(Directory.Exists(reparsePath), Is.True);
            });
        }
        finally
        {
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public void Execute_RemovesEmptyDirectorySettingWhenAncestorBecomesFile()
    {
        using var projectRoot = new TemporaryDirectory();
        var parentPath = Path.Combine(projectRoot.Path, "conflict");
        var childPath = Path.Combine(parentPath, "child");
        Directory.CreateDirectory(childPath);
        var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings
            {
                Name = "工程",
                EmptyDirectories = ["conflict\\child"],
            });
        var packFileService = CreateRealPackFileService();
        packFileService.AddContainer(project);
        FolderProjectContainer? reattached = null;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);

        coordinator.Execute(projectRoot.Path, () =>
        {
            if (Directory.Exists(parentPath))
                Directory.Delete(parentPath, false);
            File.WriteAllText(parentPath, "branch file");
            return 1;
        });

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(File.Exists(parentPath), Is.True);
                NUnitAssert.That(
                    FolderProjectSettings.Load(projectRoot.Path)
                        .EmptyDirectories,
                    Does.Not.Contain("conflict\\child"));
                NUnitAssert.That(reattached, Is.Not.Null);
            });
        }
        finally
        {
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public void Execute_ReopensUsingSettingsFromNewBranch()
    {
        using var projectRoot = new TemporaryDirectory();
        var oldEmptyPath = Path.Combine(projectRoot.Path, "old-empty");
        var newEmptyPath = Path.Combine(projectRoot.Path, "new-empty");
        Directory.CreateDirectory(oldEmptyPath);
        var project = FolderProjectContainer.Create(
            projectRoot.Path,
            new FolderProjectSettings
            {
                Name = "旧分支",
                EmptyDirectories = ["old-empty"],
            });
        var packFileService = CreateRealPackFileService();
        packFileService.AddContainer(project);
        FolderProjectContainer? reattached = null;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);

        coordinator.Execute(projectRoot.Path, () =>
        {
            new FolderProjectSettings
            {
                Name = "新分支",
                OutputPackPath = "new-output.pack",
                IgnoredPaths = ["ignored"],
                EmptyDirectories = ["new-empty"],
            }.Save(projectRoot.Path);
            return 1;
        });

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(reattached!.Name, Is.EqualTo("新分支"));
                NUnitAssert.That(
                    reattached.ProjectSettings.OutputPackPath,
                    Is.EqualTo("new-output.pack"));
                NUnitAssert.That(
                    reattached.ProjectSettings.IgnoredPaths,
                    Does.Contain("ignored"));
                NUnitAssert.That(Directory.Exists(newEmptyPath), Is.True);
                NUnitAssert.That(Directory.Exists(oldEmptyPath), Is.False);
            });
        }
        finally
        {
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public void Execute_WhenUnloadThrowsAfterDetach_ReattachesBeforeRethrowing()
    {
        using var projectRoot = new TemporaryDirectory();
        var project = CreateProject(projectRoot.Path);
        var packFileService = CreateRealPackFileService();
        packFileService.AddContainer(project);
        var expected = new InvalidOperationException("removed event failed");
        packFileService.ExceptionAfterUnload = expected;
        FolderProjectContainer? reattached = null;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);
        var operationCalls = 0;

        var actual = NUnitAssert.Throws<InvalidOperationException>(
            () => coordinator.Execute(
                projectRoot.Path,
                () => ++operationCalls));

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(actual, Is.SameAs(expected));
                NUnitAssert.That(operationCalls, Is.Zero);
                NUnitAssert.That(reattached, Is.Not.Null);
                NUnitAssert.That(
                    packFileService.GetAllPackfileContainers(),
                    Is.EqualTo(
                        new PackFileContainer[] { reattached! }));
            });
        }
        finally
        {
            packFileService.ExceptionAfterUnload = null;
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public void Execute_WhenUnloadThrowsBeforeDetach_DoesNotRunOrReattach()
    {
        using var projectRoot = new TemporaryDirectory();
        using var project = CreateProject(projectRoot.Path);
        var containers = new List<PackFileContainer> { project };
        var packFileService = CreatePackFileServiceMock(containers);
        var expected = new InvalidOperationException("before event failed");
        packFileService.Setup(
                service => service.TryUnloadPackContainer(project))
            .Throws(expected);
        var factory = new Mock<IFolderProjectFactory>(
            MockBehavior.Strict);
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService.Object,
            factory.Object,
            versionControl.Object);
        var operationCalls = 0;

        var actual = NUnitAssert.Throws<InvalidOperationException>(
            () => coordinator.Execute(
                projectRoot.Path,
                () => ++operationCalls));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(actual, Is.SameAs(expected));
            NUnitAssert.That(operationCalls, Is.Zero);
            NUnitAssert.That(
                containers,
                Is.EqualTo(new PackFileContainer[] { project }));
        });
        factory.Verify(
            item => item.Open(It.IsAny<string>()),
            Times.Never);
        versionControl.Verify(
            item => item.GetMergeState(It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public void Execute_WhenOperationAndReattachFail_PreservesBothExceptions()
    {
        using var projectRoot = new TemporaryDirectory();
        var project = CreateProject(projectRoot.Path);
        var packFileService = CreateRealPackFileService();
        packFileService.AddContainer(project);
        var operationFailure =
            new InvalidOperationException("operation failed");
        var hostFailure = new IOException("reopen failed");
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Throws(hostFailure);
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);

        var exception = NUnitAssert.Throws<FolderProjectGitHostException>(
            () => coordinator.Execute<int>(
                projectRoot.Path,
                () => throw operationFailure));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                exception!.OperationException,
                Is.SameAs(operationFailure));
            NUnitAssert.That(
                exception.HostException,
                Is.SameAs(hostFailure));
            NUnitAssert.That(
                exception.InnerException,
                Is.TypeOf<AggregateException>());
        });
    }

    [Test]
    public void Execute_WhenInitializeFailsBeforeRepositoryExists_ReattachesAndRethrows()
    {
        using var projectRoot = new TemporaryDirectory();
        var project = CreateProject(projectRoot.Path);
        var packFileService = CreateRealPackFileService();
        packFileService.AddContainer(project);
        FolderProjectContainer? reattached = null;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                reattached = FolderProjectContainer.Open(projectRoot.Path);
                return reattached;
            });
        var versionControl =
            new Mock<IFolderProjectVersionControlService>(
                MockBehavior.Strict);
        versionControl.Setup(
                item => item.GetMergeState(Normalize(projectRoot.Path)))
            .Throws(new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryNotInitialized,
                "Repository is not initialized."));
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);
        var operationFailure =
            new IOException("repository initialization failed");

        var actual = NUnitAssert.Throws<IOException>(
            () => coordinator.Execute<int>(
                projectRoot.Path,
                () => throw operationFailure));

        try
        {
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(actual, Is.SameAs(operationFailure));
                NUnitAssert.That(reattached, Is.Not.Null);
                NUnitAssert.That(reattached, Is.Not.SameAs(project));
                NUnitAssert.That(
                    packFileService.GetAllPackfileContainers(),
                    Is.EqualTo(
                        new PackFileContainer[] { reattached! }));
            });
        }
        finally
        {
            if (reattached != null)
                packFileService.TryUnloadPackContainer(reattached);
        }
    }

    [Test]
    public void Execute_WhenInitiallyUnloadedInitializeFails_DoesNotOpenAndRethrows()
    {
        using var projectRoot = new TemporaryDirectory();
        using (var project = CreateProject(projectRoot.Path))
        {
        }

        var packFileService = CreatePackFileServiceMock([]);
        var factory = new Mock<IFolderProjectFactory>(
            MockBehavior.Strict);
        var versionControl =
            new Mock<IFolderProjectVersionControlService>(
                MockBehavior.Strict);
        versionControl.Setup(
                item => item.GetMergeState(Normalize(projectRoot.Path)))
            .Throws(new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryNotInitialized,
                "Repository is not initialized."));
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService.Object,
            factory.Object,
            versionControl.Object);
        var operationFailure =
            new IOException("repository initialization failed");

        var actual = NUnitAssert.Throws<IOException>(
            () => coordinator.Execute<int>(
                projectRoot.Path,
                () => throw operationFailure,
                true));

        NUnitAssert.That(actual, Is.SameAs(operationFailure));
        factory.Verify(
            item => item.Open(It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public void Execute_SameRootSerializesAndReleasesGateAfterException()
    {
        using var projectRoot = new TemporaryDirectory();
        using (var project = CreateProject(projectRoot.Path))
        {
        }

        var packFileService = CreatePackFileServiceMock([]);
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService.Object,
            Mock.Of<IFolderProjectFactory>(),
            versionControl.Object);
        using var firstEntered = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();

        var first = Task.Run(() =>
            NUnitAssert.Throws<InvalidOperationException>(() =>
                coordinator.Execute<int>(projectRoot.Path, () =>
                {
                    firstEntered.Set();
                    releaseFirst.Wait();
                    throw new InvalidOperationException("expected");
                })));
        NUnitAssert.That(firstEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        var second = Task.Run(() =>
            coordinator.Execute(projectRoot.Path, () =>
            {
                secondEntered.Set();
                return 2;
            }));

        NUnitAssert.That(
            secondEntered.Wait(TimeSpan.FromMilliseconds(150)),
            Is.False);
        releaseFirst.Set();
        NUnitAssert.That(
            Task.WaitAll([first, second], TimeSpan.FromSeconds(5)),
            Is.True);
        NUnitAssert.That(secondEntered.IsSet, Is.True);
    }

    [Test]
    public void Execute_DifferentRootsDoNotShareGate()
    {
        using var firstRoot = new TemporaryDirectory();
        using var secondRoot = new TemporaryDirectory();
        using (var project = CreateProject(firstRoot.Path))
        {
        }
        using (var project = CreateProject(secondRoot.Path))
        {
        }

        var packFileService = CreatePackFileServiceMock([]);
        var versionControl = new Mock<IFolderProjectVersionControlService>();
        versionControl.Setup(
                item => item.GetMergeState(It.IsAny<string>()))
            .Returns(MergeState(FolderProjectMergePhase.None));
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService.Object,
            Mock.Of<IFolderProjectFactory>(),
            versionControl.Object);
        using var firstEntered = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();

        var first = Task.Run(() =>
            coordinator.Execute(firstRoot.Path, () =>
            {
                firstEntered.Set();
                releaseFirst.Wait();
                return 1;
            }));
        NUnitAssert.That(firstEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        var second = Task.Run(() =>
            coordinator.Execute(secondRoot.Path, () =>
            {
                secondEntered.Set();
                return 2;
            }));

        NUnitAssert.That(
            secondEntered.Wait(TimeSpan.FromSeconds(5)),
            Is.True);
        releaseFirst.Set();
        NUnitAssert.That(
            Task.WaitAll([first, second], TimeSpan.FromSeconds(5)),
            Is.True);
    }

    [Test]
    public async Task ExecuteAsync_DetachedWorkDoesNotBlockCaller()
    {
        using var projectRoot = new TemporaryDirectory();
        using (var project = CreateProject(projectRoot.Path))
        {
        }

        var packFileService = CreatePackFileServiceMock([]);
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService.Object,
            Mock.Of<IFolderProjectFactory>(),
            versionControl.Object);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var operation = coordinator.ExecuteAsync(
            projectRoot.Path,
            () =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return 42;
            });

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                entered.Wait(TimeSpan.FromSeconds(5)),
                Is.True);
            NUnitAssert.That(operation.IsCompleted, Is.False);
        });
        release.Set();

        NUnitAssert.That(await operation, Is.EqualTo(42));
    }

    [Test]
    public void ExecuteAsync_ReopensProjectOffCapturedUiContext()
    {
        using var projectRoot = new TemporaryDirectory();
        var packFileService = CreateRealPackFileService();
        packFileService.AddContainer(CreateProject(projectRoot.Path));
        var factoryThreadId = 0;
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(Normalize(projectRoot.Path)))
            .Returns(() =>
            {
                factoryThreadId = Environment.CurrentManagedThreadId;
                return FolderProjectContainer.Open(projectRoot.Path);
            });
        var versionControl = CreateVersionControlMock(
            projectRoot.Path,
            FolderProjectMergePhase.None);
        var coordinator = new FolderProjectGitOperationCoordinator(
            packFileService,
            factory.Object,
            versionControl.Object);
        var uiThreadId = Environment.CurrentManagedThreadId;
        var previousContext = SynchronizationContext.Current;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(dispatcher));

        try
        {
            var operation = coordinator.ExecuteAsync(
                projectRoot.Path,
                () => 42);
            var frame = new DispatcherFrame();
            _ = operation.ContinueWith(
                _ => frame.Continue = false,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(operation.GetAwaiter().GetResult(),
                    Is.EqualTo(42));
                NUnitAssert.That(factoryThreadId, Is.Not.EqualTo(uiThreadId));
                NUnitAssert.That(
                    packFileService.AddContainerThreadId,
                    Is.EqualTo(uiThreadId));
                NUnitAssert.That(
                    packFileService.AddContainerCallCount,
                    Is.EqualTo(2));
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            foreach (var container in packFileService
                         .GetAllPackfileContainers()
                         .ToList())
            {
                packFileService.TryUnloadPackContainer(container);
            }
        }
    }

    [Test]
    public void DependencyInjection_ResolvesCoordinator()
    {
        var provider =
            new DependencyInjectionConfig(false).Build(true);
        try
        {
            NUnitAssert.That(
                provider.GetRequiredService<
                    IFolderProjectGitOperationCoordinator>(),
                Is.TypeOf<FolderProjectGitOperationCoordinator>());
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    private static Mock<IPackFileService> CreatePackFileServiceMock(
        List<PackFileContainer> containers)
    {
        var service = new Mock<IPackFileService>();
        service.Setup(item => item.GetAllPackfileContainers())
            .Returns(() => containers.ToList());
        service.Setup(item => item.GetEditablePack())
            .Returns((PackFileContainer?)null);
        return service;
    }

    private static TestPackFileService CreateRealPackFileService()
    {
        return new TestPackFileService();
    }

    private static Mock<IFolderProjectVersionControlService>
        CreateVersionControlMock(
            string projectRoot,
            FolderProjectMergePhase phase)
    {
        var versionControl =
            new Mock<IFolderProjectVersionControlService>(
                MockBehavior.Strict);
        versionControl.Setup(
                item => item.GetMergeState(Normalize(projectRoot)))
            .Returns(MergeState(phase));
        return versionControl;
    }

    private static FolderProjectMergeState MergeState(
        FolderProjectMergePhase phase)
    {
        return new FolderProjectMergeState(
            phase,
            null,
            null,
            null,
            null,
            null,
            [],
            null);
    }

    private static PackFileContainer CreatePack(string name)
    {
        return new PackFileContainer(name)
        {
            SystemFilePath = Path.Combine(
                Path.GetTempPath(),
                $"{name}-{Guid.NewGuid():N}.pack"),
        };
    }

    private static FolderProjectContainer CreateProject(string projectRoot)
    {
        return FolderProjectContainer.Create(
            projectRoot,
            new FolderProjectSettings { Name = "工程" });
    }

    private static string Normalize(string projectRoot)
    {
        return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-git-host-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }

    private sealed class ReparsePointPlatform(string reparsePath) :
        FolderProjectGitOperationPlatform
    {
        public override FileAttributes GetAttributes(string path)
        {
            var attributes = base.GetAttributes(path);
            return string.Equals(
                path,
                reparsePath,
                StringComparison.OrdinalIgnoreCase)
                ? attributes | FileAttributes.ReparsePoint
                : attributes;
        }
    }

    private sealed class FailRollbackPreparationPlatform :
        FolderProjectGitOperationPlatform
    {
        public override void DeleteConfiguredEmptyDirectories(
            string projectRoot,
            Action deleteCore) =>
            throw new IOException("empty directory cleanup failed");
    }

    private sealed class TestPackFileService : IPackFileService
    {
        private readonly List<PackFileContainer> _containers = [];
        private PackFileContainer? _editable;

        public bool EnableFileLookUpEvents { get; set; }
        public bool EnforceGameFilesMustBeLoaded { get; set; }
        public Exception? ExceptionAfterUnload { get; set; }
        public int? AddContainerThreadId { get; private set; }
        public int AddContainerCallCount { get; private set; }

        public FolderProjectContainer? AddEditableFolderProject(
            FolderProjectContainer project) =>
            AddContainer(
                project,
                _containers.Count,
                true,
                PackFileContainerAddedReason.UserOpen) as
                FolderProjectContainer;

        public bool TryActivateFolderProject(string projectRoot)
        {
            var project = _containers
                .OfType<FolderProjectContainer>()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.SystemFilePath,
                    projectRoot,
                    StringComparison.OrdinalIgnoreCase));
            if (project == null)
                return false;

            _editable = project;
            return true;
        }

        public PackFileContainer? AddReferencePack(
            PackFileContainer referencePack) =>
            AddContainer(referencePack);

        public PackFileContainer? AddContainer(
            PackFileContainer container)
        {
            return AddContainer(
                container,
                _containers.Count,
                false,
                PackFileContainerAddedReason.UserOpen);
        }

        public FolderProjectContainer? ReattachFolderProject(
            FolderProjectContainer project,
            int insertionIndex,
            FolderProjectReattachMode mode) =>
            AddContainer(
                project,
                insertionIndex,
                mode == FolderProjectReattachMode.Editable,
                PackFileContainerAddedReason.InternalReattach) as
                FolderProjectContainer;

        public PackFileContainer? AddContainer(
            PackFileContainer container,
            int insertionIndex,
            bool setEditablePack,
            PackFileContainerAddedReason reason)
        {
            AddContainerThreadId = Environment.CurrentManagedThreadId;
            AddContainerCallCount++;
            _containers.Insert(
                Math.Clamp(insertionIndex, 0, _containers.Count),
                container);
            if (setEditablePack)
                _editable = container;
            if (container is FolderProjectContainer folderProject)
                folderProject.StartWatching();
            return container;
        }

        public List<PackFileContainer> GetAllPackfileContainers()
        {
            return _containers.ToList();
        }

        public PackFileContainer? GetEditablePack() => _editable;

        public bool TryUnloadPackContainer(PackFileContainer container)
        {
            if (!_containers.Remove(container))
                return false;
            if (ReferenceEquals(_editable, container))
                _editable = null;
            if (container is FolderProjectContainer folderProject)
                folderProject.Dispose();
            if (ExceptionAfterUnload != null)
                throw ExceptionAfterUnload;
            return true;
        }

        public void UnloadPackContainer(PackFileContainer container)
        {
            TryUnloadPackContainer(container);
        }

        public PackFile? FindFile(
            string path,
            PackFileContainer? container = null)
        {
            if (container != null)
            {
                return container.FileList.TryGetValue(path, out var file)
                    ? file
                    : null;
            }

            foreach (var current in _containers.AsEnumerable().Reverse())
            {
                if (current.FileList.TryGetValue(path, out var file))
                    return file;
            }

            return null;
        }

        public void SetEditablePack(PackFileContainer? container)
        {
            _editable = container;
        }

        public void AddFilesToPack(
            PackFileContainer container,
            List<NewPackFileEntry> newFiles,
            bool overwriteExisting = true) =>
            throw new NotSupportedException();

        public IReadOnlyList<PackFile> ApplyFileWrites(
            PackFileContainer container,
            IReadOnlyCollection<PackFileWrite> writes) =>
            throw new NotSupportedException();

        public void CopyFileFromOtherPackFile(
            PackFileContainer source,
            string path,
            PackFileContainer target) =>
            throw new NotSupportedException();

        public PackFileContainer CreateNewPackFileContainer(
            string name,
            PackFileVersion packFileVersion,
            PackFileCAType type) =>
            throw new NotSupportedException();

        public void CreateFolder(
            PackFileContainer container,
            string folder) =>
            throw new NotSupportedException();

        public void DeleteFile(
            PackFileContainer container,
            PackFile file) =>
            throw new NotSupportedException();

        public void DeleteFolder(
            PackFileContainer container,
            string folder) =>
            throw new NotSupportedException();

        public string GetFullPath(
            PackFile file,
            PackFileContainer? container = null) =>
            throw new NotSupportedException();

        public PackFileContainer? GetPackFileContainer(PackFile file) =>
            throw new NotSupportedException();

        public void MoveFile(
            PackFileContainer container,
            PackFile file,
            string newFolderPath) =>
            throw new NotSupportedException();

        public void RenameDirectory(
            PackFileContainer container,
            string currentNodeName,
            string newName) =>
            throw new NotSupportedException();

        public void RenameFile(
            PackFileContainer container,
            PackFile file,
            string newName) =>
            throw new NotSupportedException();

        public void SaveFile(PackFile file, byte[] data) =>
            throw new NotSupportedException();

        public void SavePackContainer(
            PackFileContainer container,
            string path,
            bool createBackup,
            Shared.Core.Settings.GameInformation gameInformation) =>
            throw new NotSupportedException();

        public bool TryAutoSavePackContainer(
            PackFileContainer container,
            string expectedPath,
            Shared.Core.Settings.GameInformation gameInformation) =>
            throw new NotSupportedException();
    }
}
