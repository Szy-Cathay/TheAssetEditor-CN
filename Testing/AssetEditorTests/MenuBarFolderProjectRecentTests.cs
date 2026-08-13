using AssetEditor.Services;
using AssetEditor.UiCommands;
using AssetEditor.Events;
using AssetEditor.ViewModels;
using AssetEditor.Views;
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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace AssetEditorTests;

public class MenuBarFolderProjectRecentTests
{
    [Test]
    public void FileMenu_UsesProjectAndReferencePackWorkflow()
    {
        var document = XDocument.Load(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Views",
            "MenuBarView.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var commands = document
            .Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Command")?.Value)
            .Where(value => value != null)
            .ToHashSet();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(commands, Does.Contain(
                "{Binding MenuBar.CreateFolderProjectCommand}"));
            NUnitAssert.That(commands, Does.Contain(
                "{Binding MenuBar.ImportPackAsFolderProjectCommand}"));
            NUnitAssert.That(commands, Does.Contain(
                "{Binding MenuBar.OpenFolderProjectCommand}"));
            NUnitAssert.That(commands, Does.Contain(
                "{Binding MenuBar.OpenReferencePackCommand}"));
            NUnitAssert.That(commands, Does.Contain(
                "{Binding MenuBar.GeneratePackCommand}"));
            NUnitAssert.That(commands, Does.Not.Contain(
                "{Binding MenuBar.CreateNewPackFileCommand}"));
            NUnitAssert.That(commands, Does.Not.Contain(
                "{Binding MenuBar.OpenPackFileCommand}"));
            NUnitAssert.That(commands, Does.Not.Contain(
                "{Binding MenuBar.SaveActivePackCommand}"));
        });
    }

    [Test]
    [NonParallelizable]
    public void FileMenu_RendersLocalizedProjectAndReferenceWorkflow()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                var settings = new ApplicationSettingsService(
                    GameTypeEnum.Warhammer3);
                var viewModel = CreateViewModel(
                    Mock.Of<IPackFileService>(),
                    settings,
                    Mock.Of<IUiCommandFactory>(),
                    Mock.Of<IFolderProjectOpenService>(),
                    new TestEventHub());
                var view = new MenuBarView
                {
                    DataContext = new MenuBarHost(viewModel),
                };
                var window = new Window
                {
                    Content = view,
                    Width = 1100,
                    Height = 240,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var menu = FindVisualDescendants<Menu>(window).Single();
                    var fileMenu = menu.Items
                        .OfType<MenuItem>()
                        .First();
                    var headers = fileMenu.Items
                        .OfType<MenuItem>()
                        .Select(item => item.Header?.ToString())
                        .Where(header => header != null)
                        .ToArray();

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(headers, Does.Contain("新建 Mod 工程"));
                        NUnitAssert.That(headers, Does.Contain("从 Pack 导入工程"));
                        NUnitAssert.That(headers, Does.Contain("打开工程"));
                        NUnitAssert.That(headers, Does.Contain("打开参考 Pack"));
                        NUnitAssert.That(headers, Does.Contain("生成 Pack"));
                        NUnitAssert.That(view.ActualWidth, Is.GreaterThan(500));
                        NUnitAssert.That(view.ActualHeight, Is.GreaterThan(0));
                    });

                    Capture(window, "issue-88-main-menu.png");
                    fileMenu.IsSubmenuOpen = true;
                    window.UpdateLayout();
                    var popup = (Popup)fileMenu.Template.FindName(
                        "PART_Popup",
                        fileMenu);
                    var popupContent = (FrameworkElement)popup.Child;
                    popupContent.UpdateLayout();
                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(popup.IsOpen, Is.True);
                        NUnitAssert.That(
                            popupContent.ActualWidth,
                            Is.GreaterThan(100));
                        NUnitAssert.That(
                            popupContent.ActualHeight,
                            Is.GreaterThan(100));
                    });
                    Capture(popupContent, "issue-88-file-menu.png");
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Test]
    public void PackFileServiceInterface_UsesExplicitWorkspaceRoles()
    {
        var interfaceMethods = typeof(IPackFileService).GetMethods();
        var serviceType = typeof(IPackFileService).Assembly.GetType(
            "Shared.Core.PackFiles.PackFileService",
            throwOnError: true)!;
        var publicServiceMethods = serviceType.GetMethods();
        var addContainer = interfaceMethods.Single(method =>
            method.Name == nameof(IPackFileService.AddContainer));
        var createContainer = interfaceMethods.Single(method =>
            method.Name == nameof(
                IPackFileService.CreateNewPackFileContainer));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                addContainer.GetParameters().Select(parameter =>
                    parameter.ParameterType),
                Is.EqualTo(new[] { typeof(PackFileContainer) }));
            NUnitAssert.That(
                createContainer.GetParameters().Any(parameter =>
                    parameter.ParameterType == typeof(bool)),
                Is.False);
            NUnitAssert.That(
                publicServiceMethods
                    .Where(method =>
                        method.Name is nameof(IPackFileService.AddContainer) or
                            nameof(IPackFileService.CreateNewPackFileContainer))
                    .SelectMany(method => method.GetParameters())
                    .Any(parameter => parameter.ParameterType == typeof(bool)),
                Is.False);
            NUnitAssert.That(
                interfaceMethods.Any(method =>
                    method.Name == nameof(
                        IPackFileService.AddEditableFolderProject)),
                Is.True);
            NUnitAssert.That(
                interfaceMethods.Any(method =>
                    method.Name == nameof(
                        IPackFileService.AddReferencePack)),
                Is.True);
        });
    }

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
    public void RecentReferencePack_UsesReferenceRole()
    {
        const string packPath = @"D:\packs\reference.pack";
        var settingsService = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        settingsService.CurrentSettings.RecentPackFilePaths.Add(packPath);
        var reference = new PackFileContainer("reference.pack")
        {
            SystemFilePath = packPath,
        };
        var loader = new Mock<IPackFileContainerLoader>();
        loader.Setup(item => item.Load(packPath)).Returns(reference);
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(item => item.AddReferencePack(reference))
            .Returns(reference);
        var viewModel = CreateViewModel(
            packFileService.Object,
            settingsService,
            Mock.Of<IUiCommandFactory>(),
            Mock.Of<IFolderProjectOpenService>(),
            new TestEventHub(),
            packFileContainerLoader: loader.Object);

        viewModel.RecentReferencePacks.Single().Command.Execute(null);

        packFileService.Verify(
            item => item.AddReferencePack(reference),
            Times.Once);
        packFileService.Verify(
            item => item.AddContainer(
                It.IsAny<PackFileContainer>()),
            Times.Never);
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
    public void RecentReferencePack_LoadFailure_UsesStandardDialog()
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

        viewModel.RecentReferencePacks.Single().Command.Execute(null);

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

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the solution root.");
    }

    private static void Capture(
        FrameworkElement element,
        string fileName)
    {
        var outputDirectory = Environment.GetEnvironmentVariable(
            "AE_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(outputDirectory);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(element.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(element.ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        using var stream = File.Create(Path.Combine(
            outputDirectory,
            fileName));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static IEnumerable<T> FindVisualDescendants<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class MenuBarHost(MenuBarViewModel menuBar)
    {
        public MenuBarViewModel MenuBar { get; } = menuBar;
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
