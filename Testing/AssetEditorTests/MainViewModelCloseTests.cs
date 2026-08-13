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

namespace AssetEditorTests;

public class MainViewModelCloseTests
{
    [NUnit.Framework.SetUp]
    public void SetUp()
    {
        new LocalizationManager().LoadLanguage();
    }

    [NUnit.Framework.Test]
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
            .AddSingleton(settings)
            .AddSingleton(new PackAutoSaveService(
                Mock.Of<IPackFileService>(),
                settings))
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(serviceProvider, () =>
        {
            var window = new MainWindow(serviceProvider)
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
        });
    }

    [NUnit.Framework.Test]
    public void Closing_DirtyFolderProject_ClosesProgressWindowBeforePrompt()
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
            var promptReached = new TaskCompletionSource<(bool, bool)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var closeGuard = new Mock<IFolderProjectCloseGuard>();
            closeGuard
                .Setup(guard => guard.CanCloseAsync(
                    project,
                    It.IsAny<Action<FolderProjectCloseProgress>>(),
                    It.IsAny<Func<Task>>()))
                .Returns((
                    FolderProjectContainer? _,
                    Action<FolderProjectCloseProgress> reportProgress,
                    Func<Task> completeProgressBeforePrompt) =>
                    RunCloseCheckAsync(
                        reportProgress,
                        completeProgressBeforePrompt));
            var viewModel = CreateMainViewModel(
                editorManager.Object,
                project,
                closeGuard.Object);
            var settings = new ApplicationSettingsService(
                GameTypeEnum.Warhammer3);
            var serviceProvider = new ServiceCollection()
                .AddSingleton(LocalizationManager.Instance)
                .AddSingleton(settings)
                .AddSingleton(new PackAutoSaveService(
                    Mock.Of<IPackFileService>(),
                    settings))
                .BuildServiceProvider();

            WpfTestApplicationHost.InvokeWithThemeResources(
                serviceProvider,
                () =>
                {
                    var window = new MainWindow(serviceProvider)
                    {
                        DataContext = viewModel,
                        Left = -10000,
                        Top = -10000,
                        ShowActivated = false,
                    };
                    try
                    {
                        var dispatcherFrame = new DispatcherFrame();
                        promptReached.Task.ContinueWith(
                            _ => window.Dispatcher.BeginInvoke(
                                new Action(() =>
                                    dispatcherFrame.Continue = false),
                                DispatcherPriority.Background));
                        window.Show();
                        window.Dispatcher.BeginInvoke(
                            new Action(window.Close),
                            DispatcherPriority.Normal);
                        Dispatcher.PushFrame(dispatcherFrame);

                        var visibility = promptReached.Task.GetAwaiter()
                            .GetResult();
                        NUnit.Framework.Assert.Multiple(() =>
                        {
                            NUnit.Framework.Assert.That(
                                visibility.Item1,
                                NUnit.Framework.Is.True,
                                "关闭检查期间没有显示汇总进度窗口。");
                            NUnit.Framework.Assert.That(
                                visibility.Item2,
                                NUnit.Framework.Is.False,
                                "确认回调开始时汇总进度窗口仍然可见。");
                        });
                    }
                    finally
                    {
                        window.DataContext = null;
                        if (window.IsVisible)
                            window.Close();
                    }
                });
            viewModel.FileTree.Dispose();

            async Task<bool> RunCloseCheckAsync(
                Action<FolderProjectCloseProgress> reportProgress,
                Func<Task> completeProgressBeforePrompt)
            {
                reportProgress(new FolderProjectCloseProgress(
                    FolderProjectCloseProgressStage.SummarizingChanges,
                    3,
                    3,
                    1));
                await Task.Delay(650);
                var wasVisible = Application.Current.Windows
                    .OfType<Shared.Ui.Common.OperationProgress
                        .OperationProgressWindow>()
                    .Any();
                await completeProgressBeforePrompt();
                var isVisible = Application.Current.Windows
                    .OfType<Shared.Ui.Common.OperationProgress
                        .OperationProgressWindow>()
                    .Any();
                promptReached.TrySetResult((wasVisible, isVisible));
                return false;
            }
        }
        finally
        {
            Directory.Delete(projectRoot, true);
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
    public async Task Closing_DirtyFolderProject_ReportsProgressAndWaitsForCloseGuard()
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
                .Setup(guard => guard.CanCloseAsync(
                    project,
                    It.IsAny<Action<FolderProjectCloseProgress>>(),
                    It.IsAny<Func<Task>>()))
                .Returns((
                    FolderProjectContainer? _,
                    Action<FolderProjectCloseProgress> reportProgress,
                    Func<Task> _) =>
                {
                    reportProgress(
                        new FolderProjectCloseProgress(
                            FolderProjectCloseProgressStage
                                .ReadingRepositoryStatus,
                            2,
                            3));
                    return closeResult.Task;
                });
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
                NUnit.Framework.Assert.That(
                    viewModel.LoadingStatusText,
                    NUnit.Framework.Is.EqualTo(
                        "正在扫描工程文件的未记录修改…"));
                NUnit.Framework.Assert.That(
                    viewModel.LoadingProgressValue,
                    NUnit.Framework.Is.EqualTo(2));
                NUnit.Framework.Assert.That(
                    viewModel.LoadingProgressMaximum,
                    NUnit.Framework.Is.EqualTo(3));
                NUnit.Framework.Assert.That(
                    viewModel.LoadingProgressIsIndeterminate,
                    NUnit.Framework.Is.True);
                NUnit.Framework.Assert.That(
                    viewModel.LoadingProgressDetailText,
                    NUnit.Framework.Does.StartWith("第 2/3 步 · 已等待 "));
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
                NUnit.Framework.Assert.That(
                    viewModel.LoadingStatusText,
                    NUnit.Framework.Is.Empty);
                NUnit.Framework.Assert.That(
                    viewModel.LoadingProgressDetailText,
                    NUnit.Framework.Is.Empty);
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
                    It.IsAny<FolderProjectContainer>(),
                    It.IsAny<Action<FolderProjectCloseProgress>>(),
                    It.IsAny<Func<Task>>()) ==
                    Task.FromResult(true)));
    }

}
