using AssetEditor.Services;
using AssetEditor.UiCommands;
using AssetEditor.Events;
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
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

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
    public void VersionControlMenuCommand_OpensEmbeddedGitPanel()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>();
        packFileService
            .Setup(service => service.GetEditablePack())
            .Returns(project);
        var eventHub = new TestEventHub();
        OpenFolderProjectGitPanelEvent? published = null;
        eventHub.Register<OpenFolderProjectGitPanelEvent>(
            this,
            item => published = item);
        var services = new ServiceCollection();
        services.AddSingleton(packFileService.Object);
        services.AddSingleton<IEventHub>(eventHub);
        services.AddTransient<OpenFolderProjectVersionControlCommand>();
        using var provider = services.BuildServiceProvider();
        var viewModel = CreateViewModel(
            packFileService.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            new UiCommandFactory(provider),
            Mock.Of<IFolderProjectOpenService>(),
            eventHub);

        viewModel.OpenFolderProjectVersionControlCommand.Execute(null);

        NUnitAssert.That(published, Is.Not.Null);
    }

    [Test]
    public void FolderProject_DisablesSaveActivePackWithoutGeneratingPack()
    {
        new LocalizationManager().LoadLanguage();
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetEditablePack())
            .Returns(project);
        var commandFactory = new Mock<IUiCommandFactory>();
        var viewModel = CreateViewModel(
            packFileService.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            commandFactory.Object,
            Mock.Of<IFolderProjectOpenService>(),
            new TestEventHub());

        var canSaveActivePack =
            viewModel.SaveActivePackCommand.CanExecute(null);
        viewModel.SaveActivePackCommand.Execute(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                canSaveActivePack,
                Is.False);
            NUnitAssert.That(
                viewModel.IsSaveActivePackVisible,
                Is.False);
            NUnitAssert.That(
                viewModel.GeneratePackCommand.CanExecute(null),
                Is.True);
        });
        commandFactory.Verify(factory => factory.Create<
            SavePackFileContainerCommand>(
                It.IsAny<Action<SavePackFileContainerCommand>?>()),
            Times.Never);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void GeneratePack_IsSeparateFolderProjectAction()
    {
        new LocalizationManager().LoadLanguage();
        using var directory = new TemporaryDirectory();
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"ae-generated-{Guid.NewGuid():N}.pack");
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings
            {
                Name = "测试工程",
                OutputPackPath = outputPath,
            });
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetEditablePack())
            .Returns(project);
        var settings =
            new ApplicationSettingsService(GameTypeEnum.Warhammer3);
        var saveCommand = new SavePackFileContainerCommand(
            packFileService.Object,
            Mock.Of<IStandardDialogs>(),
            settings);
        var commandFactory = new Mock<IUiCommandFactory>();
        commandFactory.Setup(factory => factory.Create<
                SavePackFileContainerCommand>(
                It.IsAny<Action<SavePackFileContainerCommand>?>()))
            .Returns(saveCommand);
        var viewModel = CreateViewModel(
            packFileService.Object,
            settings,
            commandFactory.Object,
            Mock.Of<IFolderProjectOpenService>(),
            new TestEventHub());

        viewModel.GeneratePackCommand.Execute(null);

        packFileService.Verify(service => service.SavePackContainer(
            project,
            outputPath,
            false,
            It.IsAny<GameInformation>()), Times.Once);
    }

    [Test]
    public void CreateNewPackFile_UsesStandardDialogAndTrimsName()
    {
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(service => service.ShowTextInputDialog(
                "New Pack Name",
                ""))
            .Returns(new TextInputDialogResult(true, "  test.pack  "));
        var packFileService = new Mock<IPackFileService>();
        var createdPack = new PackFileContainer("test.pack");
        packFileService.Setup(service => service.CreateNewPackFileContainer(
                "test.pack",
                PackFileVersion.PFH5,
                PackFileCAType.MOD,
                false))
            .Returns(createdPack);
        var viewModel = CreateViewModel(
            packFileService.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IUiCommandFactory>(),
            Mock.Of<IFolderProjectOpenService>(),
            new TestEventHub(),
            dialogs.Object);

        viewModel.CreateNewPackFileCommand.Execute(null);

        packFileService.Verify(service => service.SetEditablePack(
            createdPack), Times.Once);
    }

    [Test]
    public void CreateNewPackFile_CancelDoesNotCreatePack()
    {
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(service => service.ShowTextInputDialog(
                "New Pack Name",
                ""))
            .Returns(new TextInputDialogResult(false, "ignored.pack"));
        var packFileService = new Mock<IPackFileService>();
        var viewModel = CreateViewModel(
            packFileService.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IUiCommandFactory>(),
            Mock.Of<IFolderProjectOpenService>(),
            new TestEventHub(),
            dialogs.Object);

        viewModel.CreateNewPackFileCommand.Execute(null);

        packFileService.Verify(service => service.CreateNewPackFileContainer(
            It.IsAny<string>(),
            It.IsAny<PackFileVersion>(),
            It.IsAny<PackFileCAType>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public void RecentPackFile_LoadFailure_UsesStandardDialog()
    {
        _ = new LocalizationManager();
        const string packPath = @"D:\packs\missing.pack";
        var settingsService = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
        settingsService.CurrentSettings.RecentPackFilePaths.Add(packPath);
        var loader = new Mock<IPackFileContainerLoader>();
        loader.Setup(service => service.Load(packPath)).Returns((PackFileContainer?)null);
        var dialogs = new Mock<IStandardDialogs>();
        var viewModel = CreateViewModel(
            Mock.Of<IPackFileService>(),
            settingsService,
            Mock.Of<IUiCommandFactory>(),
            Mock.Of<IFolderProjectOpenService>(),
            new TestEventHub(),
            dialogs.Object,
            loader.Object);

        viewModel.RecentPackFiles.Single().Command.Execute(null);

        dialogs.Verify(service => service.ShowDialogBox(It.IsAny<string>(), "Error"), Times.Once);
    }

    private static MenuBarViewModel CreateViewModel(
        IPackFileService packFileService,
        ApplicationSettingsService settingsService,
        IUiCommandFactory uiCommandFactory,
        IFolderProjectOpenService openService,
        IEventHub eventHub,
        IStandardDialogs? standardDialogs = null,
        IPackFileContainerLoader? packFileContainerLoader = null)
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
            packFileContainerLoader ?? Mock.Of<IPackFileContainerLoader>(),
            openService,
            standardDialogs ?? Mock.Of<IStandardDialogs>(),
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
