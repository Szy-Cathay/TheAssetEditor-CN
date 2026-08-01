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
        var service = new Mock<IFolderProjectVersionControlService>();
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

}
