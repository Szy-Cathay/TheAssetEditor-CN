using System.Windows;
using System.Windows.Threading;
using AssetEditor.Services;
using AssetEditor.ViewModels;
using AssetEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.Common;
using Shared.Ui.Common.ValueConverters;

namespace AssetEditorTests;

public class MainViewModelCloseTests
{
    [NUnit.Framework.SetUp]
    public void SetUp()
    {
        new LocalizationManager().LoadLanguage();
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Apartment(ApartmentState.STA)]
    public void Closing_SynchronousApproval_ClosesAfterClosingEventReturns()
    {
        var pack = new PackFileContainer("普通 Pack");
        var editorManager = new Mock<IEditorManager>();
        editorManager
            .Setup(manager => manager.ShouldBlockCloseCommand(
                It.IsAny<IEditorInterface>(),
                It.IsAny<bool>()))
            .Returns(true);
        var viewModel = CreateMainViewModel(
            editorManager.Object,
            pack);
        var settings = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        var serviceProvider = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .AddSingleton(new PackAutoSaveService(
                Mock.Of<IPackFileService>(),
                settings))
            .BuildServiceProvider();
        NUnit.Framework.Assert.That(
            Application.Current,
            NUnit.Framework.Is.Null.Or.InstanceOf<TestApplication>());
        _ = Application.Current as TestApplication ??
            new TestApplication(serviceProvider);
        NUnit.Framework.Assert.That(
            Application.Current,
            NUnit.Framework.Is.InstanceOf<IAssetEditorMain>());
        var window = new MainWindow(settings, serviceProvider)
        {
            DataContext = viewModel
        };
        Exception? dispatcherException = null;
        var closed = false;
        DispatcherUnhandledExceptionEventHandler onUnhandledException =
            (_, args) =>
            {
                dispatcherException = args.Exception;
                args.Handled = true;
            };
        window.Dispatcher.UnhandledException += onUnhandledException;
        window.Closed += (_, _) => closed = true;

        try
        {
            var dispatcherFrame = new DispatcherFrame();
            window.Show();
            window.Dispatcher.BeginInvoke(
                new Action(window.Close),
                DispatcherPriority.Normal);
            window.Dispatcher.BeginInvoke(
                new Action(() => dispatcherFrame.Continue = false),
                DispatcherPriority.Background);
            Dispatcher.PushFrame(dispatcherFrame);

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(
                    dispatcherException,
                    NUnit.Framework.Is.Null);
                NUnit.Framework.Assert.That(
                    closed,
                    NUnit.Framework.Is.True);
            });
        }
        finally
        {
            window.Dispatcher.UnhandledException -= onUnhandledException;
            if (window.IsVisible)
                window.Close();
            viewModel.FileTree.Dispose();
        }
    }

    [NUnit.Framework.Test]
    public void Closing_FolderProjectOutputOutOfDate_DoesNotReportUnsavedPackFiles()
    {
        var projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"ae-folder-close-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try
        {
            using var project = FolderProjectContainer.Create(
                projectRoot,
                new FolderProjectSettings { Name = "工程" });
            var editorManager = new Mock<IEditorManager>();
            editorManager
                .Setup(manager => manager.ShouldBlockCloseCommand(
                    It.IsAny<IEditorInterface>(),
                    It.IsAny<bool>()))
                .Returns(true);
            var viewModel = CreateMainViewModel(
                editorManager.Object,
                project);
            viewModel.FileTree.Files.Single().UnsavedChanged = true;
            var editor = Mock.Of<IEditorInterface>();

            viewModel.ClosingCommand.Execute(editor);

            editorManager.Verify(
                manager => manager.ShouldBlockCloseCommand(editor, false),
                Times.Once);
            viewModel.FileTree.Dispose();
        }
        finally
        {
            Directory.Delete(projectRoot, true);
        }
    }

    [NUnit.Framework.Test]
    public void Closing_OrdinaryPackDirty_ReportsUnsavedPackFiles()
    {
        var pack = new PackFileContainer("普通 Pack");
        var editorManager = new Mock<IEditorManager>();
        editorManager
            .Setup(manager => manager.ShouldBlockCloseCommand(
                It.IsAny<IEditorInterface>(),
                It.IsAny<bool>()))
            .Returns(true);
        var viewModel = CreateMainViewModel(
            editorManager.Object,
            pack);
        viewModel.FileTree.Files.Single().UnsavedChanged = true;
        var editor = Mock.Of<IEditorInterface>();

        viewModel.ClosingCommand.Execute(editor);

        editorManager.Verify(
            manager => manager.ShouldBlockCloseCommand(editor, true),
            Times.Once);
        viewModel.FileTree.Dispose();
    }

    [NUnit.Framework.Test]
    public async Task Closing_DirtyFolderProject_WaitsForCloseGuard()
    {
        var projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"ae-folder-close-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try
        {
            using var project = FolderProjectContainer.Create(
                projectRoot,
                new FolderProjectSettings { Name = "工程" });
            var editorManager = new Mock<IEditorManager>();
            editorManager
                .Setup(manager => manager.ShouldBlockCloseCommand(
                    It.IsAny<IEditorInterface>(),
                    false))
                .Returns(true);
            var closeResult =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            var closeGuard = new Mock<IFolderProjectCloseGuard>();
            closeGuard
                .Setup(guard => guard.CanCloseAsync(project))
                .Returns(closeResult.Task);
            var viewModel = CreateMainViewModel(
                editorManager.Object,
                project,
                closeGuard.Object);

            var closing = viewModel.ClosingCommand.ExecuteAsync(null);

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(
                    viewModel.IsLoadingPacks,
                    NUnit.Framework.Is.True);
                NUnit.Framework.Assert.That(
                    viewModel.IsClosingWithoutPrompt,
                    NUnit.Framework.Is.False);
            });

            closeResult.SetResult(false);
            await closing;

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(
                    viewModel.IsLoadingPacks,
                    NUnit.Framework.Is.False);
                NUnit.Framework.Assert.That(
                    viewModel.IsClosingWithoutPrompt,
                    NUnit.Framework.Is.False);
            });
            viewModel.FileTree.Dispose();
        }
        finally
        {
            Directory.Delete(projectRoot, true);
        }
    }

    [NUnit.Framework.Test]
    public void ShouldBlockCloseCommand_DirtyEditor_RequiresPrompt()
    {
        var eventHub = new TestEventHub();
        var manager = new EditorManager(
            eventHub,
            Mock.Of<IPackFileService>(),
            Mock.Of<IEditorDatabase>(),
            (_, _, _) => MessageBoxResult.None);
        var editor = new Mock<IEditorInterface>();
        editor.As<ISaveableEditor>()
            .SetupGet(saveable => saveable.HasUnsavedChanges)
            .Returns(true);
        manager.CurrentEditorsList.Add(editor.Object);

        var closesWithoutPrompt = manager.ShouldBlockCloseCommand(
            editor.Object,
            false);

        NUnit.Framework.Assert.That(
            closesWithoutPrompt,
            NUnit.Framework.Is.False);
    }

    [NUnit.Framework.Test]
    public void ShouldBlockCloseCommand_OrdinaryPackDirty_RequiresPrompt()
    {
        var manager = new EditorManager(
            new TestEventHub(),
            Mock.Of<IPackFileService>(),
            Mock.Of<IEditorDatabase>(),
            (_, _, _) => MessageBoxResult.None);
        var editor = Mock.Of<IEditorInterface>();

        var closesWithoutPrompt = manager.ShouldBlockCloseCommand(
            editor,
            true);

        NUnit.Framework.Assert.That(
            closesWithoutPrompt,
            NUnit.Framework.Is.False);
    }

    private static MainViewModel CreateMainViewModel(
        IEditorManager editorManager,
        PackFileContainer container,
        IFolderProjectCloseGuard? closeGuard = null)
    {
        var eventHub = new TestEventHub();
        var packFileService = new Mock<IPackFileService>();
        packFileService
            .Setup(service => service.GetAllPackfileContainers())
            .Returns([container]);
        packFileService
            .Setup(service => service.GetEditablePack())
            .Returns(container);
        var settings = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        var contextMenuBuilder = new Mock<IContextMenuBuilder>();
        contextMenuBuilder
            .SetupGet(builder => builder.Type)
            .Returns(ContextMenuType.MainApplication);
        contextMenuBuilder
            .Setup(builder => builder.Build(It.IsAny<TreeNode>()))
            .Returns([]);
        var fileTreeFactory = new PackFileTreeViewFactory(
            settings,
            packFileService.Object,
            eventHub,
            new ContextMenuFactory([contextMenuBuilder.Object]));

        return new MainViewModel(
            editorManager,
            fileTreeFactory,
            null!,
            packFileService.Object,
            Mock.Of<IEditorDatabase>(),
            Mock.Of<IUiCommandFactory>(),
            eventHub,
            settings,
            closeGuard ?? Mock.Of<IFolderProjectCloseGuard>(
                guard => guard.CanCloseAsync(
                    It.IsAny<FolderProjectContainer>()) ==
                    Task.FromResult(true)));
    }

    private sealed class TestApplication : Application, IAssetEditorMain
    {
        public IServiceProvider ServiceProvider { get; }

        public TestApplication(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Resources.MergedDictionaries.Add(CreateResourceDictionary(
                "Themes/ColourDictionaries/DarkTheme.xaml"));
            Resources.MergedDictionaries.Add(CreateResourceDictionary(
                "Themes/ControlColours.xaml"));
            Resources.MergedDictionaries.Add(CreateResourceDictionary(
                "Themes/Controls.xaml"));
            Resources["BoolToChangedPrefixStr"] =
                new BoolToStringConverter { TrueValue = "*" };
        }

        private static ResourceDictionary CreateResourceDictionary(
            string path) => new()
            {
                Source = new Uri(
                    $"pack://application:,,,/AssetEditor.CN;component/{path}")
            };
    }
}
