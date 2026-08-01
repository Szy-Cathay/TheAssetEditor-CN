using System.Xml.Linq;
using AssetEditor.Services;
using AssetEditor.ViewModels;
using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using System.Text.Json;

namespace AssetEditorTests;

public class FolderProjectGitWorkspaceViewModelTests
{
    [SetUp]
    public void SetUp()
    {
        new LocalizationManager().LoadLanguage();
    }

    [Test]
    public void EditableContainer_EnablesGitOnlyForFolderProject()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var workspace = CreateWorkspace(out var versionControl);

        workspace.SetEditableContainer(project);
        workspace.ShowGitManagement();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(workspace.IsEnabled, Is.True);
            NUnitAssert.That(workspace.SelectedSidebarTabIndex, Is.EqualTo(1));
            NUnitAssert.That(
                versionControl.ProjectRoot,
                Is.EqualTo(project.ProjectRoot));
            NUnitAssert.That(versionControl.ProjectName, Is.EqualTo("测试工程"));
        });

        var pack = new PackFileContainer("普通.pack");
        workspace.SetEditableContainer(pack);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(workspace.IsEnabled, Is.False);
            NUnitAssert.That(workspace.SelectedSidebarTabIndex, Is.Zero);
        });
    }

    [Test]
    public async Task InternalGitDetach_KeepsGitManagementSelected()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var workspace = CreateWorkspace(out var versionControl);
        workspace.SetEditableContainer(project);
        await versionControl.RefreshCommand.ExecutionTask!;
        workspace.ShowGitManagement();
        versionControl.IsBusy = true;

        workspace.SetEditableContainer(null);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(workspace.IsEnabled, Is.True);
            NUnitAssert.That(workspace.SelectedSidebarTabIndex, Is.EqualTo(1));
            NUnitAssert.That(
                versionControl.ProjectRoot,
                Is.EqualTo(project.ProjectRoot));
        });
    }

    [Test]
    public async Task SelectingGitManagement_RefreshesWorkingChanges()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var workspace = CreateWorkspace(
            out var versionControl,
            out var service);
        workspace.SetEditableContainer(project);
        await versionControl.RefreshCommand.ExecutionTask!;
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "master",
                    "1111111",
                    false,
                    FolderProjectRepositoryOperationState.None,
                    [
                        new FolderProjectWorkingChange(
                            "db\\units_tables\\changed.bin",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));
        service.Setup(item => item.GetIdentity(project.ProjectRoot))
            .Returns(new FolderProjectGitIdentity("测试用户", "test@example.invalid"));
        service.Setup(item => item.GetBranches(project.ProjectRoot))
            .Returns(
                [new FolderProjectBranchInfo("master", "1111111", true, true)]);
        service.Setup(item => item.GetStashes(project.ProjectRoot))
            .Returns([]);
        service.Setup(item => item.GetHistory(
                project.ProjectRoot,
                "master",
                It.IsAny<int>()))
            .Returns([]);
        service.Setup(item => item.GetMergeState(project.ProjectRoot))
            .Returns(
                new FolderProjectMergeState(
                    FolderProjectMergePhase.None,
                    "master",
                    null,
                    null,
                    null,
                    null,
                    [],
                    null));

        workspace.SelectedSidebarTabIndex = 1;
        if (versionControl.RefreshCommand.ExecutionTask != null)
            await versionControl.RefreshCommand.ExecutionTask;

        NUnitAssert.That(
            versionControl.UnstagedChanges.Select(item => item.RepositoryPath),
            Is.EqualTo(new[] { "db\\units_tables\\changed.bin" }));
    }

    [Test]
    public void BranchFilter_ReturnsMatchingLocalBranches()
    {
        var workspace = CreateWorkspace(out var versionControl);
        versionControl.Branches.Add(
            new FolderProjectBranchInfo("master", "1", true, true));
        versionControl.Branches.Add(
            new FolderProjectBranchInfo("feature/audio", "2", false));

        workspace.BranchFilter = "audio";

        NUnitAssert.That(
            workspace.FilteredBranches.Select(item => item.Name),
            Is.EqualTo(new[] { "feature/audio" }));
    }

    [Test]
    public void OpenRepository_SelectsExistingRepositoryEditor()
    {
        var editor = new Mock<IFolderProjectGitRepositoryEditor>();
        var editorManager = new Mock<IEditorManager>();
        editorManager.Setup(item => item.GetAllEditors())
            .Returns([editor.Object]);
        var workspace = CreateWorkspace(
            out _,
            editorManager.Object);

        workspace.OpenRepository();

        editorManager.Verify(
            item => item.SetEditorAsCurrent(editor.Object),
            Times.Once);
        editorManager.Verify(
            item => item.Create(
                EditorEnums.FolderProjectGitRepository,
                It.IsAny<Action<IEditorInterface>?>()),
            Times.Never);
    }

    [Test]
    public void OpenRepository_CreatesRepositoryEditorWhenMissing()
    {
        var repository = new FolderProjectGitRepositoryViewModel();
        var editorManager = new Mock<IEditorManager>();
        editorManager.Setup(item => item.GetAllEditors())
            .Returns([]);
        editorManager.Setup(item => item.Create(
                EditorEnums.FolderProjectGitRepository,
                It.IsAny<Action<IEditorInterface>?>()))
            .Returns(
                (EditorEnums _, Action<IEditorInterface>? initialize) =>
                {
                    initialize?.Invoke(repository);
                    return repository;
                });
        var workspace = CreateWorkspace(
            out var versionControl,
            editorManager.Object);

        workspace.OpenRepository();

        editorManager.Verify(
            item => item.Create(
                EditorEnums.FolderProjectGitRepository,
                It.IsAny<Action<IEditorInterface>?>()),
            Times.Once);
        NUnitAssert.That(
            repository.VersionControl,
            Is.SameAs(versionControl));
    }

    [Test]
    public async Task RepositoryBranchFilter_ReturnsMatchingBranchesAndTracksAdditions()
    {
        var repository = new FolderProjectGitRepositoryViewModel();
        var workspace = CreateWorkspace(out var versionControl);
        repository.Open(workspace);
        await versionControl.RefreshCommand.ExecutionTask!;
        versionControl.Branches.Add(
            new FolderProjectBranchInfo("master", "1", true, true));
        versionControl.Branches.Add(
            new FolderProjectBranchInfo("feature/audio", "2", false));

        repository.BranchFilter = "audio";

        NUnitAssert.That(
            repository.FilteredBranches.Select(item => item.Name),
            Is.EqualTo(new[] { "feature/audio" }));

        versionControl.Branches.Add(
            new FolderProjectBranchInfo("feature/audio-tools", "3", false));

        NUnitAssert.That(
            repository.FilteredBranches.Select(item => item.Name),
            Is.EqualTo(new[] { "feature/audio", "feature/audio-tools" }));
        repository.Close();
    }

    [Test]
    public async Task RepositoryHistoryFilter_SearchesDescriptionAuthorAndId()
    {
        var repository = new FolderProjectGitRepositoryViewModel();
        var workspace = CreateWorkspace(out var versionControl);
        repository.Open(workspace);
        await versionControl.RefreshCommand.ExecutionTask!;
        versionControl.History.Add(
            Commit("1111111", "添加音频", "甲", "补充战斗语音"));
        versionControl.History.Add(
            Commit("2222222", "更新模型", "乙", "修正材质"));

        repository.HistoryFilter = "战斗语音";
        NUnitAssert.That(
            repository.FilteredHistory.Select(item => item.Id),
            Is.EqualTo(new[] { "1111111" }));

        repository.HistoryFilter = "乙";
        NUnitAssert.That(
            repository.FilteredHistory.Select(item => item.Id),
            Is.EqualTo(new[] { "2222222" }));

        repository.HistoryFilter = "111";
        NUnitAssert.That(
            repository.FilteredHistory.Select(item => item.Id),
            Is.EqualTo(new[] { "1111111" }));
        repository.Close();
    }

    [Test]
    public async Task RepositoryEmptyStates_ExplainNoDataAndNoSearchResults()
    {
        var repository = new FolderProjectGitRepositoryViewModel();
        var workspace = CreateWorkspace(out var versionControl);
        repository.Open(workspace);
        await versionControl.RefreshCommand.ExecutionTask!;

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(repository.HasFilteredBranches, Is.False);
            NUnitAssert.That(repository.HasFilteredHistory, Is.False);
            NUnitAssert.That(repository.HasSelectedCommit, Is.False);
            NUnitAssert.That(
                repository.BranchEmptyMessage,
                Is.EqualTo("当前仓库没有可查看的本地分支。"));
            NUnitAssert.That(
                repository.HistoryEmptyMessage,
                Is.EqualTo("所选分支还没有提交记录。"));
        });

        repository.BranchFilter = "missing";
        repository.HistoryFilter = "missing";

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                repository.BranchEmptyMessage,
                Is.EqualTo("没有符合搜索条件的分支。"));
            NUnitAssert.That(
                repository.HistoryEmptyMessage,
                Is.EqualTo("没有符合筛选条件的提交。"));
        });
        repository.Close();
    }

    [Test]
    public async Task RepositorySelection_NotifiesDetailsVisibility()
    {
        var repository = new FolderProjectGitRepositoryViewModel();
        var workspace = CreateWorkspace(out var versionControl);
        repository.Open(workspace);
        await versionControl.RefreshCommand.ExecutionTask!;
        var changedProperties = new List<string?>();
        repository.PropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName);

        versionControl.SelectedCommit =
            Commit("1111111", "添加音频", "甲", "补充战斗语音");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(repository.HasSelectedCommit, Is.True);
            NUnitAssert.That(
                changedProperties,
                Does.Contain(nameof(repository.HasSelectedCommit)));
        });
        repository.Close();
    }

    [Test]
    public void RepositoryView_WiresVsStyleBranchNavigationAndResizableDetails()
    {
        var document = XDocument.Load(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Views",
            "FolderProjectVersionControl",
            "FolderProjectGitRepositoryView.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var namedElements = document
            .Descendants()
            .Where(element => element.Attribute(xaml + "Name") != null)
            .ToDictionary(
                element => element.Attribute(xaml + "Name")!.Value);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                namedElements["BranchNavigationPane"].Name,
                Is.EqualTo(presentation + "Border"));
            NUnitAssert.That(
                namedElements["BranchList"].Attribute("ItemsSource")?.Value,
                Is.EqualTo("{Binding FilteredBranches}"));
            NUnitAssert.That(
                namedElements["BranchList"].Attribute("SelectedItem")?.Value,
                Is.EqualTo(
                    "{Binding VersionControl.SelectedHistoryBranch, Mode=TwoWay}"));
            NUnitAssert.That(
                namedElements["HistoryGrid"].Attribute("ItemsSource")?.Value,
                Is.EqualTo("{Binding FilteredHistory}"));
            NUnitAssert.That(
                namedElements["DetailsSplitter"]
                    .Attribute("ResizeDirection")?.Value,
                Is.EqualTo("Rows"));
        });
    }

    [Test]
    public async Task BranchPickerSelection_ClosesPickerAndSwitchesRealRepositoryBranch()
    {
        using var directory = new GitTemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var service = new FolderProjectVersionControlService();
        service.Initialize(
            directory.Path,
            new FolderProjectGitIdentity(
                "AssetEditor.CN 测试用户",
                "test@asseteditor.cn"),
            "main");
        service.CreateBranch(directory.Path, "feature");
        var versionControl = new FolderProjectVersionControlViewModel(
            service,
            new DirectGitOperationCoordinator(),
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IFolderProjectUnsavedChangesService>(),
            Mock.Of<IFolderProjectUnsavedChangesPrompt>(),
            LocalizationManager.Instance);
        var workspace = new FolderProjectGitWorkspaceViewModel(
            versionControl,
            Mock.Of<IEditorManager>());
        workspace.SetEditableContainer(project);
        await versionControl.RefreshCommand.ExecutionTask!;
        var targetBranch = versionControl.Branches.Single(
            branch => branch.Name == "feature");
        workspace.IsBranchPickerOpen = true;

        await workspace.SwitchBranchCommand.ExecuteAsync(targetBranch);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(workspace.IsBranchPickerOpen, Is.False);
            NUnitAssert.That(
                service.GetStatus(directory.Path).CurrentBranch,
                Is.EqualTo("feature"));
        });
    }

    [Test]
    public void GitPanel_BindsBranchPickerStateToWorkspace()
    {
        var document = LoadView("FolderProjectGitPanelView.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var branchToggle = document
            .Descendants(presentation + "ToggleButton")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value == "BranchButton");
        var popup = document.Descendants(presentation + "Popup").Single();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                branchToggle.Attribute("IsChecked")?.Value,
                Does.Contain("IsBranchPickerOpen"));
            NUnitAssert.That(
                popup.Attribute("IsOpen")?.Value,
                Does.Contain("IsBranchPickerOpen"));
        });
    }

    [Test]
    public void GitPanel_BranchRowsStretchAcrossPickerWidth()
    {
        var document = LoadView("FolderProjectGitPanelView.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var branchList = document
            .Descendants(presentation + "ListBox")
            .Single(element =>
                element.Attribute("ItemsSource")?.Value.Contains("FilteredBranches") == true);
        var stretchSetter = branchList
            .Descendants(presentation + "Setter")
            .SingleOrDefault(element =>
                element.Attribute("Property")?.Value == "HorizontalContentAlignment");

        NUnitAssert.That(
            stretchSetter?.Attribute("Value")?.Value,
            Is.EqualTo("Stretch"));
    }

    [Test]
    public async Task SwitchBranchCommand_DirtyWorkspaceOpensChoiceState()
    {
        var workspace = CreateWorkspace(out var versionControl);
        var branch = new FolderProjectBranchInfo(
            "feature/audio",
            "2222222",
            false);
        versionControl.IsInitialized = true;
        versionControl.IsClean = false;

        await workspace.SwitchBranchCommand.ExecuteAsync(branch);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                versionControl.IsBranchSwitchChoiceOpen,
                Is.True);
            NUnitAssert.That(
                versionControl.PendingBranchName,
                Is.EqualTo("feature/audio"));
        });
    }

    [Test]
    public void GitPanel_ShowsDirtyBranchSwitchChoices()
    {
        var document = LoadView("FolderProjectGitPanelView.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var choiceLayer = document
            .Descendants(presentation + "Grid")
            .SingleOrDefault(element =>
                element.Attribute("Visibility")?.Value.Contains(
                    "VersionControl.IsBranchSwitchChoiceOpen",
                    StringComparison.Ordinal) == true);
        var commands = choiceLayer?
            .Descendants(presentation + "Button")
            .Select(element => element.Attribute("Command")?.Value)
            .Where(value => value != null)
            .ToArray();

        NUnitAssert.That(
            commands,
            Is.EquivalentTo(new[]
            {
                "{Binding VersionControl.CarryChangesAndSwitchCommand}",
                "{Binding VersionControl.StashChangesAndSwitchCommand}",
                "{Binding VersionControl.DiscardChangesAndSwitchCommand}",
                "{Binding VersionControl.CancelBranchSwitchCommand}",
            }));
    }

    [Test]
    public void RepositoryBranchDot_TracksSelectedHistoryBranch()
    {
        var document = LoadView("FolderProjectGitRepositoryView.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var branchList = document
            .Descendants(presentation + "ListBox")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value == "BranchList");
        var branchDot = branchList
            .Descendants(presentation + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "●");

        NUnitAssert.That(
            branchDot.Attribute("Visibility")?.Value,
            Does.Contain("ListBoxItem").And.Contain("IsSelected"));
    }

    [Test]
    public void GitPanel_UsesUserInformationHeadingAndHeaderActionButtons()
    {
        var solutionRoot = FindSolutionRoot();
        using var localization = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(solutionRoot, "AssetEditor", "Language_Cn.json")));
        var document = LoadView("FolderProjectGitPanelView.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var stageAll = document.Descendants(presentation + "Button")
            .SingleOrDefault(element =>
                element.Attribute(xaml + "Name")?.Value == "StageAllButton");
        var unstageAll = document.Descendants(presentation + "Button")
            .SingleOrDefault(element =>
                element.Attribute(xaml + "Name")?.Value == "UnstageAllButton");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                localization.RootElement
                    .GetProperty("FolderProject.Git.RelatedItems")
                    .GetString(),
                Is.EqualTo("用户信息"));
            NUnitAssert.That(
                stageAll?.Ancestors(presentation + "Expander.Header").Any(),
                Is.True);
            NUnitAssert.That(
                unstageAll?.Ancestors(presentation + "Expander.Header").Any(),
                Is.True);
            NUnitAssert.That(
                stageAll?.Attribute("Background")?.Value,
                Is.EqualTo("{DynamicResource Button.Static.Background}"));
            NUnitAssert.That(
                unstageAll?.Attribute("Background")?.Value,
                Is.EqualTo("{DynamicResource Button.Static.Background}"));
        });
    }

    [Test]
    public void SidebarViews_UseMatchingTitlesAndHeaderHeights()
    {
        var solutionRoot = FindSolutionRoot();
        var documents = new[]
        {
            XDocument.Load(Path.Combine(
                solutionRoot,
                "Shared",
                "SharedUI",
                "BaseDialogs",
                "PackFileTree",
                "PackFileBrowserView.xaml")),
            LoadView("FolderProjectGitPanelView.xaml"),
        };
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var titles = documents.Select(document =>
            document.Descendants(presentation + "TextBlock")
                .SingleOrDefault(element =>
                    element.Attribute(xaml + "Name")?.Value ==
                    "SidebarTitle"))
            .ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(titles, Is.All.Not.Null);
            NUnitAssert.That(
                titles.Select(title => title?.Attribute("Height")?.Value),
                Is.All.EqualTo("30"));
        });
    }

    [Test]
    public void GitManagementPages_DoNotExposeInitializeAction()
    {
        var solutionRoot = FindSolutionRoot();
        var viewPaths = new[]
        {
            Path.Combine(
                solutionRoot,
                "AssetEditor",
                "Views",
                "FolderProjectVersionControl",
                "FolderProjectGitPanelView.xaml"),
            Path.Combine(
                solutionRoot,
                "AssetEditor",
                "Views",
                "FolderProjectVersionControl",
                "FolderProjectGitRepositoryView.xaml"),
            Path.Combine(
                solutionRoot,
                "AssetEditor",
                "Views",
                "FolderProjectVersionControl",
                "FolderProjectVersionControlWindow.xaml"),
        };

        var initializeBindings = viewPaths
            .Select(XDocument.Load)
            .SelectMany(document => document.Descendants())
            .SelectMany(element => element.Attributes("Command"))
            .Where(attribute =>
                attribute.Value.Contains(
                    "InitializeCommand",
                    StringComparison.Ordinal))
            .ToArray();

        NUnitAssert.That(initializeBindings, Is.Empty);
    }

    private static FolderProjectGitWorkspaceViewModel CreateWorkspace(
        out FolderProjectVersionControlViewModel versionControl,
        IEditorManager? editorManager = null)
    {
        return CreateWorkspace(
            out versionControl,
            out _,
            editorManager);
    }

    private static FolderProjectGitWorkspaceViewModel CreateWorkspace(
        out FolderProjectVersionControlViewModel versionControl,
        out Mock<IFolderProjectVersionControlService> service,
        IEditorManager? editorManager = null)
    {
        service = new Mock<IFolderProjectVersionControlService>();
        service.Setup(item => item.GetStatus(It.IsAny<string>()))
            .Returns(
                new FolderProjectRepositoryStatus(
                    false,
                    null,
                    null,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []));
        versionControl = new FolderProjectVersionControlViewModel(
            service.Object,
            Mock.Of<IFolderProjectGitOperationCoordinator>(),
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IFolderProjectUnsavedChangesService>(),
            Mock.Of<IFolderProjectUnsavedChangesPrompt>(),
            LocalizationManager.Instance);
        return new FolderProjectGitWorkspaceViewModel(
            versionControl,
            editorManager ?? Mock.Of<IEditorManager>());
    }

    private static XDocument LoadView(string fileName)
    {
        return XDocument.Load(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Views",
            "FolderProjectVersionControl",
            fileName));
    }

    private static FolderProjectCommitSummary Commit(
        string id,
        string title,
        string author,
        string description) =>
        new(
            id,
            title,
            author,
            $"{author}@example.invalid",
            DateTimeOffset.Parse("2026-08-02T10:00:00+08:00"),
            [])
        {
            Description = description,
        };

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

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-git-workspace-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }

    private sealed class DirectGitOperationCoordinator :
        IFolderProjectGitOperationCoordinator
    {
        public T Execute<T>(
            string projectRoot,
            Func<T> detachedOperation,
            bool openWhenComplete = false) => detachedOperation();

        public Task<T> ExecuteAsync<T>(
            string projectRoot,
            Func<T> detachedOperation,
            bool openWhenComplete = false) =>
            Task.FromResult(detachedOperation());
    }

    private sealed class GitTemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-git-picker-{Guid.NewGuid():N}");

        public GitTemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (!Directory.Exists(Path))
                return;

            foreach (var file in Directory.EnumerateFiles(
                         Path,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Path, true);
        }
    }

}
