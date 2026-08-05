using System.Xml.Linq;
using AssetEditor.Services;
using AssetEditor.ViewModels;
using AssetEditor.Views.FolderProjectVersionControl;
using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Ui.Common.Behaviors;
using Shared.Ui.Common.OperationProgress;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        workspace.ShowGitManagement();
        await versionControl.RefreshCommand.ExecutionTask!;
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
    public async Task SelectingGitManagement_LoadsOnceAndReusesSnapshot()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var workspace = CreateWorkspace(
            out var versionControl,
            out var service);
        var statusReads = 0;
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(
                () =>
                {
                    statusReads++;
                    return new FolderProjectRepositoryStatus(
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
                        ]);
                });
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
        workspace.SetEditableContainer(project);

        NUnitAssert.That(statusReads, Is.Zero);

        workspace.SelectedSidebarTabIndex = 1;
        if (versionControl.RefreshCommand.ExecutionTask != null)
            await versionControl.RefreshCommand.ExecutionTask;
        workspace.SelectedSidebarTabIndex = 0;
        workspace.SelectedSidebarTabIndex = 1;
        if (versionControl.RefreshCommand.ExecutionTask != null)
            await versionControl.RefreshCommand.ExecutionTask;

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                versionControl.UnstagedChanges.Select(
                    item => item.RepositoryPath),
                Is.EqualTo(new[] { "db\\units_tables\\changed.bin" }));
            NUnitAssert.That(statusReads, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task VisibleGitManagement_RefreshesOnlyChangedFolderProjectPaths()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        Action<FolderProjectChangedEvent>? changed = null;
        var eventHub = new Mock<IGlobalEventHub>();
        eventHub.Setup(item => item.Register(
                It.IsAny<object>(),
                It.IsAny<Action<FolderProjectChangedEvent>>()))
            .Callback<object, Action<FolderProjectChangedEvent>>(
                (_, callback) => changed = callback);
        var workspace = CreateWorkspace(
            out var versionControl,
            out var service,
            eventHub: eventHub.Object);
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "master",
                    "1111111",
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []));
        service.Setup(item => item.GetIdentity(project.ProjectRoot))
            .Returns(new FolderProjectGitIdentity(
                "测试用户",
                "test@example.invalid"));
        service.Setup(item => item.GetBranches(project.ProjectRoot))
            .Returns(
                [new FolderProjectBranchInfo(
                    "master",
                    "1111111",
                    true,
                    true)]);
        service.Setup(item => item.GetStashes(project.ProjectRoot)).Returns([]);
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
        service.Setup(item => item.GetStatus(
                project.ProjectRoot,
                It.Is<IReadOnlyList<string>>(
                    paths => paths.SequenceEqual(new[] { "audio/changed.wem" }))))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "master",
                    "1111111",
                    false,
                    FolderProjectRepositoryOperationState.None,
                    [
                        new FolderProjectWorkingChange(
                            "audio/changed.wem",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));
        workspace.SetEditableContainer(project);
        workspace.ShowGitManagement();
        await versionControl.RefreshCommand.ExecutionTask!;
        service.Invocations.Clear();

        changed!(
            new FolderProjectChangedEvent(
                project,
                new FolderProjectChangeSet(
                    1,
                    [
                        new FolderProjectFileChange(
                            "audio/changed.wem",
                            FolderProjectFileChangeKind.Updated,
                            PackFile.CreateFromBytes("changed.wem", [1])),
                    ])));
        await workspace.WorkingChangesRefreshTask;

        NUnitAssert.That(
            versionControl.WorkingChanges.Select(item => item.RepositoryPath),
            Is.EqualTo(new[] { "audio/changed.wem" }));
        service.Verify(
            item => item.GetStatus(project.ProjectRoot),
            Times.Never);
        service.Verify(
            item => item.GetStatus(
                project.ProjectRoot,
                It.Is<IReadOnlyList<string>>(
                    paths => paths.SequenceEqual(
                        new[] { "audio/changed.wem" }))),
            Times.Once);
    }

    [Test]
    public async Task LargeFolderProjectChangeBatch_UsesOneFullStatusRefresh()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        Action<FolderProjectChangedEvent>? changed = null;
        var eventHub = new Mock<IGlobalEventHub>();
        eventHub.Setup(item => item.Register(
                It.IsAny<object>(),
                It.IsAny<Action<FolderProjectChangedEvent>>()))
            .Callback<object, Action<FolderProjectChangedEvent>>(
                (_, callback) => changed = callback);
        var workspace = CreateWorkspace(
            out var versionControl,
            out var service,
            eventHub: eventHub.Object);
        var fullStatusReads = 0;
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(
                () =>
                {
                    fullStatusReads++;
                    return new FolderProjectRepositoryStatus(
                        true,
                        "master",
                        "1111111",
                        false,
                        FolderProjectRepositoryOperationState.None,
                        []);
                });
        service.Setup(item => item.GetIdentity(project.ProjectRoot))
            .Returns(new FolderProjectGitIdentity(
                "测试用户",
                "test@example.invalid"));
        service.Setup(item => item.GetBranches(project.ProjectRoot))
            .Returns(
                [new FolderProjectBranchInfo(
                    "master",
                    "1111111",
                    true,
                    true)]);
        service.Setup(item => item.GetStashes(project.ProjectRoot)).Returns([]);
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
        workspace.SetEditableContainer(project);
        workspace.ShowGitManagement();
        await versionControl.RefreshCommand.ExecutionTask!;
        fullStatusReads = 0;
        service.Invocations.Clear();
        var file = PackFile.CreateFromBytes("changed.wem", [1]);
        var changes = Enumerable.Range(0, 513)
            .Select(
                index => new FolderProjectFileChange(
                    $"audio/changed-{index}.wem",
                    FolderProjectFileChangeKind.Updated,
                    file))
            .ToList();

        changed!(
            new FolderProjectChangedEvent(
                project,
                new FolderProjectChangeSet(1, changes)));
        await versionControl.RefreshCommand.ExecutionTask!;

        NUnitAssert.That(fullStatusReads, Is.EqualTo(1));
        service.Verify(
            item => item.GetStatus(
                project.ProjectRoot,
                It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
    }

    [Test]
    public void Refresh_KeepsPreviousSnapshotBrowsableAndBlocksMutations()
    {
        using var directory = new TemporaryDirectory();
        _ = CreateWorkspace(
            out var versionControl,
            out var service);
        versionControl.ProjectRoot = directory.Path;
        versionControl.IsInitialized = true;
        versionControl.HasIdentity = true;
        var oldChange = new FolderProjectWorkingChangeRow(
            new FolderProjectWorkingChange(
                "audio/old.wem",
                FolderProjectWorkingChangeKind.Modified |
                FolderProjectWorkingChangeKind.Unstaged),
            LocalizationManager.Instance);
        var oldCommit = Commit(
            new string('1', 40),
            "旧提交",
            "测试用户",
            "旧快照");
        versionControl.WorkingChanges.Add(oldChange);
        versionControl.UnstagedChanges.Add(oldChange);
        versionControl.History.Add(oldCommit);

        var refreshStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRefresh = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.Setup(item => item.GetStatus(directory.Path))
            .Returns(() =>
            {
                refreshStarted.TrySetResult();
                allowRefresh.Task.GetAwaiter().GetResult();
                return new FolderProjectRepositoryStatus(
                    false,
                    null,
                    null,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []);
            });

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var refreshTask = versionControl.Refresh();
            NUnitAssert.That(
                refreshStarted.Task.Wait(TimeSpan.FromSeconds(5)),
                Is.True);

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(versionControl.IsStatusRefreshing, Is.True);
                NUnitAssert.That(versionControl.IsBusy, Is.False);
                NUnitAssert.That(versionControl.StatusMessage,
                    Is.EqualTo(LocalizationManager.Instance.Get(
                        "FolderProject.VersionControl.Busy.Refreshing")));
                NUnitAssert.That(versionControl.WorkingChanges,
                    Is.EqualTo(new[] { oldChange }));
                NUnitAssert.That(versionControl.History,
                    Is.EqualTo(new[] { oldCommit }));
                NUnitAssert.That(
                    versionControl.StageAllCommand.CanExecute(null),
                    Is.False);
            });

            allowRefresh.TrySetResult();
            NUnitAssert.That(
                refreshTask.Wait(TimeSpan.FromSeconds(5)),
                Is.True);
            NUnitAssert.That(
                versionControl.IsStatusRefreshing,
                Is.False);
        }
        finally
        {
            allowRefresh.TrySetResult();
            SynchronizationContext.SetSynchronizationContext(
                previousContext);
        }
    }

    [Test]
    public void WorkingChangeRow_UsesConciseBadgeAndKeepsFullDescription()
    {
        var row = new FolderProjectWorkingChangeRow(
            new FolderProjectWorkingChange(
                "draft-note.txt",
                FolderProjectWorkingChangeKind.Added |
                FolderProjectWorkingChangeKind.Untracked |
                FolderProjectWorkingChangeKind.Unstaged),
            LocalizationManager.Instance);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(row.KindSummaryText, Is.EqualTo("新增"));
            NUnitAssert.That(
                row.KindText,
                Is.EqualTo("新增、未跟踪、未暂存"));
        });
    }

    [Test]
    public void WorkingChangeTree_GroupsFilesByRepositoryFolders()
    {
        var rows = new[]
        {
            new FolderProjectWorkingChangeRow(
                new FolderProjectWorkingChange(
                    "AssetEditor/ViewModels/FolderProjectVersionControlViewModel.cs",
                    FolderProjectWorkingChangeKind.Modified |
                    FolderProjectWorkingChangeKind.Unstaged),
                LocalizationManager.Instance),
            new FolderProjectWorkingChangeRow(
                new FolderProjectWorkingChange(
                    "AssetEditor\\Views\\FolderProjectVersionControl\\FolderProjectGitPanelView.xaml",
                    FolderProjectWorkingChangeKind.Modified |
                    FolderProjectWorkingChangeKind.Unstaged),
                LocalizationManager.Instance),
            new FolderProjectWorkingChangeRow(
                new FolderProjectWorkingChange(
                    "README.md",
                    FolderProjectWorkingChangeKind.Modified |
                    FolderProjectWorkingChangeKind.Unstaged),
                LocalizationManager.Instance),
        };

        var tree = FolderProjectWorkingChangeTreeNode.Build(
            @"D:\TheAssetEditor-CN",
            rows);
        var root = tree.Single();
        var assetEditor = root.Children.Single(item =>
            item.Name == "AssetEditor");
        var views = assetEditor.Children.Single(item =>
            item.Name == "Views");
        var folderProject = views.Children.Single(item =>
            item.Name == "FolderProjectVersionControl");
        var panel = folderProject.Children.Single();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(root.IsRoot, Is.True);
            NUnitAssert.That(root.Name, Is.EqualTo(@"D:\TheAssetEditor-CN"));
            NUnitAssert.That(
                root.Children.Select(item => item.Name),
                Is.EqualTo(new[] { "AssetEditor", "README.md" }));
            NUnitAssert.That(root.IsExpanded, Is.True);
            NUnitAssert.That(assetEditor.IsExpanded, Is.True);
            NUnitAssert.That(assetEditor.IsFolder, Is.True);
            NUnitAssert.That(panel.IsFolder, Is.False);
            NUnitAssert.That(
                panel.Name,
                Is.EqualTo("FolderProjectGitPanelView.xaml"));
            NUnitAssert.That(
                panel.Change?.RepositoryPath,
                Is.EqualTo(
                    "AssetEditor\\Views\\FolderProjectVersionControl\\FolderProjectGitPanelView.xaml"));
        });
    }

    [Test]
    public void ChangeRows_ExposeVsStatusLettersAndDeletedState()
    {
        var added = new FolderProjectWorkingChangeRow(
            new FolderProjectWorkingChange(
                "added.txt",
                FolderProjectWorkingChangeKind.Added |
                FolderProjectWorkingChangeKind.Unstaged),
            LocalizationManager.Instance);
        var modified = new FolderProjectWorkingChangeRow(
            new FolderProjectWorkingChange(
                "modified.txt",
                FolderProjectWorkingChangeKind.Modified |
                FolderProjectWorkingChangeKind.Unstaged),
            LocalizationManager.Instance);
        var deleted = new FolderProjectWorkingChangeRow(
            new FolderProjectWorkingChange(
                "deleted.txt",
                FolderProjectWorkingChangeKind.Deleted |
                FolderProjectWorkingChangeKind.Unstaged),
            LocalizationManager.Instance);
        var statusGlyph = typeof(FolderProjectWorkingChangeRow)
            .GetProperty("StatusGlyph");
        var isDeleted = typeof(FolderProjectWorkingChangeRow)
            .GetProperty("IsDeleted");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(statusGlyph, Is.Not.Null);
            NUnitAssert.That(isDeleted, Is.Not.Null);
            NUnitAssert.That(statusGlyph?.GetValue(added), Is.EqualTo("A"));
            NUnitAssert.That(statusGlyph?.GetValue(modified), Is.EqualTo("M"));
            NUnitAssert.That(statusGlyph?.GetValue(deleted), Is.EqualTo(""));
            NUnitAssert.That(isDeleted?.GetValue(deleted), Is.True);
        });
    }

    [Test]
    public void CommitChangeTree_GroupsFilesAndExposesFolderDescendants()
    {
        var rows = new[]
        {
            new FolderProjectCommitChangeRow(
                new FolderProjectCommitChange(
                    "src/one.txt",
                    null,
                    FolderProjectCommitChangeKind.Modified,
                    false),
                LocalizationManager.Instance),
            new FolderProjectCommitChangeRow(
                new FolderProjectCommitChange(
                    "src/two.txt",
                    null,
                    FolderProjectCommitChangeKind.Added,
                    false),
                LocalizationManager.Instance),
        };
        var nodeType = typeof(FolderProjectCommitChangeRow).Assembly.GetType(
            "AssetEditor.ViewModels.FolderProjectCommitChangeTreeNode");

        NUnitAssert.That(nodeType, Is.Not.Null);
        var build = nodeType!.GetMethod("Build");
        NUnitAssert.That(build, Is.Not.Null);
        var tree = (System.Collections.IEnumerable)build!.Invoke(
            null,
            [@"C:\projects\test", rows])!;
        var root = tree.Cast<object>().Single();
        var children = (System.Collections.IEnumerable)nodeType
            .GetProperty("Children")!.GetValue(root)!;
        var folder = children.Cast<object>().Single();
        var descendants = (System.Collections.IEnumerable)nodeType
            .GetProperty("Changes")!.GetValue(folder)!;
        var isExpanded = nodeType.GetProperty("IsExpanded");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(descendants.Cast<object>(), Has.Count.EqualTo(2));
            NUnitAssert.That(isExpanded?.GetValue(root), Is.True);
            NUnitAssert.That(isExpanded?.GetValue(folder), Is.True);
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
            NUnitAssert.That(
                namedElements["DetailsSplitter"].Attribute("Cursor")?.Value,
                Is.EqualTo("SizeNS"));
            NUnitAssert.That(
                namedElements["HistoryRow"].Attribute("Height")?.Value,
                Is.EqualTo("*"));
            NUnitAssert.That(
                namedElements["DetailsRow"].Attribute("Height")?.Value,
                Is.EqualTo("*"));
            NUnitAssert.That(
                namedElements["HistoryGrid"].Attribute("RowHeight")?.Value,
                Is.EqualTo("26"));
            NUnitAssert.That(
                namedElements["HistoryGrid"].Attribute("RowStyle")?.Value,
                Is.EqualTo("{StaticResource GitHistoryDataGridRowStyle}"));
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
            Mock.Of<IEditorManager>(),
            Mock.Of<IFolderProjectVersionControlWindowService>());
        workspace.SetEditableContainer(project);
        workspace.ShowGitManagement();
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task BranchPickerSelection_AfterRepositoryWasOpened_SwitchesOnFirstClick(
        bool closeRepositoryBeforeSwitch)
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
            Mock.Of<IEditorManager>(),
            Mock.Of<IFolderProjectVersionControlWindowService>());
        workspace.SetEditableContainer(project);
        workspace.ShowGitManagement();
        await versionControl.RefreshCommand.ExecutionTask!;
        var repository = new FolderProjectGitRepositoryViewModel();
        repository.Open(workspace);
        if (versionControl.RefreshCommand.ExecutionTask is { } refreshTask)
            await refreshTask;
        await versionControl.HistoryLoadTask;
        var historyBranch = versionControl.Branches.Single(
            branch => branch.Name == "main");
        versionControl.SelectedHistoryBranch = historyBranch;
        await versionControl.HistoryLoadTask;
        if (closeRepositoryBeforeSwitch)
            repository.Close();
        var targetBranch = versionControl.Branches.Single(
            branch => branch.Name == "feature");
        var refreshingMessage = LocalizationManager.Instance.Get(
            "FolderProject.VersionControl.Busy.Refreshing");
        var refreshMessageCount = 0;
        versionControl.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(versionControl.BusyMessage) &&
                versionControl.BusyMessage == refreshingMessage)
            {
                refreshMessageCount++;
            }
        };

        await workspace.SwitchBranchCommand.ExecuteAsync(targetBranch);
        await versionControl.HistoryLoadTask;

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                service.GetStatus(directory.Path).CurrentBranch,
                Is.EqualTo("feature"));
            NUnitAssert.That(
                versionControl.SelectedHistoryBranch?.Name,
                Is.EqualTo("main"));
            NUnitAssert.That(
                versionControl.Branches.Single(branch => branch.IsCurrent).Name,
                Is.EqualTo("feature"));
            NUnitAssert.That(refreshMessageCount, Is.EqualTo(1));
        });
        if (!closeRepositoryBeforeSwitch)
            repository.Close();
    }

    [Test]
    public async Task RepositoryHistorySelection_BrowsesWithoutCheckout()
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
            Mock.Of<IEditorManager>(),
            Mock.Of<IFolderProjectVersionControlWindowService>());
        workspace.SetEditableContainer(project);
        workspace.ShowGitManagement();
        await versionControl.RefreshCommand.ExecutionTask!;
        var repository = new FolderProjectGitRepositoryViewModel();
        repository.Open(workspace);
        await versionControl.RefreshCommand.ExecutionTask!;

        versionControl.SelectedHistoryBranch =
            versionControl.Branches.Single(branch => branch.Name == "feature");
        await versionControl.HistoryLoadTask;

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                service.GetStatus(directory.Path).CurrentBranch,
                Is.EqualTo("main"));
            NUnitAssert.That(
                versionControl.SelectedBranch?.Name,
                Is.EqualTo("main"));
            NUnitAssert.That(
                versionControl.Branches.Single(branch => branch.IsCurrent).Name,
                Is.EqualTo("main"));
            NUnitAssert.That(
                versionControl.SelectedHistoryBranch?.Name,
                Is.EqualTo("feature"));
        });
        repository.Close();
    }

    [Test]
    public void BranchContextActions_UseExistingDeleteAndMergeWorkflows()
    {
        var windowService =
            new Mock<IFolderProjectVersionControlWindowService>();
        var workspace = CreateWorkspace(
            out var versionControl,
            windowService: windowService.Object);
        versionControl.ProjectRoot = @"D:\Projects\Test";
        versionControl.ProjectName = "测试工程";
        versionControl.IsInitialized = true;
        versionControl.IsClean = true;
        versionControl.OperationState =
            FolderProjectRepositoryOperationState.None;
        var primaryCurrent = new FolderProjectBranchInfo(
            "main",
            "1111111",
            true,
            true);
        var primaryNotCurrent = new FolderProjectBranchInfo(
            "main",
            "1111111",
            false,
            true);
        var currentFeature = new FolderProjectBranchInfo(
            "current-feature",
            "2222222",
            true);
        var feature = new FolderProjectBranchInfo(
            "feature",
            "3333333",
            false);

        versionControl.SelectedBranch = currentFeature;
        var versionControlCanDeleteCurrent =
            versionControl.DeleteBranchCommand.CanExecute(null);
        versionControl.SelectedBranch = primaryNotCurrent;
        var versionControlCanDeletePrimary =
            versionControl.DeleteBranchCommand.CanExecute(null);
        if (workspace.MergeBranchCommand.CanExecute(currentFeature))
            workspace.MergeBranchCommand.Execute(currentFeature);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                workspace.MergeBranchCommand.CanExecute(primaryCurrent),
                Is.False);
            NUnitAssert.That(
                workspace.MergeBranchCommand.CanExecute(primaryNotCurrent),
                Is.False);
            NUnitAssert.That(
                workspace.DeleteBranchCommand.CanExecute(primaryCurrent),
                Is.False);
            NUnitAssert.That(
                workspace.DeleteBranchCommand.CanExecute(primaryNotCurrent),
                Is.False);
            NUnitAssert.That(
                workspace.MergeBranchCommand.CanExecute(currentFeature),
                Is.True);
            NUnitAssert.That(
                workspace.DeleteBranchCommand.CanExecute(currentFeature),
                Is.True);
            NUnitAssert.That(
                workspace.MergeBranchCommand.CanExecute(feature),
                Is.True);
            NUnitAssert.That(
                workspace.DeleteBranchCommand.CanExecute(feature),
                Is.True);
            NUnitAssert.That(versionControlCanDeleteCurrent, Is.True);
            NUnitAssert.That(versionControlCanDeletePrimary, Is.False);
        });
        windowService.Verify(service => service.ShowMergeDialog(
            @"D:\Projects\Test",
            "测试工程",
            "current-feature"), Times.Once);
    }

    [Test]
    public void BranchLists_ExposeDeleteAndMergeContextMenus()
    {
        var panel = LoadView("FolderProjectGitPanelView.xaml");
        var repository = LoadView("FolderProjectGitRepositoryView.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var panelCommands = panel
            .Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Command")?.Value)
            .Where(value => value != null)
            .ToArray();
        var repositoryCommands = repository
            .Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Command")?.Value)
            .Where(value => value != null)
            .ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                panelCommands,
                Does.Contain(
                    "{Binding Data.DeleteBranchCommand, Source={StaticResource WorkspaceProxy}}"));
            NUnitAssert.That(
                panelCommands,
                Does.Contain(
                    "{Binding Data.MergeBranchCommand, Source={StaticResource WorkspaceProxy}}"));
            NUnitAssert.That(
                repositoryCommands,
                Does.Contain(
                    "{Binding Data.Workspace.DeleteBranchCommand, Source={StaticResource WorkspaceProxy}}"));
            NUnitAssert.That(
                repositoryCommands,
                Does.Contain(
                    "{Binding Data.Workspace.MergeBranchCommand, Source={StaticResource WorkspaceProxy}}"));
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
        var branchButton = branchList
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Command")?.Value.Contains(
                    "SwitchBranchCommand") == true);
        var createButton = document.Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Command")?.Value.Contains(
                    "CreateAndSwitchBranchCommand") == true);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                stretchSetter?.Attribute("Value")?.Value,
                Is.EqualTo("Stretch"));
            NUnitAssert.That(
                branchButton.Attribute("Style")?.Value,
                Is.EqualTo("{StaticResource GitBranchPickerButtonStyle}"));
            NUnitAssert.That(
                createButton.Attribute("Style")?.Value,
                Is.EqualTo("{StaticResource GitTextLinkButtonStyle}"));
        });
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
    public void RepositoryBranchMarker_TracksSelectedHistoryBranch()
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
        var branchMarker = branchList
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "MaterialIcon" &&
                element.Attribute("Kind")?.Value == "Check");

        NUnitAssert.That(
            branchMarker.Attribute("Visibility")?.Value,
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
                Is.Null);
            NUnitAssert.That(
                unstageAll?.Attribute("Background")?.Value,
                Is.Null);
            NUnitAssert.That(
                stageAll?.Ancestors(presentation + "Expander")
                    .Single()
                    .Attribute("Style")?.Value,
                Is.EqualTo("{StaticResource GitSectionExpanderStyle}"));
        });
    }

    [Test]
    public void GitManagementViews_UseSharedVisualSystemAndMaterialIcons()
    {
        var solutionRoot = FindSolutionRoot();
        using var localization = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(solutionRoot, "AssetEditor", "Language_Cn.json")));
        var panel = LoadView("FolderProjectGitPanelView.xaml");
        var repository = LoadView("FolderProjectGitRepositoryView.xaml");
        var documents = new[] { panel, repository };
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var textSymbols = new HashSet<string>
        {
            "+",
            "−",
            "▼",
            "●",
            "⌄",
        };

        var sharedDictionaries = documents.Select(document =>
            document.Descendants(presentation + "ResourceDictionary")
                .SingleOrDefault(element =>
                    element.Attribute("Source")?.Value ==
                    "FolderProjectGitStyles.xaml"));
        var obsoleteSymbols = documents
            .SelectMany(document => document.Descendants())
            .SelectMany(element => element.Attributes())
            .Where(attribute =>
                attribute.Name.LocalName is "Content" or "Header" or "Text")
            .Where(attribute => textSymbols.Contains(attribute.Value))
            .ToArray();
        var commitButton = panel
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value == "CommitButton");
        var newLocalizationKeys = new[]
        {
            "FolderProject.Git.CurrentBranch",
            "FolderProject.Git.CommitChanges",
            "FolderProject.Git.CommitMessageHint",
            "FolderProject.Git.CommitOptions",
            "FolderProject.Git.NoUnstagedChanges",
            "FolderProject.Git.NoStagedChanges",
            "FolderProject.Git.NoStashes",
        };

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(sharedDictionaries, Is.All.Not.Null);
            NUnitAssert.That(
                documents.SelectMany(document => document.Descendants())
                    .Count(element =>
                        element.Name.LocalName == "MaterialIcon"),
                Is.GreaterThanOrEqualTo(8));
            NUnitAssert.That(obsoleteSymbols, Is.Empty);
            NUnitAssert.That(
                commitButton.Attribute("Style")?.Value,
                Is.EqualTo(
                    "{StaticResource GitCommitSplitMainButtonStyle}"));
            NUnitAssert.That(
                newLocalizationKeys.All(key =>
                    localization.RootElement.TryGetProperty(key, out _)),
                Is.True);
        });
    }

    [Test]
    public void GitIconButtons_UseThemeForeground()
    {
        var styles = LoadView("FolderProjectGitStyles.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var iconButtonStyle = styles
            .Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "GitIconButtonStyle");
        NUnitAssert.That(
            iconButtonStyle.Attribute("BasedOn")?.Value,
            Is.EqualTo("{StaticResource AeButton.Icon}"));
    }

    [Test]
    public void GitPanel_UsesCompactUnifiedCommitSplitButton()
    {
        var panel = LoadView("FolderProjectGitPanelView.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var splitButton = panel.Descendants(presentation + "Border")
            .SingleOrDefault(element =>
                element.Attribute(xaml + "Name")?.Value ==
                "CommitSplitButton");
        var commitButton = splitButton?
            .Descendants(presentation + "Button")
            .SingleOrDefault(element =>
                element.Attribute(xaml + "Name")?.Value == "CommitButton");
        var optionsButton = splitButton?
            .Descendants(presentation + "Button")
            .SingleOrDefault(element =>
                element.Attribute(xaml + "Name")?.Value ==
                "CommitOptionsButton");
        var menuCommands = optionsButton?
            .Descendants(presentation + "MenuItem")
            .Select(element => element.Attribute("Command")?.Value)
            .Where(value => value != null)
            .ToArray();
        var styles = LoadView("FolderProjectGitStyles.xaml");
        var splitStyle = styles.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "GitCommitSplitButtonStyle");
        var partStyle = styles.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "GitCommitSplitButtonPartStyle");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(splitButton, Is.Not.Null);
            NUnitAssert.That(
                splitButton?.Attribute("HorizontalAlignment")?.Value,
                Is.EqualTo("Left"));
            NUnitAssert.That(splitButton?.Attribute("Width"), Is.Null);
            NUnitAssert.That(
                splitStyle.Elements(presentation + "Setter")
                    .Single(element =>
                        element.Attribute("Property")?.Value == "Background")
                    .Attribute("Value")?.Value,
                Is.EqualTo("{DynamicResource AeBrush.Accent}"));
            NUnitAssert.That(
                partStyle.Elements(presentation + "Setter")
                    .Single(element =>
                        element.Attribute("Property")?.Value == "Height")
                    .Attribute("Value")?.Value,
                Is.EqualTo("26"));
            NUnitAssert.That(
                partStyle.Elements(presentation + "Setter")
                    .Single(element =>
                        element.Attribute("Property")?.Value ==
                        "FocusVisualStyle")
                    .Attribute("Value")?.Value,
                Is.EqualTo("{x:Null}"));
            NUnitAssert.That(
                partStyle.Descendants(presentation + "Trigger").Any(
                    element =>
                        element.Attribute("Property")?.Value ==
                        "IsKeyboardFocused"),
                Is.False);
            NUnitAssert.That(
                partStyle.ToString(),
                Does.Contain("To=\"1.015\""));
            NUnitAssert.That(
                partStyle.ToString(),
                Does.Contain("To=\"0.94\""));
            NUnitAssert.That(
                commitButton?.Attribute("Command")?.Value,
                Is.EqualTo("{Binding VersionControl.CommitCommand}"));
            NUnitAssert.That(
                optionsButton?.Attribute("Click")?.Value,
                Is.EqualTo("CommitOptionsButton_Click"));
            NUnitAssert.That(
                menuCommands,
                Is.EquivalentTo(new[]
                {
                    "{Binding VersionControl.CommitStagedCommand}",
                    "{Binding VersionControl.CommitAllCommand}",
                }));
        });
    }

    [Test]
    public void GitManagementViews_ExplainInputsAndUseChineseDateLayout()
    {
        var panel = LoadView("FolderProjectGitPanelView.xaml");
        var repository = LoadView("FolderProjectGitRepositoryView.xaml");
        var documents = new[] { panel, repository };
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var watermarks = documents
            .SelectMany(document => document.Descendants())
            .SelectMany(element => element.Attributes())
            .Where(attribute =>
                attribute.Name.LocalName == "TextBoxExtensions.Watermark")
            .ToArray();
        var watermarkedTextBoxes = documents
            .SelectMany(document =>
                document.Descendants(presentation + "TextBox"))
            .Where(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "TextBoxExtensions.Watermark" &&
                (attribute.Value.Contains("SearchBranches") ||
                 attribute.Value.Contains("SearchHistory"))))
            .ToArray();
        var localizedDates = repository
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute =>
                attribute.Value.Contains("yyyy-MM-dd HH:mm"))
            .ToArray();
        var projectPath = repository
            .Descendants(presentation + "TextBlock")
            .Single(element =>
                element.Attribute("Text")?.Value ==
                "{Binding VersionControl.ProjectRoot}");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(watermarks, Has.Length.EqualTo(4));
            NUnitAssert.That(
                watermarkedTextBoxes.Select(element =>
                    element.Attribute("VerticalContentAlignment")?.Value),
                Is.All.EqualTo("Center"));
            NUnitAssert.That(localizedDates, Has.Length.EqualTo(2));
            NUnitAssert.That(
                projectPath.Attribute("TextWrapping")?.Value,
                Is.EqualTo("NoWrap"));
        });
    }

    [Test]
    public void SearchWatermark_UsesTextBoxVerticalContentAlignment()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(
                typeof(LocalizationManager)))
            .Returns(LocalizationManager.Instance);
        WpfTestApplicationHost.InvokeWithThemeResources(
            serviceProvider.Object,
            () =>
            {
                var textBox = new TextBox
                {
                    Width = 180,
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                textBox.Measure(new Size(180, 28));
                textBox.Arrange(new Rect(0, 0, 180, 28));

                TextBoxExtensions.SetWatermark(textBox, "搜索分支");

                var brush = textBox.Background as VisualBrush;
                var grid = brush?.Visual as Grid;
                var label = grid?.Children.OfType<Label>().SingleOrDefault();
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(label, Is.Not.Null);
                    NUnitAssert.That(
                        label?.VerticalAlignment,
                        Is.EqualTo(VerticalAlignment.Center));
                    NUnitAssert.That(
                        label?.VerticalContentAlignment,
                        Is.EqualTo(VerticalAlignment.Center));
                });
            });
    }

    [Test]
    public void GitManagementPanel_UsesFolderTreesForWorkingChanges()
    {
        var panel = LoadView("FolderProjectGitPanelView.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var trees = panel.Descendants(presentation + "TreeView")
            .ToDictionary(
                element => element.Attribute(xaml + "Name")?.Value ?? "");
        var nodeTemplate = panel
            .Descendants(presentation + "HierarchicalDataTemplate")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "GitChangeTreeNodeTemplate");
        var nodeName = nodeTemplate
            .Descendants(presentation + "TextBlock")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value == "NodeName");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                nodeTemplate.Attribute("ItemContainerStyle")?.Value,
                Is.EqualTo("{StaticResource GitChangeTreeItemStyle}"));
            NUnitAssert.That(
                nodeName.Attribute("Grid.Column")?.Value,
                Is.EqualTo("1"));
            NUnitAssert.That(
                trees["UnstagedChangesTree"].Attribute("ItemsSource")?.Value,
                Is.EqualTo("{Binding VersionControl.UnstagedChangeTree}"));
            NUnitAssert.That(
                trees["StagedChangesTree"].Attribute("ItemsSource")?.Value,
                Is.EqualTo("{Binding VersionControl.StagedChangeTree}"));
            NUnitAssert.That(
                nodeTemplate.Descendants()
                    .Any(element =>
                        element.Attribute("Command")?.Value.Contains(
                            "StageTreeNodeCommand",
                            StringComparison.Ordinal) == true),
                Is.True);
            NUnitAssert.That(
                nodeTemplate.Descendants()
                    .Any(element =>
                        element.Attribute("Command")?.Value.Contains(
                            "UnstageTreeNodeCommand",
                            StringComparison.Ordinal) == true),
                Is.True);
            NUnitAssert.That(
                nodeTemplate.Descendants()
                    .Any(element =>
                        element.Attribute("Command")?.Value.Contains(
                            "DiscardTreeNodeCommand",
                            StringComparison.Ordinal) == true),
                Is.True);
        });
    }

    [Test]
    public void GitChangeTreeStyle_VirtualizesLargeFolders()
    {
        var styles = LoadView("FolderProjectGitStyles.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var treeStyle = styles.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "GitChangeTreeStyle");
        var setters = treeStyle.Elements(presentation + "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value ?? "",
                element => element.Attribute("Value")?.Value ?? "");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                setters["VirtualizingPanel.IsVirtualizing"],
                Is.EqualTo("True"));
            NUnitAssert.That(
                setters["VirtualizingPanel.VirtualizationMode"],
                Is.EqualTo("Recycling"));
            NUnitAssert.That(
                setters["ScrollViewer.CanContentScroll"],
                Is.EqualTo("True"));
        });
    }

    [Test]
    public void GitRepository_UsesCommitTreeWithFileLevelResetAndRevert()
    {
        var repository = LoadView("FolderProjectGitRepositoryView.xaml");
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var tree = repository.Descendants(presentation + "TreeView")
            .Single(element =>
                element.Attribute("ItemsSource")?.Value ==
                "{Binding VersionControl.CommitChangeTree}");
        var nodeTemplate = repository
            .Descendants(presentation + "HierarchicalDataTemplate")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "GitCommitChangeTreeNodeTemplate");
        var commands = nodeTemplate.Descendants()
            .Select(element => element.Attribute("Command")?.Value)
            .Where(value => value != null)
            .ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                tree.Attribute("ItemTemplate")?.Value,
                Is.EqualTo(
                    "{StaticResource GitCommitChangeTreeNodeTemplate}"));
            NUnitAssert.That(
                commands.Any(value => value!.Contains(
                    "ResetCommitChangesKeepCommand",
                    StringComparison.Ordinal)),
                Is.True);
            NUnitAssert.That(
                commands.Any(value => value!.Contains(
                    "ResetCommitChangesAndDiscardCommand",
                    StringComparison.Ordinal)),
                Is.True);
            NUnitAssert.That(
                commands.Any(value => value!.Contains(
                    "RevertCommitChangesCommand",
                    StringComparison.Ordinal)),
                Is.True);
            NUnitAssert.That(
                commands.Any(value =>
                    value!.Contains("RestoreFileCommand") ||
                    value.Contains("DiscardCommitChangesCommand") ||
                    value.Contains("RestoreCommitChangesToStageCommand") ||
                    value.Contains("ReturnChangesToOriginalCommitCommand")),
                Is.False);
        });
    }

    [Test]
    public void GitManagementLoadingStates_BlockTheUnderlyingInterface()
    {
        var overlays = new[]
        {
            (Document: LoadView("FolderProjectGitPanelView.xaml"),
                Name: "GitPanelBusyOverlay"),
            (Document: LoadView("FolderProjectGitRepositoryView.xaml"),
                Name: "RepositoryBusyOverlay"),
        };
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        NUnitAssert.Multiple(() =>
        {
            foreach (var overlay in overlays)
            {
                var element = overlay.Document
                    .Descendants(presentation + "Grid")
                    .Single(candidate =>
                        candidate.Attribute(xaml + "Name")?.Value ==
                        overlay.Name);
                NUnitAssert.That(
                    element.Attribute("Background")?.Value,
                    Is.EqualTo("Transparent"));
                var backdrop = element.Elements(presentation + "Border")
                    .First();
                NUnitAssert.That(
                    backdrop.Attribute("Background")?.Value,
                    Is.EqualTo("{DynamicResource AeBrush.Canvas}"));
                NUnitAssert.That(
                    backdrop.Attribute("Opacity")?.Value,
                    Is.EqualTo("0.86"));
                NUnitAssert.That(
                    element.Attribute("IsHitTestVisible")?.Value,
                    Does.Contain("IsLoadingOperation"));
            }
        });
    }

    [Test]
    public void GitBackgroundLoads_ShowBlockingProgressOverlays()
    {
        var workspace = CreateWorkspace(out var versionControl);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(
                typeof(LocalizationManager)))
            .Returns(LocalizationManager.Instance);

        WpfTestApplicationHost.InvokeWithThemeResources(
            serviceProvider.Object,
            () =>
            {
                versionControl.IsStatusRefreshing = true;
                var panel = new FolderProjectGitPanelView
                {
                    DataContext = workspace,
                };
                panel.Measure(new Size(500, 700));
                panel.Arrange(new Rect(0, 0, 500, 700));
                panel.UpdateLayout();

                var panelOverlay = (Grid)panel.FindName(
                    "GitPanelBusyOverlay");
                var panelProgress = FindVisualDescendant<
                    OperationProgressView>(panelOverlay);

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        panelOverlay.Visibility,
                        Is.EqualTo(Visibility.Visible));
                    NUnitAssert.That(
                        panelOverlay.IsHitTestVisible,
                        Is.True);
                    NUnitAssert.That(
                        panelProgress?.IsOperationActive,
                        Is.True);
                    NUnitAssert.That(
                        panelProgress?.StatusText,
                        Is.EqualTo(LocalizationManager.Instance.Get(
                            "FolderProject.VersionControl.Busy.Refreshing")));
                });

                versionControl.IsStatusRefreshing = false;
                versionControl.IsCommitChangesLoading = true;
                var repository = new FolderProjectGitRepositoryView
                {
                    DataContext = workspace,
                };
                repository.Measure(new Size(900, 650));
                repository.Arrange(new Rect(0, 0, 900, 650));
                repository.UpdateLayout();

                var repositoryOverlay = (Grid)repository.FindName(
                    "RepositoryBusyOverlay");
                var repositoryProgress = FindVisualDescendant<
                    OperationProgressView>(repositoryOverlay);

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        repositoryOverlay.Visibility,
                        Is.EqualTo(Visibility.Visible));
                    NUnitAssert.That(
                        repositoryOverlay.IsHitTestVisible,
                        Is.True);
                    NUnitAssert.That(
                        repositoryProgress?.IsOperationActive,
                        Is.True);
                    NUnitAssert.That(
                        repositoryProgress?.StatusText,
                        Is.EqualTo(LocalizationManager.Instance.Get(
                            "FolderProject.VersionControl.Busy.LoadingCommit")));
                });
            });
    }

    [Test]
    public async Task RepositoryLoad_ShowsOnlyRepositoryProgressOverlay()
    {
        var workspace = CreateWorkspace(out var versionControl);
        var repositoryEditor = new FolderProjectGitRepositoryViewModel();
        repositoryEditor.Open(workspace);
        if (versionControl.RefreshCommand.ExecutionTask != null)
            await versionControl.RefreshCommand.ExecutionTask;
        versionControl.IsCommitChangesLoading = true;
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(
                typeof(LocalizationManager)))
            .Returns(LocalizationManager.Instance);

        WpfTestApplicationHost.InvokeWithThemeResources(
            serviceProvider.Object,
            () =>
            {
                var panel = new FolderProjectGitPanelView
                {
                    DataContext = workspace,
                };
                var repository = new FolderProjectGitRepositoryView
                {
                    DataContext = workspace,
                };
                panel.Measure(new Size(500, 700));
                panel.Arrange(new Rect(0, 0, 500, 700));
                repository.Measure(new Size(900, 650));
                repository.Arrange(new Rect(0, 0, 900, 650));
                panel.UpdateLayout();
                repository.UpdateLayout();

                var panelOverlay = (Grid)panel.FindName(
                    "GitPanelBusyOverlay");
                var repositoryOverlay = (Grid)repository.FindName(
                    "RepositoryBusyOverlay");
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        panelOverlay.Visibility,
                        Is.EqualTo(Visibility.Collapsed));
                    NUnitAssert.That(
                        panelOverlay.IsHitTestVisible,
                        Is.False);
                    NUnitAssert.That(
                        repositoryOverlay.Visibility,
                        Is.EqualTo(Visibility.Visible));
                    NUnitAssert.That(
                        repositoryOverlay.IsHitTestVisible,
                        Is.True);
                });

                repositoryEditor.Close();
                panel.UpdateLayout();

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        panelOverlay.Visibility,
                        Is.EqualTo(Visibility.Visible));
                    NUnitAssert.That(
                        panelOverlay.IsHitTestVisible,
                        Is.True);
                });
            });
    }

    [Test]
    public void GitManagementViews_LoadAndArrangeAtSupportedSizes()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(
                typeof(LocalizationManager)))
            .Returns(LocalizationManager.Instance);
        WpfTestApplicationHost.InvokeWithThemeResources(
            serviceProvider.Object,
            () =>
            {
                var views = new (FrameworkElement View, Size Size)[]
                {
                    (new FolderProjectGitPanelView(), new Size(260, 700)),
                    (new FolderProjectGitRepositoryView(),
                        new Size(900, 650)),
                };

                foreach (var (view, size) in views)
                {
                    view.Measure(size);
                    view.Arrange(new Rect(size));
                    view.UpdateLayout();

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(view.ActualWidth, Is.EqualTo(size.Width));
                        NUnitAssert.That(view.ActualHeight, Is.EqualTo(size.Height));
                    });
                }
            });
    }

    [Test]
    public void SidebarTabs_AlignWithEditorTabsWithoutDuplicateTitles()
    {
        var solutionRoot = FindSolutionRoot();
        var mainWindow = XDocument.Load(Path.Combine(
            solutionRoot,
            "AssetEditor",
            "Views",
            "MainWindow.xaml"));
        var shell = XDocument.Load(Path.Combine(
            solutionRoot,
            "AssetEditor",
            "Themes",
            "DesignSystem",
            "Shell.xaml"));
        var panel = LoadView("FolderProjectGitPanelView.xaml");
        var fileTree = XDocument.Load(Path.Combine(
            solutionRoot,
            "AssetEditor",
            "Views",
            "FileTreeView.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var editorHeader = shell.Descendants(presentation + "TabPanel")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value == "HeaderPanel");
        var sidebarTabs = mainWindow.Descendants(presentation + "TabControl")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value ==
                "WorkspaceSidebar");
        var packBrowser = fileTree.Descendants()
            .Single(element => element.Name.LocalName == "PackFileBrowserView");
        var workspaceStyle = shell.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "AeShell.WorkspaceSidebar");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                sidebarTabs.Attribute("Style")?.Value,
                Is.EqualTo("{StaticResource AeShell.WorkspaceSidebar}"));
            NUnitAssert.That(
                editorHeader.Attribute("Height")?.Value,
                Is.EqualTo("{StaticResource AeSize.TabHeight}"));
            NUnitAssert.That(
                workspaceStyle.Descendants(presentation + "TabPanel"),
                Is.Empty);
            NUnitAssert.That(
                panel.Descendants().Any(element =>
                    element.Attribute(xaml + "Name")?.Value ==
                    "GitSidebarHeader"),
                Is.False);
            NUnitAssert.That(
                packBrowser.Attribute("ShowTitle")?.Value,
                Is.EqualTo("False"));
        });
    }

    [Test]
    public void SidebarAndEditorTabBands_UseContinuousMatchingGeometry()
    {
        var solutionRoot = FindSolutionRoot();
        var mainWindow = XDocument.Load(Path.Combine(
            solutionRoot,
            "AssetEditor",
            "Views",
            "MainWindow.xaml"));
        var shell = XDocument.Load(Path.Combine(
            solutionRoot,
            "AssetEditor",
            "Themes",
            "DesignSystem",
            "Shell.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var editorTabs = mainWindow.Descendants()
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value ==
                "EditorsTabControl");
        var editorHeader = shell.Descendants(presentation + "TabPanel")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value == "HeaderPanel");
        var splitter = mainWindow.Descendants(presentation + "GridSplitter")
            .Single(element => element.Attribute("Grid.Column")?.Value == "1");
        var sidebarStyle = shell.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "AeShell.WorkspaceSidebar");
        var editorStyle = shell.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "AeShell.EditorTabs");
        var splitterStyle = shell.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "AeShell.Splitter");
        var borderThickness = sidebarStyle
            .Descendants(presentation + "Setter")
            .Single(element =>
                element.Attribute("Property")?.Value == "BorderThickness");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                borderThickness.Attribute("Value")?.Value,
                Is.EqualTo("0,0,1,0"));
            NUnitAssert.That(
                editorTabs.Attribute("Style")?.Value,
                Is.EqualTo("{StaticResource AeShell.EditorTabs}"));
            NUnitAssert.That(
                editorStyle.Descendants(presentation + "Setter").Any(
                    setter =>
                        setter.Attribute("Property")?.Value == "Margin" &&
                        setter.Attribute("Value")?.Value == "0"),
                Is.True);
            NUnitAssert.That(
                editorHeader.Attribute("Height")?.Value,
                Is.EqualTo("{StaticResource AeSize.TabHeight}"));
            NUnitAssert.That(
                editorHeader.Parent?.Attribute("Background")?.Value,
                Is.EqualTo("{DynamicResource AeBrush.Surface1}"));
            NUnitAssert.That(
                splitter.Attribute("Style")?.Value,
                Is.EqualTo("{StaticResource AeShell.Splitter}"));
            NUnitAssert.That(
                splitterStyle.Descendants(presentation + "Setter").Any(
                    setter =>
                        setter.Attribute("Property")?.Value == "Width" &&
                        setter.Attribute("Value")?.Value == "3"),
                Is.True);
            NUnitAssert.That(
                splitterStyle.Descendants(presentation + "Setter").Any(
                    setter =>
                        setter.Attribute("Property")?.Value == "Background" &&
                        setter.Attribute("Value")?.Value == "Transparent"),
                Is.True);
        });
    }

    [Test]
    public void EditorTabs_ReuseSidebarSegmentedStyleAndPreserveInteractions()
    {
        var mainWindow = XDocument.Load(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Views",
            "MainWindow.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var editorTabs = mainWindow.Descendants()
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value ==
                "EditorsTabControl");
        var itemContainerStyle = editorTabs.Descendants(
                presentation + "Style")
            .Single(element =>
                element.Attribute("TargetType")?.Value.Contains(
                    "TabItem",
                    StringComparison.Ordinal) == true);
        var eventNames = itemContainerStyle.Elements(
                presentation + "EventSetter")
            .Select(element => element.Attribute("Event")?.Value)
            .ToHashSet();
        var setters = itemContainerStyle.Elements(presentation + "Setter")
            .Where(element => element.Attribute("Property") != null)
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")?.Value);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                itemContainerStyle.Attribute("BasedOn")?.Value,
                Is.EqualTo("{StaticResource AeShell.EditorTabItem}"));
            NUnitAssert.That(
                eventNames,
                Is.SupersetOf(new[]
                {
                    "Drop",
                    "PreviewMouseMove",
                    "PreviewMouseDown",
                }));
            NUnitAssert.That(setters["AllowDrop"], Is.EqualTo("True"));
            NUnitAssert.That(setters["Focusable"], Is.EqualTo("False"));
            NUnitAssert.That(
                setters["behaviors:MouseMiddleClick.Command"],
                Does.Contain("CloseToolCommand"));
            NUnitAssert.That(
                setters["behaviors:MouseMiddleClick.CommandParameter"],
                Is.EqualTo("{Binding}"));
        });
    }

    [Test]
    public void SidebarWorkspaceTabs_StretchSelectedContentToAvailableWidth()
    {
        var shell = XDocument.Load(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Themes",
            "DesignSystem",
            "Shell.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = shell.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "AeShell.WorkspaceSidebar");
        var activityItemStyle = shell.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "AeShell.ActivityItem");
        var selectedContent = style.Descendants(
                presentation + "ContentPresenter")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value ==
                "PART_SelectedContentHost");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                style.Descendants(presentation + "Setter").Any(setter =>
                    setter.Attribute("Property")?.Value ==
                        "HorizontalContentAlignment" &&
                    setter.Attribute("Value")?.Value == "Stretch"),
                Is.True);
            NUnitAssert.That(
                style.Descendants(presentation + "Setter").Any(setter =>
                    setter.Attribute("Property")?.Value ==
                        "VerticalContentAlignment" &&
                    setter.Attribute("Value")?.Value == "Stretch"),
                Is.True);
            NUnitAssert.That(
                selectedContent.Attribute("HorizontalAlignment")?.Value,
                Is.EqualTo("{TemplateBinding HorizontalContentAlignment}"));
            NUnitAssert.That(
                selectedContent.Attribute("VerticalAlignment")?.Value,
                Is.EqualTo("{TemplateBinding VerticalContentAlignment}"));
            NUnitAssert.That(
                activityItemStyle.Descendants(presentation + "Setter").Any(
                    setter =>
                        setter.Attribute("Property")?.Value ==
                            "HorizontalContentAlignment" &&
                        setter.Attribute("Value")?.Value == "Center"),
                Is.True);
            NUnitAssert.That(
                activityItemStyle.Descendants(presentation + "Setter").Any(
                    setter =>
                        setter.Attribute("Property")?.Value ==
                            "VerticalContentAlignment" &&
                        setter.Attribute("Value")?.Value == "Center"),
                Is.True);
        });
    }

    [Test]
    public void SidebarTabs_UseFlatSegmentedThemeStates()
    {
        var solutionRoot = FindSolutionRoot();
        var shell = XDocument.Load(Path.Combine(
            solutionRoot,
            "AssetEditor",
            "Themes",
            "DesignSystem",
            "Shell.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = shell.Descendants(presentation + "Style")
            .Single(element =>
                element.Attribute(xaml + "Key")?.Value ==
                "AeShell.ActivityItem");
        var indicator = style.Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value ==
                "SelectionIndicator");
        var focusRing = style.Descendants(presentation + "Border")
            .Single(element =>
                element.Attribute(xaml + "Name")?.Value ==
                "FocusRing");
        var selectedTrigger = style.Descendants(presentation + "Trigger")
            .Single(element =>
                element.Attribute("Property")?.Value == "IsSelected" &&
                element.Attribute("Value")?.Value == "True");
        var hoverTrigger = style.Descendants(presentation + "Trigger")
            .Single(element =>
                element.Attribute("Property")?.Value == "IsMouseOver" &&
                element.Attribute("Value")?.Value == "True");
        var focusTrigger = style.Descendants(presentation + "Trigger")
            .Single(element =>
                element.Attribute("Property")?.Value ==
                "IsKeyboardFocused" &&
                element.Attribute("Value")?.Value == "True");
        var requiredThemeKeys = new[]
        {
            "AeBrush.Surface1",
            "AeBrush.SurfaceHover",
            "AeBrush.TextPrimary",
            "AeBrush.TextMuted",
            "AeBrush.Accent",
            "AeBrush.AccentSoft",
        };

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                indicator.Attribute("Width")?.Value,
                Is.EqualTo("2"));
            NUnitAssert.That(
                indicator.Attribute("Margin")?.Value,
                Is.EqualTo("0,4,0,4"));
            NUnitAssert.That(
                indicator.Attribute("Background")?.Value,
                Is.EqualTo("{DynamicResource AeBrush.Accent}"));
            NUnitAssert.That(
                selectedTrigger.Descendants(presentation + "Setter").Any(
                    setter =>
                        setter.Attribute("TargetName")?.Value ==
                            "SelectionIndicator" &&
                        setter.Attribute("Property")?.Value == "Visibility" &&
                        setter.Attribute("Value")?.Value == "Visible"),
                Is.True);
            NUnitAssert.That(
                selectedTrigger.Descendants(presentation + "Setter").Any(
                    setter =>
                        setter.Attribute("Property")?.Value == "Background" &&
                        setter.Attribute("Value")?.Value ==
                            "{DynamicResource AeBrush.AccentSoft}"),
                Is.True);
            NUnitAssert.That(
                hoverTrigger.Descendants(presentation + "Setter").Any(
                    setter =>
                        setter.Attribute("Property")?.Value == "Background" &&
                        setter.Attribute("Value")?.Value ==
                            "{DynamicResource AeBrush.SurfaceHover}"),
                Is.True);
            NUnitAssert.That(
                focusTrigger.Descendants(presentation + "Setter").Any(
                    setter =>
                        setter.Attribute("TargetName")?.Value == "FocusRing" &&
                        setter.Attribute("Property")?.Value == "Visibility" &&
                        setter.Attribute("Value")?.Value == "Visible"),
                Is.True);
            NUnitAssert.That(
                focusRing.Attribute("BorderBrush")?.Value,
                Is.EqualTo("{DynamicResource AeBrush.Accent}"));
            NUnitAssert.That(
                style.Descendants(presentation + "LinearGradientBrush"),
                Is.Empty);
            NUnitAssert.That(
                style.Descendants(presentation + "DropShadowEffect"),
                Is.Empty);
        });

        foreach (var themeName in new[] { "DarkTheme.xaml", "LightTheme.xaml" })
        {
            var theme = XDocument.Load(Path.Combine(
                solutionRoot,
                "AssetEditor",
                "Themes",
                "ColourDictionaries",
                themeName));
            var keys = theme.Descendants()
                .Select(element => element.Attribute(xaml + "Key")?.Value)
                .Where(value => value != null)
                .ToHashSet();
            NUnitAssert.That(
                requiredThemeKeys.All(keys.Contains),
                Is.True,
                themeName);
        }
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

    [Test]
    public void LegacyVersionControlWindow_OnlyPresentsMergeWorkflow()
    {
        var view = LoadView("FolderProjectVersionControlWindow.xaml");
        var source = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Views",
            "FolderProjectVersionControl",
            "FolderProjectVersionControlWindow.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var requiredBindings = new[]
        {
            "MergeSources",
            "SelectedMergeSource",
            "MergeTargets",
            "SelectedMergeTarget",
            "BeginMergeCommand",
            "AbortMergeCommand",
            "MergeSummary",
            "MergeConflicts",
            "UseCurrentCommand",
            "UseIncomingCommand",
            "CompleteMergeCommand",
        };
        var removedLegacySurfaceMarkers = new[]
        {
            "FolderProject.VersionControl.Tab.Status",
            "FolderProject.VersionControl.Tab.History",
            "FolderProject.VersionControl.Tab.Branches",
            "UnstagedChanges",
            "StagedChanges",
            "CommitCommand",
            "CreateBranchCommand",
            "DeleteBranchCommand",
            "ApplyStashCommand",
            "SelectedStash",
        };

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                view.Root?.Attribute("Title")?.Value,
                Is.EqualTo(
                    "{loc:Loc FolderProject.VersionControl.Tab.Merge}"));
            NUnitAssert.That(
                view.Descendants(presentation + "TabControl"),
                Is.Empty);
            foreach (var binding in requiredBindings)
            {
                NUnitAssert.That(
                    source,
                    Does.Contain(binding),
                    binding);
            }
            foreach (var marker in removedLegacySurfaceMarkers)
            {
                NUnitAssert.That(
                    source,
                    Does.Not.Contain(marker),
                    marker);
            }
        });
    }

    private static FolderProjectGitWorkspaceViewModel CreateWorkspace(
        out FolderProjectVersionControlViewModel versionControl,
        IEditorManager? editorManager = null,
        IFolderProjectVersionControlWindowService? windowService = null,
        IGlobalEventHub? eventHub = null)
    {
        return CreateWorkspace(
            out versionControl,
            out _,
            editorManager,
            windowService,
            eventHub);
    }

    private static FolderProjectGitWorkspaceViewModel CreateWorkspace(
        out FolderProjectVersionControlViewModel versionControl,
        out Mock<IFolderProjectVersionControlService> service,
        IEditorManager? editorManager = null,
        IFolderProjectVersionControlWindowService? windowService = null,
        IGlobalEventHub? eventHub = null)
    {
        var createdService =
            new Mock<IFolderProjectVersionControlService>();
        service = createdService;
        createdService.Setup(item => item.GetStatus(
                It.IsAny<string>(),
                It.IsAny<Action<FolderProjectVersionControlProgress>>(),
                It.IsAny<bool>()))
            .Returns(
                (
                    string projectRoot,
                    Action<FolderProjectVersionControlProgress> _,
                    bool scanUnreadableEntries) =>
                    createdService.Object.GetStatus(
                        projectRoot,
                        scanUnreadableEntries));
        createdService.Setup(item => item.GetCommitChanges(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Action<FolderProjectVersionControlProgress>>()))
            .Returns(
                (
                    string projectRoot,
                    string commitId,
                    Action<FolderProjectVersionControlProgress> _) =>
                    createdService.Object.GetCommitChanges(
                        projectRoot,
                        commitId));
        createdService.Setup(item => item.GetStatus(It.IsAny<string>()))
            .Returns(
                new FolderProjectRepositoryStatus(
                    false,
                    null,
                    null,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []));
        versionControl = new FolderProjectVersionControlViewModel(
            createdService.Object,
            Mock.Of<IFolderProjectGitOperationCoordinator>(),
            Mock.Of<IStandardDialogs>(),
            Mock.Of<IFolderProjectUnsavedChangesService>(),
            Mock.Of<IFolderProjectUnsavedChangesPrompt>(),
            LocalizationManager.Instance);
        return new FolderProjectGitWorkspaceViewModel(
            versionControl,
            editorManager ?? Mock.Of<IEditorManager>(),
            windowService ??
                Mock.Of<IFolderProjectVersionControlWindowService>(),
            eventHub);
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

    private static T? FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(parent);
             index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;

            var descendant = FindVisualDescendant<T>(child);
            if (descendant != null)
                return descendant;
        }

        return null;
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
