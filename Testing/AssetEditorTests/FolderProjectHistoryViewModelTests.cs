using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Services;
using AssetEditor.ViewModels;
using AssetEditor.Views.FolderProjectHistory;
using CommunityToolkit.Mvvm.Input;
using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace AssetEditorTests;

public class FolderProjectHistoryViewModelTests
{
    [SetUp]
    public void SetUp()
    {
        new LocalizationManager().LoadLanguage();
    }

    [Test]
    public async Task Refresh_UsesDisplayStatusAndShowsHistorySnapshot()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var history = CreateHistoryService(project.ProjectRoot);
        history.Setup(item => item.GetDisplayStatus(project.ProjectRoot))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                [
                    new FolderProjectUnrecordedChange(
                        "db\\units_tables\\changed.bin",
                        FolderProjectUnrecordedChangeKind.Modified),
                ]));
        history.Setup(item => item.GetRestorePoints(
                project.ProjectRoot,
                100,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns([
                RestorePoint("head", "调整单位数据"),
                RestorePoint("initial", "初始还原点", true),
            ]);
        var viewModel = CreateViewModel(history.Object);

        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.IsReady, Is.True);
            NUnitAssert.That(viewModel.HasUnrecordedChanges, Is.True);
            NUnitAssert.That(viewModel.UnrecordedChanges, Has.Count.EqualTo(1));
            NUnitAssert.That(viewModel.RestorePoints, Has.Count.EqualTo(2));
            NUnitAssert.That(
                viewModel.RestorePointDescription,
                Is.Empty);
            NUnitAssert.That(
                viewModel.CreateRestorePointCommand.CanExecute(null),
                Is.True);
        });
        history.Verify(item => item.GetStatus(
            project.ProjectRoot,
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Never);
    }

    [Test]
    public async Task RecoverHistory_ConfirmsUsesCoordinatorAndClosesRecoveryFlow()
    {
        var projectRoot = Path.GetFullPath("legacy-project");
        var recoveryStatus = new FolderProjectHistoryStatus(
            FolderProjectHistoryAvailability.RecoveryRequired,
            "head",
            [])
        {
            RecoveryReason =
                FolderProjectHistoryRecoveryReason.UnfinishedOperation,
            CanRecover = true,
        };
        var readyStatus = new FolderProjectHistoryStatus(
            FolderProjectHistoryAvailability.Ready,
            "head",
            [
                new FolderProjectUnrecordedChange(
                    "conflict.txt",
                    FolderProjectUnrecordedChangeKind.Modified),
            ]);
        var history = new Mock<IFolderProjectHistoryService>();
        history.SetupSequence(item => item.GetDisplayStatus(projectRoot))
            .Returns(recoveryStatus)
            .Returns(readyStatus);
        history.Setup(item => item.GetRestorePoints(
                projectRoot,
                100,
                It.IsAny<Action<FolderProjectHistoryProgress>>() ))
            .Returns([]);
        var recoveryOperation = new FolderProjectRecoveryOperation(
            readyStatus,
            null!);
        history.Setup(item => item.BeginRecoverToSafeState(
                projectRoot,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(recoveryOperation);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowYesNoBox(
                It.Is<string>(message => message.Contains("保留当前磁盘文件")),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK);
        var coordinator = new Mock<
            IFolderProjectGitOperationCoordinator>();
        coordinator.Setup(item => item.ExecuteTransactionalAsync(
                projectRoot,
                It.IsAny<Func<FolderProjectRecoveryOperation>>(),
                It.IsAny<Action<FolderProjectRecoveryOperation>>(),
                It.IsAny<Action<FolderProjectRecoveryOperation>>(),
                true))
            .Returns<string, Func<FolderProjectRecoveryOperation>,
                Action<FolderProjectRecoveryOperation>,
                Action<FolderProjectRecoveryOperation>, bool>(
                (_, begin, complete, _, _) =>
                {
                    var operation = begin();
                    complete(operation);
                    return Task.FromResult(operation);
                });
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object,
            coordinator: coordinator.Object);
        var completed = 0;
        viewModel.RecoveryCompleted += (_, _) => completed++;
        viewModel.OpenRecoveryProject(projectRoot, "旧工程");
        await viewModel.RefreshCommand.ExecuteAsync(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.IsRecoveryRequired, Is.True);
            NUnitAssert.That(viewModel.IsReady, Is.False);
            NUnitAssert.That(
                viewModel.CreateRestorePointCommand.CanExecute(null),
                Is.False);
            NUnitAssert.That(
                viewModel.RecoverHistoryCommand.CanExecute(null),
                Is.True);
        });

        await viewModel.RecoverHistoryCommand.ExecuteAsync(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.IsReady, Is.True);
            NUnitAssert.That(viewModel.IsRecoveryRequired, Is.False);
            NUnitAssert.That(viewModel.UnrecordedChanges,
                Has.One.Property("Path").EqualTo("conflict.txt"));
            NUnitAssert.That(completed, Is.EqualTo(1));
        });
        history.Verify(item => item.BeginRecoverToSafeState(
            projectRoot,
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Once);
    }

    [Test]
    public void OpenRecoveryProject_ImmediatelyHidesNormalHistoryActions()
    {
        var viewModel = CreateViewModel(
            Mock.Of<IFolderProjectHistoryService>());

        viewModel.OpenRecoveryProject(
            Path.GetFullPath("legacy-project"),
            "旧工程");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.IsRecoveryRequired, Is.True);
            NUnitAssert.That(viewModel.CanRecover, Is.False);
            NUnitAssert.That(
                viewModel.RecoverHistoryCommand.CanExecute(null),
                Is.False);
        });
    }

    [Test]
    public async Task RecoverHistory_CancelLeavesRecoveryStateUntouched()
    {
        var projectRoot = Path.GetFullPath("legacy-project");
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(item => item.GetDisplayStatus(projectRoot))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.RecoveryRequired,
                "head",
                [])
            {
                RecoveryReason =
                    FolderProjectHistoryRecoveryReason.DetachedHistory,
                CanRecover = true,
            });
        history.Setup(item => item.GetRestorePoints(
                projectRoot,
                100,
                It.IsAny<Action<FolderProjectHistoryProgress>>() ))
            .Returns([]);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.Cancel);
        var coordinator = new Mock<
            IFolderProjectGitOperationCoordinator>(MockBehavior.Strict);
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object,
            coordinator: coordinator.Object);
        viewModel.OpenRecoveryProject(projectRoot, "旧工程");
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.RecoverHistoryCommand.ExecuteAsync(null);

        NUnitAssert.That(viewModel.IsRecoveryRequired, Is.True);
        history.Verify(item => item.BeginRecoverToSafeState(
            It.IsAny<string>(),
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Never);
    }

    [Test]
    public async Task RecoverHistory_ReloadFailureRollsBackAndKeepsRecoveryOpen()
    {
        var projectRoot = Path.GetFullPath("legacy-project");
        var recoveryStatus = new FolderProjectHistoryStatus(
            FolderProjectHistoryAvailability.RecoveryRequired,
            "head",
            [])
        {
            RecoveryReason =
                FolderProjectHistoryRecoveryReason.UnfinishedOperation,
            CanRecover = true,
        };
        var recoveryOperation = new FolderProjectRecoveryOperation(
            recoveryStatus,
            null!);
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(item => item.GetDisplayStatus(projectRoot))
            .Returns(recoveryStatus);
        history.Setup(item => item.GetRestorePoints(
                projectRoot,
                100,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns([]);
        history.Setup(item => item.BeginRecoverToSafeState(
                projectRoot,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(recoveryOperation);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK);
        var coordinator = new Mock<IFolderProjectGitOperationCoordinator>();
        coordinator.Setup(item => item.ExecuteTransactionalAsync(
                projectRoot,
                It.IsAny<Func<FolderProjectRecoveryOperation>>(),
                It.IsAny<Action<FolderProjectRecoveryOperation>>(),
                It.IsAny<Action<FolderProjectRecoveryOperation>>(),
                true))
            .Returns<string, Func<FolderProjectRecoveryOperation>,
                Action<FolderProjectRecoveryOperation>,
                Action<FolderProjectRecoveryOperation>, bool>(
                (_, begin, _, rollback, _) =>
                {
                    var operation = begin();
                    rollback(operation);
                    throw new IOException("Injected reload failure.");
                });
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object,
            coordinator: coordinator.Object);
        var completed = 0;
        viewModel.RecoveryCompleted += (_, _) => completed++;
        viewModel.OpenRecoveryProject(projectRoot, "旧工程");
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.RecoverHistoryCommand.ExecuteAsync(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(viewModel.IsRecoveryRequired, Is.True);
            NUnitAssert.That(completed, Is.Zero);
        });
        history.Verify(item => item.RollbackRecoverToSafeState(
            recoveryOperation), Times.Once);
        history.Verify(item => item.CompleteRecoverToSafeState(
            It.IsAny<FolderProjectRecoveryOperation>()), Times.Never);
        dialogs.Verify(item => item.ShowExceptionWindow(
            It.IsAny<IOException>(),
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task CreateRestorePoint_SaveChoiceSavesEditorsAndRecordsDisk()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var history = CreateHistoryService(project.ProjectRoot);
        var unsaved = new Mock<IFolderProjectUnsavedChangesService>();
        unsaved.Setup(item => item.HasUnsavedChanges(
                project.ProjectRoot,
                null))
            .Returns(true);
        unsaved.Setup(item => item.SaveUnsavedChanges(
                project.ProjectRoot,
                null))
            .Returns(true);
        var prompt = new Mock<IFolderProjectUnsavedChangesPrompt>();
        prompt.Setup(item => item.Show())
            .Returns(FolderProjectUnsavedChangesChoice.Save);
        var viewModel = CreateViewModel(
            history.Object,
            unsaved.Object,
            prompt.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.CreateRestorePointCommand.ExecuteAsync(null);

        unsaved.Verify(item => item.SaveUnsavedChanges(
            project.ProjectRoot,
            null), Times.Once);
        history.Verify(item => item.CreateRestorePoint(
            project.ProjectRoot,
            "记录工程当前状态",
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Once);
    }

    [Test]
    public async Task CleanDisk_StillAllowsSavingUnsavedEditorIntoRestorePoint()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var history = CreateHistoryService(project.ProjectRoot);
        history.Setup(item => item.GetDisplayStatus(project.ProjectRoot))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                []));
        var unsaved = new Mock<IFolderProjectUnsavedChangesService>();
        unsaved.Setup(item => item.HasUnsavedChanges(
                project.ProjectRoot,
                null))
            .Returns(true);
        unsaved.Setup(item => item.SaveUnsavedChanges(
                project.ProjectRoot,
                null))
            .Returns(true);
        var prompt = new Mock<IFolderProjectUnsavedChangesPrompt>();
        prompt.Setup(item => item.Show())
            .Returns(FolderProjectUnsavedChangesChoice.Save);
        var viewModel = CreateViewModel(
            history.Object,
            unsaved.Object,
            prompt.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        NUnitAssert.That(
            viewModel.CreateRestorePointCommand.CanExecute(null),
            Is.True);

        await viewModel.CreateRestorePointCommand.ExecuteAsync(null);

        unsaved.Verify(item => item.SaveUnsavedChanges(
            project.ProjectRoot,
            null), Times.Once);
        history.Verify(item => item.CreateRestorePoint(
            project.ProjectRoot,
            "记录工程当前状态",
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Once);
    }

    [Test]
    public async Task CreateRestorePoint_DiskOnlyChoiceDoesNotSaveEditors()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var history = CreateHistoryService(project.ProjectRoot);
        var unsaved = new Mock<IFolderProjectUnsavedChangesService>();
        unsaved.Setup(item => item.HasUnsavedChanges(
                project.ProjectRoot,
                null))
            .Returns(true);
        var prompt = new Mock<IFolderProjectUnsavedChangesPrompt>();
        prompt.Setup(item => item.Show())
            .Returns(FolderProjectUnsavedChangesChoice.DontSave);
        var viewModel = CreateViewModel(
            history.Object,
            unsaved.Object,
            prompt.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.CreateRestorePointCommand.ExecuteAsync(null);

        unsaved.Verify(item => item.SaveUnsavedChanges(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyCollection<string>?>()), Times.Never);
        history.Verify(item => item.CreateRestorePoint(
            project.ProjectRoot,
            It.IsAny<string>(),
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Once);
    }

    [Test]
    public async Task CreateRestorePoint_CancelChoiceDoesNothing()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var history = CreateHistoryService(project.ProjectRoot);
        var unsaved = new Mock<IFolderProjectUnsavedChangesService>();
        unsaved.Setup(item => item.HasUnsavedChanges(
                project.ProjectRoot,
                null))
            .Returns(true);
        var prompt = new Mock<IFolderProjectUnsavedChangesPrompt>();
        prompt.Setup(item => item.Show())
            .Returns(FolderProjectUnsavedChangesChoice.Cancel);
        var viewModel = CreateViewModel(
            history.Object,
            unsaved.Object,
            prompt.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.CreateRestorePointCommand.ExecuteAsync(null);

        history.Verify(item => item.CreateRestorePoint(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Never);
    }

    [Test]
    public async Task SelectingRestorePoint_LoadsItsChangesOnDemand()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var point = RestorePoint("head", "调整单位数据");
        var history = CreateHistoryService(project.ProjectRoot, [point]);
        history.Setup(item => item.GetRestorePointChanges(
                project.ProjectRoot,
                point.Id,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns([
                new FolderProjectRestorePointChange(
                    "db\\units_tables\\changed.bin",
                    null,
                    FolderProjectRestorePointChangeKind.Modified,
                    true),
            ]);
        var viewModel = CreateViewModel(history.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SelectedRestorePoint = point;
        await viewModel.SelectedChangesLoadTask;

        NUnitAssert.That(
            viewModel.SelectedRestorePointChanges,
            Has.Count.EqualTo(1));
        history.Verify(item => item.GetRestorePointChanges(
            project.ProjectRoot,
            point.Id,
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Once);
    }

    [Test]
    public async Task SelectingRestorePoint_ReusesPreviouslyLoadedChanges()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var initial = RestorePoint("initial", "初始还原点", true);
        var later = RestorePoint("later", "后续还原点");
        var history = CreateHistoryService(
            project.ProjectRoot,
            [later, initial]);
        history.Setup(item => item.GetRestorePointChanges(
                project.ProjectRoot,
                It.IsAny<string>(),
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns([]);
        var viewModel = CreateViewModel(history.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SelectedRestorePoint = initial;
        await viewModel.SelectedChangesLoadTask;
        viewModel.SelectedRestorePoint = later;
        await viewModel.SelectedChangesLoadTask;
        viewModel.SelectedRestorePoint = initial;
        await viewModel.SelectedChangesLoadTask;

        history.Verify(item => item.GetRestorePointChanges(
            project.ProjectRoot,
            initial.Id,
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Once);
    }

    [Test]
    public async Task SelectingRestorePoints_OutOfOrderCompletionKeepsLatest()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var first = RestorePoint("first", "第一项");
        var second = RestorePoint("second", "第二项");
        var firstStarted = new ManualResetEventSlim();
        var releaseFirst = new ManualResetEventSlim();
        var history = CreateHistoryService(
            project.ProjectRoot,
            [first, second]);
        history.Setup(item => item.GetRestorePointChanges(
                project.ProjectRoot,
                first.Id,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(() =>
            {
                firstStarted.Set();
                releaseFirst.Wait();
                return
                [
                    new FolderProjectRestorePointChange(
                        "first.bin",
                        null,
                        FolderProjectRestorePointChangeKind.Modified,
                        true),
                ];
            });
        history.Setup(item => item.GetRestorePointChanges(
                project.ProjectRoot,
                second.Id,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns([
                new FolderProjectRestorePointChange(
                    "second.bin",
                    null,
                    FolderProjectRestorePointChangeKind.Added,
                    true),
            ]);
        var viewModel = CreateViewModel(history.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SelectedRestorePoint = first;
        var firstLoad = viewModel.SelectedChangesLoadTask;
        NUnitAssert.That(firstStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
        viewModel.SelectedRestorePoint = second;
        var secondLoad = viewModel.SelectedChangesLoadTask;
        await secondLoad;
        releaseFirst.Set();
        await firstLoad;

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                viewModel.SelectedRestorePoint?.Id,
                Is.EqualTo(second.Id));
            NUnitAssert.That(
                viewModel.SelectedRestorePointChanges.Single().Path,
                Is.EqualTo("second.bin"));
        });
    }

    [Test]
    public async Task RestoreProject_CancelDoesNotCallHistoryOrCoordinator()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var point = RestorePoint("target", "目标状态");
        var history = CreateHistoryService(project.ProjectRoot, [point]);
        history.Setup(item => item.GetRestoreImpactCount(
                project.ProjectRoot,
                point.Id))
            .Returns(1);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.Cancel);
        var coordinator = new Mock<IFolderProjectGitOperationCoordinator>();
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object,
            coordinator: coordinator.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedRestorePoint = point;

        await viewModel.RestoreProjectCommand.ExecuteAsync(null);

        history.Verify(item => item.RestoreProject(
            It.IsAny<string>(),
            It.IsAny<FolderProjectRestorePoint>(),
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Never);
        coordinator.VerifyNoOtherCalls();
        NUnitAssert.That(viewModel.SelectedRestorePoint, Is.SameAs(point));
    }

    [Test]
    public async Task RestoreProject_ConfirmsScopeAndUsesCoordinator()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var point = RestorePoint("target", "目标状态");
        var history = CreateHistoryService(project.ProjectRoot, [point]);
        history.Setup(item => item.RestoreProject(
                project.ProjectRoot,
                point,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(new FolderProjectRestoreResult(point, null));
        history.Setup(item => item.GetRestoreImpactCount(
                project.ProjectRoot,
                point.Id))
            .Returns(1);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK);
        var coordinator = new Mock<IFolderProjectGitOperationCoordinator>();
        coordinator.Setup(item => item.ExecuteTransactionalAsync(
                project.ProjectRoot,
                It.IsAny<Func<FolderProjectRestoreResult>>(),
                It.IsAny<Action<FolderProjectRestoreResult>>(),
                It.IsAny<Action<FolderProjectRestoreResult>>(),
                false))
            .Returns<string, Func<FolderProjectRestoreResult>,
                Action<FolderProjectRestoreResult>,
                Action<FolderProjectRestoreResult>, bool>(
                (_, operation, complete, _, _) =>
                {
                    var result = operation();
                    complete(result);
                    return Task.FromResult(result);
                });
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object,
            coordinator: coordinator.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedRestorePoint = point;

        await viewModel.RestoreProjectCommand.ExecuteAsync(null);

        dialogs.Verify(item => item.ShowYesNoBox(
            It.Is<string>(message =>
                message.Contains("目标状态") &&
                message.Contains("1") &&
                message.Contains("重新加载")),
            It.IsAny<string>()), Times.Once);
        coordinator.Verify(item => item.ExecuteTransactionalAsync(
            project.ProjectRoot,
            It.IsAny<Func<FolderProjectRestoreResult>>(),
            It.IsAny<Action<FolderProjectRestoreResult>>(),
            It.IsAny<Action<FolderProjectRestoreResult>>(),
            false), Times.Once);
        history.Verify(item => item.RestoreProject(
            project.ProjectRoot,
            point,
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Once);
    }

    [Test]
    public async Task RestoreFile_DirtyTargetRequiresSecondConfirmation()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var point = RestorePoint("target", "目标状态");
        var change = new FolderProjectRestorePointChange(
            "changed.bin",
            null,
            FolderProjectRestorePointChangeKind.Modified,
            true);
        var history = CreateHistoryService(project.ProjectRoot, [point]);
        history.Setup(item => item.GetRestorePointChanges(
                project.ProjectRoot,
                point.Id,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns([change]);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.SetupSequence(item => item.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK)
            .Returns(ShowMessageBoxResult.OK);
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedRestorePoint = point;
        await viewModel.SelectedChangesLoadTask;
        viewModel.SelectedRestorePointChange = change;

        await viewModel.RestoreFileCommand.ExecuteAsync(null);

        dialogs.Verify(item => item.ShowYesNoBox(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Exactly(2));
        history.Verify(item => item.BeginRestoreFile(
            project.ProjectRoot,
            point.Id,
            change.Path,
            true), Times.Once);
    }

    [Test]
    public async Task RestoreDeletedFile_UsesPreviousRestorePointVersion()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var previousId = new string('1', 40);
        var point = RestorePoint(
            new string('2', 40),
            "删除文件",
            previousRestorePointId: previousId);
        var change = new FolderProjectRestorePointChange(
            "deleted.bin",
            null,
            FolderProjectRestorePointChangeKind.Deleted,
            true);
        var history = CreateHistoryService(project.ProjectRoot, [point]);
        history.Setup(item => item.GetRestorePointChanges(
                project.ProjectRoot,
                point.Id,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns([change]);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK);
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedRestorePoint = point;
        await viewModel.SelectedChangesLoadTask;
        viewModel.SelectedRestorePointChange = change;

        await viewModel.RestoreFileCommand.ExecuteAsync(null);

        history.Verify(item => item.BeginRestoreFile(
            project.ProjectRoot,
            previousId,
            change.Path,
            false), Times.Once);
    }

    [Test]
    public async Task RestoreFile_RollbackFailure_UsesCoordinatorIsolation()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var point = RestorePoint("target", "目标状态");
        var change = new FolderProjectRestorePointChange(
            "changed.bin",
            null,
            FolderProjectRestorePointChangeKind.Modified,
            true);
        var history = CreateHistoryService(project.ProjectRoot, [point]);
        history.Setup(item => item.GetRestorePointChanges(
                project.ProjectRoot,
                point.Id,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns([change]);
        var coordinator = new Mock<IFolderProjectGitOperationCoordinator>();
        coordinator.Setup(item => item.ExecuteInPlaceTransactionalAsync(
                project.ProjectRoot,
                It.IsAny<Func<FolderProjectFileRestoreOperation>>(),
                It.IsAny<Action<FolderProjectFileRestoreOperation>>(),
                It.IsAny<Action<FolderProjectFileRestoreOperation>>()))
            .ThrowsAsync(new FolderProjectGitHostException(
                "rollback failed",
                new IOException("rollback failed")));
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK);
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object,
            coordinator: coordinator.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedRestorePoint = point;
        await viewModel.SelectedChangesLoadTask;
        viewModel.SelectedRestorePointChange = change;

        await viewModel.RestoreFileCommand.ExecuteAsync(null);

        coordinator.Verify(item => item.ExecuteInPlaceTransactionalAsync(
            project.ProjectRoot,
            It.IsAny<Func<FolderProjectFileRestoreOperation>>(),
            It.IsAny<Action<FolderProjectFileRestoreOperation>>(),
            It.IsAny<Action<FolderProjectFileRestoreOperation>>()), Times.Once);
        dialogs.Verify(item => item.ShowExceptionWindow(
            It.IsAny<FolderProjectGitHostException>(),
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task DiscardSelected_ReconcileFailureRollsBackBeforeCleanup()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var history = CreateHistoryService(project.ProjectRoot);
        var discard = new FolderProjectDiscardResult(
            new FolderProjectDiscardRollback(
                "staging", [], [], [],
                new FolderProjectIndexSnapshot(
                    false, [], FileAttributes.Normal)));
        history.Setup(item => item.BeginDiscardChanges(
                project.ProjectRoot,
                new[] { "changed.bin" },
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Callback(() => Directory.Delete(project.ProjectRoot, true))
            .Returns(discard);
        history.Setup(item => item.RollbackDiscardChanges(
                project.ProjectRoot,
                discard))
            .Callback(() =>
            {
                Directory.CreateDirectory(project.ProjectRoot);
                project.ProjectSettings.Save(project.ProjectRoot);
            });
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK);
        var coordinator = new Mock<IFolderProjectGitOperationCoordinator>();
        coordinator.Setup(item => item.ExecuteInPlaceTransactionalAsync(
                project.ProjectRoot,
                It.IsAny<Func<FolderProjectDiscardResult>>(),
                It.IsAny<Action<FolderProjectDiscardResult>>(),
                It.IsAny<Action<FolderProjectDiscardResult>>()))
            .Returns<string, Func<FolderProjectDiscardResult>,
                Action<FolderProjectDiscardResult>,
                Action<FolderProjectDiscardResult>>(
                (_, operation, _, rollback) =>
                {
                    var result = operation();
                    rollback(result);
                    throw new FolderProjectGitHostException(
                        "reopen failed",
                        new DirectoryNotFoundException());
                });
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object,
            coordinator: coordinator.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedUnrecordedChange =
            viewModel.UnrecordedChanges.Single();

        await viewModel.DiscardSelectedCommand.ExecuteAsync(null);

        history.Verify(item => item.RollbackDiscardChanges(
            project.ProjectRoot,
            discard), Times.Once);
        history.Verify(item => item.CompleteDiscardChanges(
            It.IsAny<FolderProjectDiscardResult>()), Times.Never);
        dialogs.Verify(item => item.ShowExceptionWindow(
            It.IsAny<Exception>(),
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task DiscardAll_ConfirmsAndUsesCoordinator()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var history = CreateHistoryService(project.ProjectRoot);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK);
        var coordinator = new Mock<IFolderProjectGitOperationCoordinator>();
        var discard = new FolderProjectDiscardResult(
            new FolderProjectDiscardRollback(
                "staging", [], [], [],
                new FolderProjectIndexSnapshot(
                    false, [], FileAttributes.Normal)));
        history.Setup(item => item.BeginDiscardChanges(
                project.ProjectRoot,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(discard);
        coordinator.Setup(item => item.ExecuteTransactionalAsync(
                project.ProjectRoot,
                It.IsAny<Func<FolderProjectDiscardResult>>(),
                It.IsAny<Action<FolderProjectDiscardResult>>(),
                It.IsAny<Action<FolderProjectDiscardResult>>(),
                false))
            .Returns<string, Func<FolderProjectDiscardResult>,
                Action<FolderProjectDiscardResult>,
                Action<FolderProjectDiscardResult>, bool>(
                (_, operation, complete, _, _) =>
                {
                    var result = operation();
                    complete(result);
                    return Task.FromResult(result);
                });
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object,
            coordinator: coordinator.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.DiscardAllCommand.ExecuteAsync(null);

        coordinator.Verify(item => item.ExecuteTransactionalAsync(
            project.ProjectRoot,
            It.IsAny<Func<FolderProjectDiscardResult>>(),
            It.IsAny<Action<FolderProjectDiscardResult>>(),
            It.IsAny<Action<FolderProjectDiscardResult>>(),
            false), Times.Once);
        history.Verify(item => item.BeginDiscardChanges(
            project.ProjectRoot,
            new[] { "changed.bin" },
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Once);
    }

    [Test]
    public async Task DiscardAll_ConfirmsFreshDiskStatusAndUsesOnlyConfirmedPaths()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var history = CreateHistoryService(project.ProjectRoot);
        history.SetupSequence(item => item.GetDisplayStatus(project.ProjectRoot))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                [
                    new FolderProjectUnrecordedChange(
                        "cached.bin",
                        FolderProjectUnrecordedChangeKind.Modified),
                ]))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                []));
        history.Setup(item => item.GetStatus(
                project.ProjectRoot,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                [
                    new FolderProjectUnrecordedChange(
                        "cached.bin",
                        FolderProjectUnrecordedChangeKind.Modified),
                    new FolderProjectUnrecordedChange(
                        "confirmed.bin",
                        FolderProjectUnrecordedChangeKind.Added),
                ]));
        var discard = new FolderProjectDiscardResult(
            new FolderProjectDiscardRollback(
                "staging", [], [], [],
                new FolderProjectIndexSnapshot(
                    false, [], FileAttributes.Normal)));
        history.Setup(item => item.BeginDiscardChanges(
                project.ProjectRoot,
                new[] { "cached.bin", "confirmed.bin" },
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(discard);
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK);
        var coordinator = new Mock<IFolderProjectGitOperationCoordinator>();
        coordinator.Setup(item => item.ExecuteTransactionalAsync(
                project.ProjectRoot,
                It.IsAny<Func<FolderProjectDiscardResult>>(),
                It.IsAny<Action<FolderProjectDiscardResult>>(),
                It.IsAny<Action<FolderProjectDiscardResult>>(),
                false))
            .Returns<string, Func<FolderProjectDiscardResult>,
                Action<FolderProjectDiscardResult>,
                Action<FolderProjectDiscardResult>, bool>(
                (_, operation, complete, _, _) =>
                {
                    var result = operation();
                    complete(result);
                    return Task.FromResult(result);
                });
        var viewModel = CreateViewModel(
            history.Object,
            dialogs: dialogs.Object,
            coordinator: coordinator.Object);
        viewModel.OpenProject(project);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        await viewModel.DiscardAllCommand.ExecuteAsync(null);

        dialogs.Verify(item => item.ShowYesNoBox(
            It.Is<string>(message => message.Contains("2")),
            It.IsAny<string>()), Times.Once);
        history.Verify(item => item.BeginDiscardChanges(
            project.ProjectRoot,
            new[] { "cached.bin", "confirmed.bin" },
            It.IsAny<Action<FolderProjectHistoryProgress>>()), Times.Once);
    }

    [Test]
    public void HistoryView_UsesSharedComponentsAndHidesAdvancedGitConcepts()
    {
        var path = Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Views",
            "FolderProjectHistory",
            "FolderProjectHistoryView.xaml");
        var document = XDocument.Load(path);
        var source = File.ReadAllText(path);
        var mainWindowSource = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Views",
            "MainWindow.xaml"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                document.Descendants()
                    .Any(item => item.Name.LocalName ==
                                 "OperationProgressWindowHost"),
                Is.True);
            NUnitAssert.That(source, Does.Contain("AeButton.Primary"));
            NUnitAssert.That(source, Does.Contain("AeButton.Secondary"));
            NUnitAssert.That(source, Does.Contain("AeButton.Danger"));
            NUnitAssert.That(source, Does.Contain("AeInput.TextBox"));
            NUnitAssert.That(
                source,
                Does.Contain("x:Name=\"RestorePointDescriptionHint\""));
            NUnitAssert.That(
                source,
                Does.Contain("Foreground=\"{DynamicResource AeBrush.TextMuted}\""));
            NUnitAssert.That(
                source,
                Does.Contain("FolderProject.History.DefaultDescription"));
            NUnitAssert.That(source, Does.Contain("AeList.View"));
            NUnitAssert.That(
                source,
                Does.Contain("LoadedProjectText"));
            NUnitAssert.That(
                source,
                Does.Not.Contain("FolderProject.History.Title"));
            NUnitAssert.That(
                source,
                Does.Not.Contain("FolderProject.History.SameDiskNotice"));
            NUnitAssert.That(
                mainWindowSource,
                Does.Contain(
                    "ToolTip=\"{loc:Loc FolderProject.History.SameDiskNotice}\""));
            foreach (var forbidden in new[]
                     {
                         "Git", "Stage", "Commit", "Branch", "Identity",
                         "暂存", "提交", "分支", "身份",
                     })
            {
                NUnitAssert.That(
                    source,
                    Does.Not.Contain(forbidden),
                    forbidden);
            }
        });
    }

    [Test]
    [NonParallelizable]
    public void HistoryView_RendersAtSidebarWidth()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var viewModel = CreateViewModel(
                    CreateHistoryService("project").Object);
                viewModel.RestorePoints.Add(
                    RestorePoint("initial", "初始还原点", true));
                var view = new FolderProjectHistoryView
                {
                    DataContext = viewModel,
                };
                var window = new Window
                {
                    Width = 360,
                    Height = 700,
                    Content = view,
                    WindowStyle = WindowStyle.None,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var dpi = VisualTreeHelper.GetDpi(window);
                    var bitmap = new RenderTargetBitmap(
                        Math.Max(
                            1,
                            (int)Math.Ceiling(
                                window.ActualWidth * dpi.DpiScaleX)),
                        Math.Max(
                            1,
                            (int)Math.Ceiling(
                                window.ActualHeight * dpi.DpiScaleY)),
                        dpi.PixelsPerInchX,
                        dpi.PixelsPerInchY,
                        PixelFormats.Pbgra32);
                    bitmap.Render(window);

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            bitmap.PixelWidth,
                            Is.GreaterThan(0));
                        NUnitAssert.That(
                            bitmap.PixelHeight,
                            Is.GreaterThan(0));
                        NUnitAssert.That(
                            FindDescendants<Button>(view)
                                .Select(button => button.Content?.ToString()),
                            Does.Contain("创建还原点"));
                        NUnitAssert.That(
                            FindDescendants<Button>(view)
                                .Select(button => button.Content?.ToString()),
                            Does.Contain("恢复整个工程"));
                        NUnitAssert.That(
                            FindDescendants<TextBlock>(view)
                                .Select(text => text.Text),
                            Does.Contain("当前加载工程："));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    [NonParallelizable]
    public void HistoryView_RendersRecoveryActionWithoutAdvancedConcepts()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var viewModel = CreateViewModel(
                    CreateHistoryService("project").Object);
                viewModel.OpenRecoveryProject(
                    Path.GetFullPath("project"),
                    "旧工程");
                viewModel.IsRecoveryRequired = true;
                viewModel.CanRecover = true;
                viewModel.RecoveryText =
                    "恢复会保留当前磁盘内容，冲突内容将作为未记录修改保留。";
                var view = new FolderProjectHistoryView
                {
                    DataContext = viewModel,
                };
                var window = new Window
                {
                    Width = 500,
                    Height = 760,
                    Content = view,
                    WindowStyle = WindowStyle.None,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            FindDescendants<Button>(view)
                                .Where(button => button.IsVisible)
                                .Select(button => button.Content?.ToString()),
                            Is.EqualTo(new[]
                            {
                                "保留当前文件并恢复工程",
                            }));
                        NUnitAssert.That(
                            FindDescendants<TextBlock>(view)
                                .Select(text => text.Text),
                            Does.Contain(viewModel.RecoveryText));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private static FolderProjectHistoryViewModel CreateViewModel(
        IFolderProjectHistoryService history,
        IFolderProjectUnsavedChangesService? unsaved = null,
        IFolderProjectUnsavedChangesPrompt? prompt = null,
        IStandardDialogs? dialogs = null,
        IFolderProjectGitOperationCoordinator? coordinator = null) =>
        new(
            history,
            unsaved ?? Mock.Of<IFolderProjectUnsavedChangesService>(),
            prompt ?? Mock.Of<IFolderProjectUnsavedChangesPrompt>(),
            coordinator ?? CreateImmediateCoordinator(),
            dialogs ?? Mock.Of<IStandardDialogs>(),
            LocalizationManager.Instance);

    private static IFolderProjectGitOperationCoordinator
        CreateImmediateCoordinator()
    {
        var coordinator = new Mock<IFolderProjectGitOperationCoordinator>();
        coordinator.Setup(item => item.ExecuteInPlaceTransactionalAsync(
                It.IsAny<string>(),
                It.IsAny<Func<FolderProjectFileRestoreOperation>>(),
                It.IsAny<Action<FolderProjectFileRestoreOperation>>(),
                It.IsAny<Action<FolderProjectFileRestoreOperation>>()))
            .Returns<string, Func<FolderProjectFileRestoreOperation>,
                Action<FolderProjectFileRestoreOperation>,
                Action<FolderProjectFileRestoreOperation>>(
                (_, operation, complete, rollback) =>
                    ExecuteImmediate(operation, complete, rollback));
        coordinator.Setup(item => item.ExecuteInPlaceTransactionalAsync(
                It.IsAny<string>(),
                It.IsAny<Func<FolderProjectDiscardResult>>(),
                It.IsAny<Action<FolderProjectDiscardResult>>(),
                It.IsAny<Action<FolderProjectDiscardResult>>()))
            .Returns<string, Func<FolderProjectDiscardResult>,
                Action<FolderProjectDiscardResult>,
                Action<FolderProjectDiscardResult>>(
                (_, operation, complete, rollback) =>
                    ExecuteImmediate(operation, complete, rollback));
        return coordinator.Object;

        static Task<T> ExecuteImmediate<T>(
            Func<T> operation,
            Action<T> complete,
            Action<T> rollback)
        {
            var result = operation();
            try
            {
                complete(result);
            }
            catch
            {
                rollback(result);
                throw;
            }
            return Task.FromResult(result);
        }
    }

    private static Mock<IFolderProjectHistoryService> CreateHistoryService(
        string projectRoot,
        IReadOnlyList<FolderProjectRestorePoint>? restorePoints = null)
    {
        var history = new Mock<IFolderProjectHistoryService>();
        var status = new FolderProjectHistoryStatus(
            FolderProjectHistoryAvailability.Ready,
            "head",
            [
                new FolderProjectUnrecordedChange(
                    "changed.bin",
                    FolderProjectUnrecordedChangeKind.Modified),
            ]);
        history.Setup(item => item.GetDisplayStatus(projectRoot))
            .Returns(status);
        history.Setup(item => item.GetStatus(
                projectRoot,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(status);
        history.Setup(item => item.GetRestorePoints(
                projectRoot,
                100,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(restorePoints ?? [RestorePoint("head", "现有还原点")]);
        history.Setup(item => item.CreateRestorePoint(
                projectRoot,
                It.IsAny<string>(),
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(RestorePoint("new", "记录工程当前状态"));
        return history;
    }

    private static FolderProjectContainer CreateProject(string path) =>
        FolderProjectContainer.Create(
            path,
            new FolderProjectSettings { Name = "测试工程" });

    private static FolderProjectRestorePoint RestorePoint(
        string id,
        string description,
        bool initial = false,
        string? previousRestorePointId = null) =>
        new(
            id,
            description,
            DateTimeOffset.Parse("2026-08-13T08:00:00+08:00"),
            new FolderProjectRestorePointChangeSummary(1, 0, 0, 0, 0),
            initial)
        {
            PreviousRestorePointId = previousRestorePointId,
        };

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "AssetEditor.CN.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private static IEnumerable<T> FindDescendants<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AssetEditorHistoryVmTests-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, true);
    }
}
