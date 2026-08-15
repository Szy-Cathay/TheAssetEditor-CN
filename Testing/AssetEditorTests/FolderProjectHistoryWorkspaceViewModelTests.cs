using AssetEditor.Services;
using AssetEditor.ViewModels;
using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace AssetEditorTests;

public sealed class FolderProjectHistoryWorkspaceViewModelTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public async Task FolderProject_OpensHistoryAndRegularPackDisablesIt()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var historyService = new Mock<IFolderProjectHistoryService>();
        historyService.Setup(item =>
                item.GetDisplayStatus(project.ProjectRoot))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                []));
        historyService.Setup(item => item.GetRestorePoints(
                project.ProjectRoot,
                100,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns([]);
        var history = new FolderProjectHistoryViewModel(
            historyService.Object,
            Mock.Of<IFolderProjectUnsavedChangesService>(),
            Mock.Of<IFolderProjectUnsavedChangesPrompt>(),
            Mock.Of<IFolderProjectGitOperationCoordinator>(),
            Mock.Of<IStandardDialogs>(),
            LocalizationManager.Instance);
        var eventHub = new TestEventHub();
        var workspace = new FolderProjectHistoryWorkspaceViewModel(
            history,
            eventHub);

        workspace.SetEditableContainer(project);
        workspace.ShowHistory();
        await history.RefreshCommand.ExecutionTask!;

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(workspace.IsEnabled, Is.True);
            NUnitAssert.That(workspace.SelectedSidebarTabIndex, Is.EqualTo(1));
            NUnitAssert.That(history.ProjectName, Is.EqualTo("测试工程"));
            NUnitAssert.That(history.IsReady, Is.True);
        });

        workspace.SelectedSidebarTabIndex = 0;
        workspace.SelectedSidebarTabIndex = 1;
        await history.RefreshCommand.ExecutionTask!;

        historyService.Verify(item =>
            item.GetDisplayStatus(project.ProjectRoot), Times.Once);

        workspace.SelectedSidebarTabIndex = 0;
        eventHub.Publish(new FolderProjectChangedEvent(
            project,
            new FolderProjectChangeSet(1, [])));
        workspace.SelectedSidebarTabIndex = 1;
        await history.RefreshCommand.ExecutionTask!;

        historyService.Verify(item =>
            item.GetDisplayStatus(project.ProjectRoot),
            Times.Exactly(2));

        workspace.SetEditableContainer(new PackFileContainer("普通.pack"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(workspace.IsEnabled, Is.False);
            NUnitAssert.That(workspace.SelectedSidebarTabIndex, Is.Zero);
        });
    }

    [Test]
    public void InternalDetach_KeepsBusyHistoryVisible()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var history = new FolderProjectHistoryViewModel(
            Mock.Of<IFolderProjectHistoryService>(),
            Mock.Of<IFolderProjectUnsavedChangesService>(),
            Mock.Of<IFolderProjectUnsavedChangesPrompt>(),
            Mock.Of<IFolderProjectGitOperationCoordinator>(),
            Mock.Of<IStandardDialogs>(),
            LocalizationManager.Instance);
        var workspace = new FolderProjectHistoryWorkspaceViewModel(
            history,
            new TestEventHub());
        workspace.SetEditableContainer(project);
        workspace.ShowHistory();
        history.IsBusy = true;

        workspace.SetEditableContainer(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(workspace.IsEnabled, Is.True);
            NUnitAssert.That(workspace.SelectedSidebarTabIndex, Is.EqualTo(1));
            NUnitAssert.That(history.ProjectName, Is.EqualTo("测试工程"));
        });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-history-workspace-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
