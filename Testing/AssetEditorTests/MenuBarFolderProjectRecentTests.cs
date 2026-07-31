using AssetEditor.Services;
using AssetEditor.UiCommands;
using AssetEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;

namespace AssetEditorTests;

public class MenuBarFolderProjectRecentTests
{
    [Test]
    public void RecentFolderProject_UsesUnifiedOpenService()
    {
        const string projectPath = @"D:\projects\recent";
        var settingsService = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        settingsService.CurrentSettings.RecentFolderProjectPaths.Add(
            projectPath);
        var openService = new Mock<IFolderProjectOpenService>();
        var viewModel = CreateViewModel(
            Mock.Of<IPackFileService>(),
            settingsService,
            Mock.Of<IUiCommandFactory>(),
            openService.Object,
            new TestEventHub());

        viewModel.RecentFolderProjects.Single().Command.Execute(null);

        openService.Verify(
            service => service.Open(projectPath),
            Times.Once);
    }

    [Test]
    public void EditableContainerChanged_RefreshesVersionControlAvailability()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        PackFileContainer? editable = null;
        var packFileService = new Mock<IPackFileService>();
        packFileService
            .Setup(service => service.GetEditablePack())
            .Returns(() => editable);
        var eventHub = new TestEventHub();
        var viewModel = CreateViewModel(
            packFileService.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IUiCommandFactory>(),
            Mock.Of<IFolderProjectOpenService>(),
            eventHub);
        var canExecuteChangedCount = 0;
        viewModel.OpenFolderProjectVersionControlCommand
            .CanExecuteChanged +=
                (_, _) => canExecuteChangedCount++;

        NUnitAssert.That(
            viewModel.OpenFolderProjectVersionControlCommand
                .CanExecute(null),
            Is.False);

        editable = project;
        eventHub.Publish(
            new PackFileContainerSetAsMainEditableEvent(project));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                viewModel.OpenFolderProjectVersionControlCommand
                    .CanExecute(null),
                Is.True);
            NUnitAssert.That(canExecuteChangedCount, Is.EqualTo(1));
        });

        editable = new PackFileContainer("regular.pack");
        eventHub.Publish(
            new PackFileContainerSetAsMainEditableEvent(editable));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                viewModel.OpenFolderProjectVersionControlCommand
                    .CanExecute(null),
                Is.False);
            NUnitAssert.That(canExecuteChangedCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void VersionControlMenuCommand_OpensWindowOnce()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>();
        packFileService
            .Setup(service => service.GetEditablePack())
            .Returns(project);
        var windowService =
            new Mock<IFolderProjectVersionControlWindowService>();
        var services = new ServiceCollection();
        services.AddSingleton(packFileService.Object);
        services.AddSingleton(windowService.Object);
        services.AddTransient<OpenFolderProjectVersionControlCommand>();
        using var provider = services.BuildServiceProvider();
        var viewModel = CreateViewModel(
            packFileService.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            new UiCommandFactory(provider),
            Mock.Of<IFolderProjectOpenService>(),
            new TestEventHub());

        viewModel.OpenFolderProjectVersionControlCommand.Execute(null);

        windowService.Verify(
            service => service.ShowDialog(
                project.ProjectRoot,
                "测试工程",
                false),
            Times.Once);
    }

    private static MenuBarViewModel CreateViewModel(
        IPackFileService packFileService,
        ApplicationSettingsService settingsService,
        IUiCommandFactory uiCommandFactory,
        IFolderProjectOpenService openService,
        IEventHub eventHub)
    {
        var editorDatabase = new Mock<IEditorDatabase>();
        editorDatabase
            .Setup(database => database.GetEditorInfos())
            .Returns([]);
        return new MenuBarViewModel(
            packFileService,
            settingsService,
            editorDatabase.Object,
            uiCommandFactory,
            new TouchedFilesRecorder(
                packFileService,
                Mock.Of<IGlobalEventHub>(),
                settingsService),
            Mock.Of<IFileSaveService>(),
            Mock.Of<IPackFileContainerLoader>(),
            openService,
            Mock.Of<IStandardDialogs>(),
            eventHub);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-menu-version-control-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
