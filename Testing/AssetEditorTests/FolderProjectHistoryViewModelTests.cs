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
    public async Task Refresh_ShowsUnrecordedChangesAndRestorePoints()
    {
        using var directory = new TemporaryDirectory();
        using var project = CreateProject(directory.Path);
        var history = CreateHistoryService(project.ProjectRoot);
        history.Setup(item => item.GetStatus(
                project.ProjectRoot,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
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
                Is.EqualTo("记录工程当前状态"));
            NUnitAssert.That(
                viewModel.CreateRestorePointCommand.CanExecute(null),
                Is.True);
        });
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
        prompt.Setup(item => item.Show(
                FolderProjectUnsavedChangesOperation.CreateRestorePoint))
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
        history.Setup(item => item.GetStatus(
                project.ProjectRoot,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
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
        prompt.Setup(item => item.Show(
                FolderProjectUnsavedChangesOperation.CreateRestorePoint))
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
        prompt.Setup(item => item.Show(
                FolderProjectUnsavedChangesOperation.CreateRestorePoint))
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
        prompt.Setup(item => item.Show(
                FolderProjectUnsavedChangesOperation.CreateRestorePoint))
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
        NUnitAssert.That(
            viewModel.SelectedRestorePoint?.ChangeSummary?.Modified,
            Is.EqualTo(1));
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

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                document.Descendants()
                    .Any(item => item.Name.LocalName ==
                                 "OperationProgressWindowHost"),
                Is.True);
            NUnitAssert.That(source, Does.Contain("AeButton.Primary"));
            NUnitAssert.That(source, Does.Contain("AeInput.TextBox"));
            NUnitAssert.That(source, Does.Contain("AeList.View"));
            NUnitAssert.That(
                source,
                Does.Contain("FolderProject.History.SameDiskNotice"));
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
                            FindDescendants<TextBlock>(view)
                                .Select(text => text.Text),
                            Does.Contain("工程历史"));
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
        IFolderProjectUnsavedChangesPrompt? prompt = null) =>
        new(
            history,
            unsaved ?? Mock.Of<IFolderProjectUnsavedChangesService>(),
            prompt ?? Mock.Of<IFolderProjectUnsavedChangesPrompt>(),
            Mock.Of<IStandardDialogs>(),
            LocalizationManager.Instance);

    private static Mock<IFolderProjectHistoryService> CreateHistoryService(
        string projectRoot,
        IReadOnlyList<FolderProjectRestorePoint>? restorePoints = null)
    {
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(item => item.GetStatus(
                projectRoot,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                [
                    new FolderProjectUnrecordedChange(
                        "changed.bin",
                        FolderProjectUnrecordedChangeKind.Modified),
                ]));
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
        bool initial = false) =>
        new(
            id,
            description,
            DateTimeOffset.Parse("2026-08-13T08:00:00+08:00"),
            null,
            initial);

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
