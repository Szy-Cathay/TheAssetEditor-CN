using AssetEditor.Services;
using AssetEditor.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Does = NUnit.Framework.Does;
using Has = NUnit.Framework.Has;
using Is = NUnit.Framework.Is;

namespace AssetEditorTests;

public class FolderProjectVersionControlViewModelTests
{
    private const string ProjectRoot = @"C:\projects\test";
    private const string MainCommit = "1111111111111111111111111111111111111111";
    private const string FeatureCommit = "2222222222222222222222222222222222222222";
    private const string RewrittenCommit = "3333333333333333333333333333333333333333";

    [NUnit.Framework.SetUp]
    public void SetUp()
    {
        new LocalizationManager().LoadLanguage();
    }

    [NUnit.Framework.Test]
    public async Task OpenProject_LoadsRepositoryAndNullSidedMergeConflict()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status());
        service.Setup(item => item.GetMergeState(ProjectRoot))
            .Returns(
                MergeState(
                    FolderProjectMergePhase.Conflicts,
                    [
                        new FolderProjectMergeConflict(
                            "opaque:conflict:7",
                            null,
                            null,
                            new FolderProjectMergeSide(
                                "incoming/file.bin",
                                FeatureCommit,
                                FolderProjectGitFileMode.NonExecutable,
                                12,
                                true)),
                    ]));
        var viewModel = CreateViewModel(service);

        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", true);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.ProjectRoot,
                Is.EqualTo(ProjectRoot));
            NUnit.Framework.Assert.That(viewModel.ProjectName, Is.EqualTo("测试工程"));
            NUnit.Framework.Assert.That(viewModel.OpenWhenComplete, Is.True);
            NUnit.Framework.Assert.That(viewModel.IsInitialized, Is.True);
            NUnit.Framework.Assert.That(viewModel.CurrentBranch, Is.EqualTo("main"));
            NUnit.Framework.Assert.That(viewModel.History, Has.Count.EqualTo(1));
            NUnit.Framework.Assert.That(viewModel.Branches, Has.Count.EqualTo(2));
            NUnit.Framework.Assert.That(viewModel.MergeConflicts, Has.Count.EqualTo(1));
            NUnit.Framework.Assert.That(
                viewModel.MergeConflicts[0].Id,
                Is.EqualTo("opaque:conflict:7"));
            NUnit.Framework.Assert.That(
                viewModel.MergeConflicts[0].DisplayPath,
                Is.EqualTo("incoming/file.bin"));
            NUnit.Framework.Assert.That(
                viewModel.MergeConflicts[0].CurrentText,
                Is.Not.Null.And.Not.Empty);
        });
    }

    [NUnit.Framework.Test]
    public async Task OpenProject_SplitsStagedAndUnstagedChanges()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                Status(
                    changes:
                    [
                        new FolderProjectWorkingChange(
                            "working.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                        new FolderProjectWorkingChange(
                            "index.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Staged),
                        new FolderProjectWorkingChange(
                            "both.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Staged |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));
        var viewModel = CreateViewModel(service);

        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.UnstagedChanges.Select(item => item.RepositoryPath),
                Is.EquivalentTo(new[] { "working.txt", "both.txt" }));
            NUnit.Framework.Assert.That(
                viewModel.StagedChanges.Select(item => item.RepositoryPath),
                Is.EquivalentTo(new[] { "index.txt", "both.txt" }));
        });
    }

    [NUnit.Framework.Test]
    public async Task StatusCommands_OperateOnSelectedAndAllVisiblePaths()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                Status(
                    changes:
                    [
                        new FolderProjectWorkingChange(
                            "first.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                        new FolderProjectWorkingChange(
                            "second.txt",
                            FolderProjectWorkingChangeKind.Added |
                            FolderProjectWorkingChangeKind.Unstaged),
                        new FolderProjectWorkingChange(
                            "staged.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Staged),
                    ]));
        var viewModel = CreateViewModel(
            service,
            dialogs: ConfirmingDialogs().Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);

        viewModel.SelectedUnstagedChange = viewModel.UnstagedChanges[0];
        await ExecuteAsync(viewModel.StageSelectedCommand);
        await ExecuteAsync(viewModel.StageAllCommand);
        viewModel.SelectedStagedChange = viewModel.StagedChanges.Single();
        await ExecuteAsync(viewModel.UnstageSelectedCommand);
        await ExecuteAsync(viewModel.UnstageAllCommand);
        viewModel.SelectedUnstagedChange = viewModel.UnstagedChanges[1];
        await ExecuteAsync(viewModel.DiscardUnstagedCommand);

        service.Verify(
            item => item.StageChanges(ProjectRoot, new[] { "first.txt" }),
            Times.Once);
        service.Verify(
            item => item.StageChanges(
                ProjectRoot,
                It.Is<IReadOnlyList<string>>(
                    paths =>
                        paths.Count == 2 &&
                        paths.Contains("first.txt") &&
                        paths.Contains("second.txt"))),
            Times.Once);
        service.Verify(
            item => item.UnstageChanges(ProjectRoot, new[] { "staged.txt" }),
            Times.Exactly(2));
        service.Verify(
            item => item.DiscardChanges(ProjectRoot, new[] { "second.txt" }),
            Times.Once);
    }

    [NUnit.Framework.Test]
    public async Task StageSelected_OperatesOnEverySelectedChange()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                Status(
                    changes:
                    [
                        new FolderProjectWorkingChange(
                            "first.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                        new FolderProjectWorkingChange(
                            "second.txt",
                            FolderProjectWorkingChangeKind.Added |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedUnstagedChanges =
            viewModel.UnstagedChanges.ToList();

        await ExecuteAsync(viewModel.StageSelectedCommand);

        service.Verify(
            item => item.StageChanges(
                ProjectRoot,
                It.Is<IReadOnlyList<string>>(
                    paths =>
                        paths.Count == 2 &&
                        paths.Contains("first.txt") &&
                        paths.Contains("second.txt"))),
            Times.Once);
    }

    [NUnit.Framework.Test]
    public async Task DiscardAll_DiscardsEveryWorkingChange()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                Status(
                    changes:
                    [
                        new FolderProjectWorkingChange(
                            "working.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                        new FolderProjectWorkingChange(
                            "index.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Staged),
                        new FolderProjectWorkingChange(
                            "both.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Staged |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));
        var viewModel = CreateViewModel(
            service,
            dialogs: ConfirmingDialogs().Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        var command = viewModel.GetType()
            .GetProperty("DiscardAllCommand")?
            .GetValue(viewModel) as IAsyncRelayCommand;

        NUnit.Framework.Assert.That(command, Is.Not.Null);
        await command!.ExecuteAsync(null);

        service.Verify(
            item => item.DiscardChanges(
                ProjectRoot,
                It.Is<IReadOnlyList<string>>(
                    paths =>
                        paths.Count == 3 &&
                        paths.Contains("working.txt") &&
                        paths.Contains("index.txt") &&
                        paths.Contains("both.txt"))),
            Times.Once);
    }

    [NUnit.Framework.Test]
    public async Task Commit_UsesOnlyStagedChanges()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                Status(
                    changes:
                    [
                        new FolderProjectWorkingChange(
                            "staged.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Staged),
                    ]));
        service.Setup(item => item.CommitStaged(
                ProjectRoot,
                "保存修改\n\n详细说明"))
            .Returns(Commit());
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.CommitMessage = "保存修改\n\n详细说明";

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.CommitCommand.CanExecute(null),
                Is.True);
            NUnit.Framework.Assert.That(
                viewModel.CommitActionText,
                Is.EqualTo("提交暂存的更改"));
        });

        await ExecuteAsync(viewModel.CommitCommand);

        service.Verify(
            item => item.CommitStaged(
                ProjectRoot,
                "保存修改\n\n详细说明"),
            Times.Once);
        service.Verify(
            item => item.CommitAll(
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [NUnit.Framework.Test]
    public async Task Commit_WithoutStagedChanges_CommitsAllChanges()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                Status(
                    changes:
                    [
                        new FolderProjectWorkingChange(
                            "working.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));
        service.Setup(item => item.CommitAll(ProjectRoot, "保存全部修改"))
            .Returns(Commit());
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.CommitMessage = "保存全部修改";

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.CommitCommand.CanExecute(null),
                Is.True);
            NUnit.Framework.Assert.That(
                viewModel.CommitActionText,
                Is.EqualTo("全部提交"));
        });

        await ExecuteAsync(viewModel.CommitCommand);

        service.Verify(
            item => item.CommitAll(ProjectRoot, "保存全部修改"),
            Times.Once);
        service.Verify(
            item => item.CommitStaged(
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [NUnit.Framework.Test]
    public async Task RestoreDeletedFile_UsesCommitParentVersion()
    {
        var deletedCommit = new FolderProjectCommitSummary(
            MainCommit,
            "删除文件",
            "测试者",
            "test@example.com",
            DateTimeOffset.Parse("2026-07-31T10:00:00+08:00"),
            [FeatureCommit]);
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot)).Returns(Status());
        service.Setup(item => item.GetHistory(
                ProjectRoot,
                It.IsAny<string>(),
                100))
            .Returns([deletedCommit]);
        service.Setup(item => item.GetCommitChanges(ProjectRoot, MainCommit))
            .Returns(
                [
                    new FolderProjectCommitChange(
                        "deleted.bin",
                        null,
                        FolderProjectCommitChangeKind.Deleted,
                        true),
                ]);
        service.Setup(item => item.RestoreFile(
                ProjectRoot,
                FeatureCommit,
                "deleted.bin",
                false))
            .Returns(
                new FolderProjectFileRestoreResult(
                    FeatureCommit,
                    "deleted.bin",
                    10));
        var viewModel = CreateViewModel(
            service,
            dialogs: ConfirmingDialogs().Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedTabIndex = 1;
        await viewModel.CommitChangesLoadTask;
        viewModel.SelectedCommitChange = viewModel.CommitChanges.Single();

        await ExecuteAsync(viewModel.RestoreFileCommand);

        service.Verify(item => item.RestoreFile(
            ProjectRoot,
            FeatureCommit,
            "deleted.bin",
            false), Times.Once);
    }

    [NUnit.Framework.Test]
    public async Task RestoreFile_RestoresEverySelectedCommitChange()
    {
        var commit = new FolderProjectCommitSummary(
            MainCommit,
            "修改文件",
            "测试者",
            "test@example.com",
            DateTimeOffset.Parse("2026-07-31T10:00:00+08:00"),
            [FeatureCommit]);
        var changes = new[]
        {
            new FolderProjectCommitChange(
                "first.bin",
                null,
                FolderProjectCommitChangeKind.Modified,
                true),
            new FolderProjectCommitChange(
                "second.bin",
                null,
                FolderProjectCommitChangeKind.Modified,
                true),
        };
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot)).Returns(Status());
        service.Setup(item => item.GetHistory(
                ProjectRoot,
                It.IsAny<string>(),
                100))
            .Returns([commit]);
        service.Setup(item => item.GetCommitChanges(ProjectRoot, MainCommit))
            .Returns(changes);
        service.Setup(item => item.RestoreFile(
                ProjectRoot,
                MainCommit,
                It.IsAny<string>(),
                false))
            .Returns(
                (string _, string commitId, string path, bool _) =>
                    new FolderProjectFileRestoreResult(
                        commitId,
                        path,
                        10));
        var viewModel = CreateViewModel(
            service,
            dialogs: ConfirmingDialogs().Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedTabIndex = 1;
        await viewModel.CommitChangesLoadTask;
        viewModel.SelectedCommitChanges = viewModel.CommitChanges.ToList();
        viewModel.SelectedCommitChange = viewModel.CommitChanges[0];

        await ExecuteAsync(viewModel.RestoreFileCommand);

        service.Verify(item => item.RestoreFile(
            ProjectRoot,
            MainCommit,
            "first.bin",
            false), Times.Once);
        service.Verify(item => item.RestoreFile(
            ProjectRoot,
            MainCommit,
            "second.bin",
            false), Times.Once);
    }

    [NUnit.Framework.Test]
    public async Task DiscardCommitChanges_RemovesSelectedPathFromLatestCommit()
    {
        var original = new FolderProjectCommitSummary(
            MainCommit,
            "提交 A",
            "测试者",
            "test@example.com",
            DateTimeOffset.Parse("2026-07-31T10:00:00+08:00"),
            [FeatureCommit]);
        var rewritten = original with { Id = RewrittenCommit };
        var first = new FolderProjectCommitChange(
            "first.txt",
            null,
            FolderProjectCommitChangeKind.Modified,
            false);
        var second = new FolderProjectCommitChange(
            "second.txt",
            null,
            FolderProjectCommitChangeKind.Modified,
            false);
        var edited = false;
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                () => new FolderProjectRepositoryStatus(
                    true,
                    "main",
                    edited ? RewrittenCommit : MainCommit,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []));
        service.Setup(item => item.GetHistory(ProjectRoot, "main", 100))
            .Returns(() => edited ? [rewritten] : [original]);
        service.Setup(item => item.GetCommitChanges(
                ProjectRoot,
                It.IsAny<string>()))
            .Returns(
                (string _, string commitId) =>
                    commitId == MainCommit ? [first, second] : [second]);
        service.Setup(item => item.EditLatestCommitChanges(
                ProjectRoot,
                MainCommit,
                It.IsAny<IReadOnlyList<string>>(),
                FolderProjectCommitChangeEditMode.Discard))
            .Callback(() => edited = true)
            .Returns(
                new FolderProjectCommitEditSession(
                    MainCommit,
                    RewrittenCommit,
                    ["first.txt"],
                    true));
        var viewModel = CreateViewModel(
            service,
            dialogs: ConfirmingDialogs().Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedTabIndex = 1;
        await viewModel.CommitChangesLoadTask;
        viewModel.SelectedCommitChange = viewModel.CommitChanges[0];

        await ExecuteAsync(viewModel.DiscardCommitChangesCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.CommitChanges.Select(item => item.RepositoryPath),
                Is.EqualTo(new[] { "second.txt" }));
            NUnit.Framework.Assert.That(
                viewModel.SelectedCommit?.Id,
                Is.EqualTo(RewrittenCommit));
        });
        service.Verify(item => item.EditLatestCommitChanges(
            ProjectRoot,
            MainCommit,
            It.Is<IReadOnlyList<string>>(
                paths => paths.SequenceEqual(new[] { "first.txt" })),
            FolderProjectCommitChangeEditMode.Discard), Times.Once);
    }

    [NUnit.Framework.Test]
    public async Task RestoreCommitChangesToStage_AllowsExplicitReturnToOriginalCommit()
    {
        var original = new FolderProjectCommitSummary(
            MainCommit,
            "提交 A",
            "测试者",
            "test@example.com",
            DateTimeOffset.Parse("2026-07-31T10:00:00+08:00"),
            [FeatureCommit]);
        var rewritten = original with { Id = RewrittenCommit };
        var first = new FolderProjectCommitChange(
            "first.txt",
            null,
            FolderProjectCommitChangeKind.Modified,
            false);
        var second = new FolderProjectCommitChange(
            "second.txt",
            null,
            FolderProjectCommitChangeKind.Modified,
            false);
        var staged = false;
        var completed = false;
        var session = new FolderProjectCommitEditSession(
            MainCommit,
            RewrittenCommit,
            ["first.txt"],
            true);
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                () => new FolderProjectRepositoryStatus(
                    true,
                    "main",
                    completed ? MainCommit : staged ? RewrittenCommit : MainCommit,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    staged && !completed
                        ?
                        [
                            new FolderProjectWorkingChange(
                                "first.txt",
                                FolderProjectWorkingChangeKind.Modified |
                                FolderProjectWorkingChangeKind.Staged),
                        ]
                        : []));
        service.Setup(item => item.GetHistory(ProjectRoot, "main", 100))
            .Returns(
                () => completed || !staged
                    ? [original]
                    : [rewritten]);
        service.Setup(item => item.GetCommitChanges(
                ProjectRoot,
                It.IsAny<string>()))
            .Returns(
                (string _, string commitId) =>
                    commitId == RewrittenCommit ? [second] : [first, second]);
        service.Setup(item => item.EditLatestCommitChanges(
                ProjectRoot,
                MainCommit,
                It.IsAny<IReadOnlyList<string>>(),
                FolderProjectCommitChangeEditMode.StageForEdit))
            .Callback(() => staged = true)
            .Returns(session);
        service.Setup(item => item.CompleteLatestCommitEdit(
                ProjectRoot,
                session))
            .Callback(() => completed = true)
            .Returns(original);
        var viewModel = CreateViewModel(
            service,
            dialogs: ConfirmingDialogs().Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedTabIndex = 1;
        await viewModel.CommitChangesLoadTask;
        viewModel.SelectedCommitChange = viewModel.CommitChanges[0];

        await ExecuteAsync(viewModel.RestoreCommitChangesToStageCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.CommitChanges.Select(item => item.RepositoryPath),
                Is.EqualTo(new[] { "second.txt" }));
            NUnit.Framework.Assert.That(
                viewModel.StagedChanges.Select(item => item.RepositoryPath),
                Is.EqualTo(new[] { "first.txt" }));
            NUnit.Framework.Assert.That(viewModel.SelectedTabIndex, Is.Zero);
            NUnit.Framework.Assert.That(
                viewModel.ReturnChangesToOriginalCommitCommand.CanExecute(null),
                Is.True);
        });

        await ExecuteAsync(viewModel.ReturnChangesToOriginalCommitCommand);
        viewModel.SelectedTabIndex = 1;
        await viewModel.CommitChangesLoadTask;

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(viewModel.CommitChanges, Has.Count.EqualTo(2));
            NUnit.Framework.Assert.That(viewModel.StagedChanges, Is.Empty);
            NUnit.Framework.Assert.That(
                viewModel.ReturnChangesToOriginalCommitCommand.CanExecute(null),
                Is.False);
        });
    }

    [NUnit.Framework.Test]
    public void CommitFileEditingCommands_HaveClearChineseLabels()
    {
        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                LocalizationManager.Instance.Get(
                    "FolderProject.VersionControl.DiscardCommitChanges"),
                Is.EqualTo("撤销修改"));
            NUnit.Framework.Assert.That(
                LocalizationManager.Instance.Get(
                    "FolderProject.VersionControl.RestoreCommitChangesToStage"),
                Is.EqualTo("恢复到暂存"));
            NUnit.Framework.Assert.That(
                LocalizationManager.Instance.Get(
                    "FolderProject.VersionControl.ReturnChangesToOriginalCommit"),
                Is.EqualTo("重新提交"));
            NUnit.Framework.Assert.That(
                LocalizationManager.Instance.Get(
                    "FolderProject.VersionControl.Commit"),
                Is.EqualTo("提交"));
        });
    }

    [NUnit.Framework.Test]
    public async Task UndoLatestCommit_RequiresCleanCurrentBranchAndLatestHead()
    {
        var commit = new FolderProjectCommitSummary(
            MainCommit,
            "待重新修改",
            "测试者",
            "test@example.com",
            DateTimeOffset.Parse("2026-07-31T10:00:00+08:00"),
            [FeatureCommit]);
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot)).Returns(Status());
        service.Setup(item => item.GetHistory(
                ProjectRoot,
                It.IsAny<string>(),
                100))
            .Returns([commit]);
        var viewModel = CreateViewModel(
            service,
            dialogs: ConfirmingDialogs().Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);

        NUnit.Framework.Assert.That(
            viewModel.UndoLatestCommitKeepChangesCommand.CanExecute(null),
            Is.True);
        NUnit.Framework.Assert.That(
            viewModel.UndoLatestCommitAndDiscardChangesCommand.CanExecute(null),
            Is.True);
        await ExecuteAsync(viewModel.UndoLatestCommitKeepChangesCommand);
        await ExecuteAsync(
            viewModel.UndoLatestCommitAndDiscardChangesCommand);

        service.Verify(
            item => item.UndoLatestCommit(
                ProjectRoot,
                MainCommit,
                FolderProjectCommitUndoMode.KeepChanges),
            Times.Once);
        service.Verify(
            item => item.UndoLatestCommit(
                ProjectRoot,
                MainCommit,
                FolderProjectCommitUndoMode.DiscardChanges),
            Times.Once);
    }

    [NUnit.Framework.Test]
    public async Task OpenProject_UsesRecordedMergeSourceWhenBranchNoLongerExists()
    {
        const string recordedSource = "deleted-feature";
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status(FolderProjectRepositoryOperationState.Merge));
        service.Setup(item => item.GetMergeState(ProjectRoot))
            .Returns(
                new FolderProjectMergeState(
                    FolderProjectMergePhase.Conflicts,
                    "main",
                    recordedSource,
                    MainCommit,
                    FeatureCommit,
                    "合并 deleted-feature",
                    [],
                    null));
        var viewModel = CreateViewModel(service);

        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", true);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.SelectedMergeSource?.Name,
                Is.EqualTo(recordedSource));
            NUnit.Framework.Assert.That(
                viewModel.MergeSources.Any(
                    item => item.Name == recordedSource),
                Is.True);
            NUnit.Framework.Assert.That(
                viewModel.CanSelectMergeSource,
                Is.False);
        });
    }

    [NUnit.Framework.Test]
    public async Task SelectingHistoryBranch_LoadsBranchWithoutCheckoutAndDisablesRestore()
    {
        var featureSummary = new FolderProjectCommitSummary(
            FeatureCommit,
            "功能分支提交",
            "测试者",
            "test@example.com",
            DateTimeOffset.Parse("2026-07-31T11:00:00+08:00"),
            [MainCommit]);
        var featureChange = new FolderProjectCommitChange(
            "db/feature.tsv",
            null,
            FolderProjectCommitChangeKind.Modified,
            false);
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status());
        service.Setup(item => item.GetHistory(ProjectRoot, "main", 100))
            .Returns([Commit()]);
        service.Setup(item => item.GetHistory(ProjectRoot, "feature", 100))
            .Returns([featureSummary, Commit()]);
        service.Setup(item => item.GetCommitChanges(
                ProjectRoot,
                FeatureCommit))
            .Returns([featureChange]);
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedTabIndex = 1;

        viewModel.SelectedHistoryBranch =
            viewModel.Branches.Single(item => item.Name == "feature");
        await viewModel.HistoryLoadTask;
        viewModel.SelectedCommit = featureSummary;
        await viewModel.CommitChangesLoadTask;
        viewModel.SelectedCommitChange = viewModel.CommitChanges.Single();

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.History.Select(commit => commit.Id),
                Is.EqualTo(new[] { FeatureCommit, MainCommit }));
            NUnit.Framework.Assert.That(
                viewModel.CurrentBranch,
                Is.EqualTo("main"));
            NUnit.Framework.Assert.That(
                viewModel.RestoreFileCommand.CanExecute(null),
                Is.False);
            NUnit.Framework.Assert.That(
                viewModel.HistoryBranchHint,
                Does.Contain("只读"));
        });
        service.Verify(
            item => item.GetHistory(ProjectRoot, "feature", 100),
            Times.Once);
        service.Verify(
            item => item.SwitchBranch(
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [NUnit.Framework.Test]
    public async Task Initialize_UsesLocalDefaultsAndReportsBusyProgress()
    {
        var initialized = false;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                () => initialized
                    ? Status()
                    : new FolderProjectRepositoryStatus(
                        false,
                        null,
                        null,
                        false,
                        FolderProjectRepositoryOperationState.None,
                        []));
        FolderProjectGitIdentity? capturedIdentity = null;
        string? capturedPrimaryBranchName = null;
        service.Setup(
                item => item.Initialize(
                    ProjectRoot,
                    It.IsAny<FolderProjectGitIdentity>(),
                    It.IsAny<string>()))
            .Returns(
                (
                    string _,
                    FolderProjectGitIdentity identity,
                    string primaryBranchName) =>
                {
                    capturedIdentity = identity;
                    capturedPrimaryBranchName = primaryBranchName;
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(5));
                    initialized = true;
                    return Commit();
                });
        var viewModel = CreateViewModel(
            service,
            dialogs: ConfirmingDialogs().Object);
        await OpenProjectAsync(
            viewModel,
            ProjectRoot,
            "测试工程",
            false);
        viewModel.IdentityName = "";
        viewModel.IdentityEmail = "";

        var operation =
            viewModel.InitializeCommand.ExecuteAsync(null);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                entered.Wait(TimeSpan.FromSeconds(5)),
                Is.True);
            NUnit.Framework.Assert.That(viewModel.IsBusy, Is.True);
            NUnit.Framework.Assert.That(
                viewModel.BusyMessage,
                Does.Contain("正在"));
            NUnit.Framework.Assert.That(operation.IsCompleted, Is.False);
        });
        release.Set();
        await operation;

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                capturedIdentity?.Name,
                Is.EqualTo("AssetEditor.CN 本地用户"));
            NUnit.Framework.Assert.That(
                capturedIdentity?.Email,
                Is.EqualTo("local@asseteditor.cn"));
            NUnit.Framework.Assert.That(
                capturedPrimaryBranchName,
                Is.EqualTo("master"));
            NUnit.Framework.Assert.That(viewModel.IsBusy, Is.False);
            NUnit.Framework.Assert.That(viewModel.IsInitialized, Is.True);
        });
    }

    [NUnit.Framework.Test]
    public async Task IdentityAndBranchUpdates_AvoidFullProjectRescan()
    {
        var statusReads = 0;
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                () =>
                {
                    statusReads++;
                    return Status();
                });
        service.Setup(
            item => item.SetIdentity(
                ProjectRoot,
                It.IsAny<FolderProjectGitIdentity>()));
        service.Setup(
                item => item.CreateBranch(
                    ProjectRoot,
                    "new-branch",
                    null))
            .Returns(
                new FolderProjectBranchInfo(
                    "new-branch",
                    MainCommit,
                    false));
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowTextInputDialog(
                "新建分支",
                It.IsAny<string>()))
            .Returns(new TextInputDialogResult(true, "new-branch"));
        var viewModel = CreateViewModel(service, dialogs: dialogs.Object);
        await OpenProjectAsync(
            viewModel,
            ProjectRoot,
            "测试工程",
            false);

        await ExecuteAsync(viewModel.SaveIdentityCommand);
        await ExecuteAsync(viewModel.CreateBranchCommand);

        NUnit.Framework.Assert.That(statusReads, Is.EqualTo(1));
    }

    [NUnit.Framework.Test]
    public async Task CreateAndSwitchBranch_CarriesDirtyChangesFromStatusPage()
    {
        var currentBranch = "main";
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                () => new FolderProjectRepositoryStatus(
                    true,
                    currentBranch,
                    MainCommit,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    [
                        new FolderProjectWorkingChange(
                            "file.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));
        service.Setup(item => item.GetBranches(ProjectRoot))
            .Returns(
                () =>
                [
                    new FolderProjectBranchInfo(
                        "main",
                        MainCommit,
                        currentBranch == "main"),
                    .. currentBranch == "topic"
                        ? new[]
                        {
                            new FolderProjectBranchInfo(
                                "topic",
                                MainCommit,
                                true),
                        }
                        : [],
                    new FolderProjectBranchInfo(
                        "feature",
                        FeatureCommit,
                        false),
                ]);
        service.Setup(item => item.GetStashes(ProjectRoot)).Returns([]);
        service.Setup(item => item.CreateBranch(
                ProjectRoot,
                "topic",
                null))
            .Returns(new FolderProjectBranchInfo("topic", MainCommit, false));
        service.Setup(item => item.SwitchBranch(ProjectRoot, "topic"))
            .Returns(
                () =>
                {
                    currentBranch = "topic";
                    return new FolderProjectBranchInfo(
                        "topic",
                        MainCommit,
                        true);
                });
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowTextInputDialog(
                "新建分支",
                It.IsAny<string>()))
            .Returns(new TextInputDialogResult(true, "topic"));
        dialogs.Setup(item => item.ShowYesNoBox(
                It.Is<string>(message => message.Contains("带入")),
                "新建分支"))
            .Returns(ShowMessageBoxResult.OK);
        var coordinator = new RecordingCoordinator();
        var viewModel = CreateViewModel(
            service,
            coordinator,
            dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);

        await ExecuteAsync(viewModel.CreateAndSwitchBranchCommand);

        service.Verify(item => item.CreateBranch(
            ProjectRoot,
            "topic",
            null), Times.Once);
        service.Verify(item => item.SwitchBranch(
            ProjectRoot,
            "topic"), Times.Once);
        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.CurrentBranch,
                Is.EqualTo("topic"));
            NUnit.Framework.Assert.That(
                viewModel.SelectedBranch?.Name,
                Is.EqualTo("topic"));
            NUnit.Framework.Assert.That(
                viewModel.SelectedBranch?.IsCurrent,
                Is.True);
            NUnit.Framework.Assert.That(
                coordinator.Calls,
                Has.Count.EqualTo(1));
        });
    }

    [NUnit.Framework.Test]
    public async Task CreateAndSwitchBranch_DirtyCancellationDoesNotCreateBranch()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                Status(
                    changes:
                    [
                        new FolderProjectWorkingChange(
                            "file.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(item => item.ShowTextInputDialog(
                "新建分支",
                It.IsAny<string>()))
            .Returns(new TextInputDialogResult(true, "topic"));
        dialogs.Setup(item => item.ShowYesNoBox(
                It.Is<string>(message => message.Contains("带入")),
                "新建分支"))
            .Returns(ShowMessageBoxResult.Cancel);
        var viewModel = CreateViewModel(service, dialogs: dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);

        await ExecuteAsync(viewModel.CreateAndSwitchBranchCommand);

        service.Verify(item => item.CreateBranch(
            ProjectRoot,
            "topic",
            null), Times.Never);
        service.Verify(item => item.SwitchBranch(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [NUnit.Framework.Test]
    public async Task PrepareMerge_SelectsBranchAndOpensMergeTab()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status());
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(
            viewModel,
            ProjectRoot,
            "测试工程",
            false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");

        viewModel.PrepareMergeCommand.Execute(null);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.SelectedMergeSource?.Name,
                Is.EqualTo("feature"));
            NUnit.Framework.Assert.That(
                viewModel.SelectedMergeTarget?.Name,
                Is.EqualTo("main"));
            NUnit.Framework.Assert.That(
                viewModel.SelectedTabIndex,
                Is.EqualTo(1));
        });
    }

    [NUnit.Framework.Test]
    public async Task PrepareMerge_CurrentBranchIsValidSource()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "feature",
                    FeatureCommit,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []));
        service.Setup(item => item.GetBranches(ProjectRoot))
            .Returns(
                [
                    new FolderProjectBranchInfo(
                        "feature",
                        FeatureCommit,
                        true),
                    new FolderProjectBranchInfo(
                        "master",
                        MainCommit,
                        false),
                ]);
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(
            viewModel,
            ProjectRoot,
            "测试工程",
            false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.IsCurrent);

        NUnit.Framework.Assert.That(
            viewModel.PrepareMergeCommand.CanExecute(null),
            Is.True);
        viewModel.PrepareMergeCommand.Execute(null);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.SelectedMergeSource?.Name,
                Is.EqualTo("feature"));
            NUnit.Framework.Assert.That(
                viewModel.SelectedMergeTarget?.Name,
                Is.EqualTo("master"));
            NUnit.Framework.Assert.That(
                viewModel.SelectedTabIndex,
                Is.EqualTo(1));
        });
    }

    [NUnit.Framework.Test]
    public async Task ChangingMergeSource_RecomputesDefaultTarget()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "feature",
                    FeatureCommit,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []));
        service.Setup(item => item.GetBranches(ProjectRoot))
            .Returns(
                [
                    new FolderProjectBranchInfo(
                        "feature",
                        FeatureCommit,
                        true),
                    new FolderProjectBranchInfo(
                        "master",
                        MainCommit,
                        false),
                ]);
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(
            viewModel,
            ProjectRoot,
            "测试工程",
            false);

        viewModel.SelectedMergeSource =
            viewModel.MergeSources.Single(item => item.Name == "feature");

        NUnit.Framework.Assert.That(
            viewModel.SelectedMergeTarget?.Name,
            Is.EqualTo("master"));
    }

    [NUnit.Framework.Test]
    public async Task BeginMerge_SourceAndTargetMustDiffer()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status());
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(
            viewModel,
            ProjectRoot,
            "测试工程",
            false);
        var feature =
            viewModel.MergeSources.Single(item => item.Name == "feature");
        viewModel.SelectedMergeSource = feature;
        viewModel.SelectedMergeTarget =
            viewModel.MergeTargets.Single(item => item.Name == "feature");

        NUnit.Framework.Assert.That(
            viewModel.BeginMergeCommand.CanExecute(null),
            Is.False);
    }

    [NUnit.Framework.Test]
    public async Task BranchSelection_ExplainsMergeDirection()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status());
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(
            viewModel,
            ProjectRoot,
            "测试工程",
            false);

        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.IsCurrent);
        NUnit.Framework.Assert.That(
            viewModel.BranchActionHint,
            Does.Contain("feature"));
        NUnit.Framework.Assert.That(
            viewModel.BranchActionHint,
            Does.Contain("默认"));

        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");
        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.BranchActionHint,
                Does.Contain("feature"));
            NUnit.Framework.Assert.That(
                viewModel.BranchActionHint,
                Does.Contain("main"));
            NUnit.Framework.Assert.That(
                viewModel.BranchActionHint,
                Does.Contain("合并到当前分支"));
            NUnit.Framework.Assert.That(
                LocalizationManager.Instance.Get(
                    "FolderProject.VersionControl.Error.WorkingTreeNotClean"),
                Does.Contain("状态与提交"));
        });
    }

    [NUnit.Framework.Test]
    public async Task SelectingCommit_WhenChangesFail_ClearsPreviousRestoreTarget()
    {
        var secondCommit = new FolderProjectCommitSummary(
            FeatureCommit,
            "第二次提交",
            "测试者",
            "test@example.com",
            DateTimeOffset.Parse("2026-07-31T11:00:00+08:00"),
            [MainCommit]);
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status());
        service.Setup(item => item.GetHistory(ProjectRoot, "main", 100))
            .Returns([Commit(), secondCommit]);
        service.Setup(item => item.GetCommitChanges(ProjectRoot, MainCommit))
            .Returns(
                [
                    new FolderProjectCommitChange(
                        "db/old.tsv",
                        null,
                        FolderProjectCommitChangeKind.Modified,
                        false),
                ]);
        service.Setup(
                item => item.GetCommitChanges(ProjectRoot, FeatureCommit))
            .Throws(
                new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.RepositoryBusy,
                    "raw failure"));
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedTabIndex = 1;
        await viewModel.CommitChangesLoadTask;
        viewModel.SelectedCommitChange = viewModel.CommitChanges.Single();

        viewModel.SelectedCommit = secondCommit;
        await viewModel.CommitChangesLoadTask;

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(viewModel.CommitChanges, Is.Empty);
            NUnit.Framework.Assert.That(
                viewModel.SelectedCommitChange,
                Is.Null);
            NUnit.Framework.Assert.That(
                viewModel.RestoreFileCommand.CanExecute(null),
                Is.False);
        });
    }

    [NUnit.Framework.Test]
    public async Task RestoreFile_WorkingChangeRequiresSecondConfirmationAndRetries()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status());
        service.Setup(
                item => item.GetCommitChanges(ProjectRoot, MainCommit))
            .Returns(
                [
                    new FolderProjectCommitChange(
                        "db/file.tsv",
                        null,
                        FolderProjectCommitChangeKind.Modified,
                        false),
                ]);
        var overwriteValues = new List<bool>();
        service.Setup(
                item => item.RestoreFile(
                    ProjectRoot,
                    MainCommit,
                    "db/file.tsv",
                    It.IsAny<bool>()))
            .Returns(
                (
                    string _,
                    string _,
                    string _,
                    bool overwrite) =>
                {
                    overwriteValues.Add(overwrite);
                    if (!overwrite)
                    {
                        throw new FolderProjectVersionControlException(
                            FolderProjectVersionControlError
                                .WorkingTreeNotClean,
                            "raw conflict");
                    }

                    return new FolderProjectFileRestoreResult(
                        MainCommit,
                        "db/file.tsv",
                        16);
                });
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(
                item => item.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK);
        var coordinator = new RecordingCoordinator();
        var viewModel = CreateViewModel(
            service,
            coordinator,
            dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", true);
        viewModel.SelectedTabIndex = 1;
        await viewModel.CommitChangesLoadTask;
        viewModel.SelectedCommit = viewModel.History.Single();
        viewModel.SelectedCommitChange =
            viewModel.CommitChanges.Single();

        await ExecuteAsync(viewModel.RestoreFileCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                overwriteValues,
                Is.EqualTo(new[] { false, true }));
            NUnit.Framework.Assert.That(coordinator.Calls, Has.Count.EqualTo(2));
            NUnit.Framework.Assert.That(
                coordinator.Calls.All(
                    item =>
                        item.ProjectRoot == ProjectRoot &&
                        item.OpenWhenComplete),
                Is.True);
        });
    }

    [NUnit.Framework.Test]
    public async Task SwitchAndBeginMerge_AreExecutedThroughCoordinator()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status());
        service.Setup(item => item.SwitchBranch(ProjectRoot, "feature"))
            .Returns(
                new FolderProjectBranchInfo(
                    "feature",
                    FeatureCommit,
                    true));
        service.Setup(item => item.BeginMerge(ProjectRoot, "feature"))
            .Returns(
                new FolderProjectMergeStartResult(
                    FolderProjectMergeOutcome.UpToDate,
                    Commit(),
                    MergeState(FolderProjectMergePhase.None)));
        var dialogs = ConfirmingDialogs();
        var coordinator = new RecordingCoordinator();
        var viewModel = CreateViewModel(
            service,
            coordinator,
            dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");

        await ExecuteAsync(viewModel.SwitchBranchCommand);
        viewModel.SelectedMergeSource =
            viewModel.MergeSources.Single(item => item.Name == "feature");
        await ExecuteAsync(viewModel.BeginMergeCommand);

        service.Verify(
            item => item.SwitchBranch(ProjectRoot, "feature"),
            Times.Once);
        service.Verify(
            item => item.BeginMerge(ProjectRoot, "feature"),
            Times.Once);
        NUnit.Framework.Assert.That(coordinator.Calls, Has.Count.EqualTo(2));
    }

    [NUnit.Framework.Test]
    public async Task SwitchBranch_DirtyRepository_OpensVsChoiceWithoutSwitching()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                Status(
                    changes:
                    [
                        new FolderProjectWorkingChange(
                            "file.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");

        await ExecuteAsync(viewModel.SwitchBranchCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.IsBranchSwitchChoiceOpen,
                Is.True);
            NUnit.Framework.Assert.That(
                viewModel.PendingBranchName,
                Is.EqualTo("feature"));
        });
        service.Verify(
            item => item.SwitchBranch(
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
        service.Verify(
            item => item.SwitchBranch(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FolderProjectBranchSwitchMode>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [NUnit.Framework.Test]
    public async Task StashChangesAndSwitch_UsesStashModeThroughCoordinator()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                Status(
                    changes:
                    [
                        new FolderProjectWorkingChange(
                            "file.txt",
                            FolderProjectWorkingChangeKind.Modified |
                            FolderProjectWorkingChangeKind.Unstaged),
                    ]));
        service.Setup(item => item.SwitchBranch(
                ProjectRoot,
                "feature",
                FolderProjectBranchSwitchMode.StashChanges,
                "WIP on main"))
            .Returns(
                new FolderProjectBranchInfo(
                    "feature",
                    FeatureCommit,
                    true));
        var coordinator = new RecordingCoordinator();
        var viewModel = CreateViewModel(service, coordinator);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");
        await ExecuteAsync(viewModel.SwitchBranchCommand);

        await ExecuteAsync(viewModel.StashChangesAndSwitchCommand);

        service.Verify(item => item.SwitchBranch(
            ProjectRoot,
            "feature",
            FolderProjectBranchSwitchMode.StashChanges,
            "WIP on main"), Times.Once);
        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(coordinator.Calls, Has.Count.EqualTo(1));
            NUnit.Framework.Assert.That(
                viewModel.IsBranchSwitchChoiceOpen,
                Is.False);
        });
    }

    [NUnit.Framework.Test]
    public async Task OpenProject_LoadsStashesAndApplyUsesCoordinator()
    {
        var stash = new FolderProjectStashInfo(
            0,
            "WIP on main",
            DateTimeOffset.Parse("2026-08-01T10:00:00+08:00"),
            ["file.txt"]);
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot)).Returns(Status());
        service.Setup(item => item.GetStashes(ProjectRoot)).Returns([stash]);
        var coordinator = new RecordingCoordinator();
        var viewModel = CreateViewModel(service, coordinator);

        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedStash = viewModel.Stashes.Single();
        await ExecuteAsync(viewModel.ApplyStashCommand);

        service.Verify(item => item.ApplyStash(ProjectRoot, 0), Times.Once);
        NUnit.Framework.Assert.That(coordinator.Calls, Has.Count.EqualTo(1));
    }

    [NUnit.Framework.Test]
    public async Task BeginMerge_CurrentSourceSwitchesToMasterThenMergesOnce()
    {
        var operations = new List<string>();
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "feature",
                    FeatureCommit,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []));
        service.Setup(item => item.GetBranches(ProjectRoot))
            .Returns(
                [
                    new FolderProjectBranchInfo(
                        "feature",
                        FeatureCommit,
                        true),
                    new FolderProjectBranchInfo(
                        "master",
                        MainCommit,
                        false),
                ]);
        service.Setup(item => item.SwitchBranch(ProjectRoot, "master"))
            .Callback(() => operations.Add("switch:master"))
            .Returns(
                new FolderProjectBranchInfo(
                    "master",
                    MainCommit,
                    true));
        service.Setup(item => item.BeginMerge(ProjectRoot, "feature"))
            .Callback(() => operations.Add("merge:feature"))
            .Returns(
                new FolderProjectMergeStartResult(
                    FolderProjectMergeOutcome.UpToDate,
                    Commit(),
                    MergeState(FolderProjectMergePhase.None)));
        string? confirmationMessage = null;
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(
                item => item.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
            .Callback<string, string>(
                (message, _) => confirmationMessage = message)
            .Returns(ShowMessageBoxResult.OK);
        var coordinator = new RecordingCoordinator();
        var viewModel = CreateViewModel(
            service,
            coordinator,
            dialogs.Object);
        await OpenProjectAsync(
            viewModel,
            ProjectRoot,
            "测试工程",
            false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.IsCurrent);
        viewModel.PrepareMergeCommand.Execute(null);

        await ExecuteAsync(viewModel.BeginMergeCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                operations,
                Is.EqualTo(new[] { "switch:master", "merge:feature" }));
            NUnit.Framework.Assert.That(
                coordinator.Calls,
                Has.Count.EqualTo(1));
            NUnit.Framework.Assert.That(
                confirmationMessage,
                Does.Contain("feature"));
            NUnit.Framework.Assert.That(
                confirmationMessage,
                Does.Contain("master"));
        });
    }

    [NUnit.Framework.Test]
    public async Task BeginMerge_FailureBeforeMergeRestoresOriginalBranch()
    {
        var operations = new List<string>();
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "feature",
                    FeatureCommit,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []));
        service.Setup(item => item.GetBranches(ProjectRoot))
            .Returns(
                [
                    new FolderProjectBranchInfo(
                        "feature",
                        FeatureCommit,
                        true),
                    new FolderProjectBranchInfo(
                        "master",
                        MainCommit,
                        false),
                ]);
        service.Setup(item => item.SwitchBranch(ProjectRoot, "master"))
            .Callback(() => operations.Add("switch:master"))
            .Returns(
                new FolderProjectBranchInfo(
                    "master",
                    MainCommit,
                    true));
        service.Setup(item => item.BeginMerge(ProjectRoot, "feature"))
            .Callback(() => operations.Add("merge:feature"))
            .Throws(
                new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.UnrelatedHistories,
                    "test failure"));
        service.Setup(item => item.SwitchBranch(ProjectRoot, "feature"))
            .Callback(() => operations.Add("switch:feature"))
            .Returns(
                new FolderProjectBranchInfo(
                    "feature",
                    FeatureCommit,
                    true));
        var coordinator = new RecordingCoordinator();
        var viewModel = CreateViewModel(
            service,
            coordinator,
            ConfirmingDialogs().Object);
        await OpenProjectAsync(
            viewModel,
            ProjectRoot,
            "测试工程",
            false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.IsCurrent);
        viewModel.PrepareMergeCommand.Execute(null);

        await ExecuteAsync(viewModel.BeginMergeCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                operations,
                Is.EqualTo(
                    new[]
                    {
                        "switch:master",
                        "merge:feature",
                        "switch:feature",
                    }));
            NUnit.Framework.Assert.That(
                viewModel.StatusMessage,
                Does.Contain("共同历史"));
            NUnit.Framework.Assert.That(
                coordinator.Calls,
                Has.Count.EqualTo(1));
        });
    }

    [NUnit.Framework.Test]
    public async Task BeginMerge_TargetAdvancedBeforeFailureDoesNotSwitchBack()
    {
        var operations = new List<string>();
        var currentBranch = "feature";
        var targetAdvanced = false;
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                () => new FolderProjectRepositoryStatus(
                    true,
                    currentBranch,
                    currentBranch == "master" && targetAdvanced
                        ? FeatureCommit
                        : currentBranch == "master"
                            ? MainCommit
                            : FeatureCommit,
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []));
        service.Setup(item => item.GetBranches(ProjectRoot))
            .Returns(
                () =>
                [
                    new FolderProjectBranchInfo(
                        currentBranch,
                        currentBranch == "master" && !targetAdvanced
                            ? MainCommit
                            : FeatureCommit,
                        true),
                    new FolderProjectBranchInfo(
                        currentBranch == "master" ? "feature" : "master",
                        currentBranch == "master"
                            ? FeatureCommit
                            : targetAdvanced
                                ? FeatureCommit
                                : MainCommit,
                        false),
                ]);
        service.Setup(item => item.SwitchBranch(ProjectRoot, "master"))
            .Callback(
                () =>
                {
                    operations.Add("switch:master");
                    currentBranch = "master";
                })
            .Returns(
                new FolderProjectBranchInfo(
                    "master",
                    MainCommit,
                    true));
        service.Setup(item => item.BeginMerge(ProjectRoot, "feature"))
            .Callback(
                () =>
                {
                    operations.Add("merge:feature");
                    targetAdvanced = true;
                })
            .Throws(
                new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.RepositoryFailure,
                    "failure after fast-forward"));
        service.Setup(item => item.SwitchBranch(ProjectRoot, "feature"))
            .Callback(() => operations.Add("switch:feature"))
            .Returns(
                new FolderProjectBranchInfo(
                    "feature",
                    FeatureCommit,
                    true));
        var viewModel = CreateViewModel(
            service,
            new RecordingCoordinator(),
            ConfirmingDialogs().Object);
        await OpenProjectAsync(
            viewModel,
            ProjectRoot,
            "测试工程",
            false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.IsCurrent);
        viewModel.PrepareMergeCommand.Execute(null);

        await ExecuteAsync(viewModel.BeginMergeCommand);

        NUnit.Framework.Assert.That(
            operations,
            Is.EqualTo(new[] { "switch:master", "merge:feature" }));
    }

    [NUnit.Framework.Test]
    public async Task ResolveConflict_ResolvesEverySelectedConflict()
    {
        var conflicts = new[]
        {
            MergeConflict("first-conflict", "first.txt"),
            MergeConflict("second-conflict", "second.txt"),
        };
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status(FolderProjectRepositoryOperationState.Merge));
        service.Setup(item => item.GetMergeState(ProjectRoot))
            .Returns(
                MergeState(
                    FolderProjectMergePhase.Conflicts,
                    conflicts));
        service.Setup(item => item.ResolveMergeConflict(
                ProjectRoot,
                It.IsAny<string>(),
                FolderProjectMergeChoice.Current))
            .Returns(
                MergeState(
                    FolderProjectMergePhase.Conflicts,
                    conflicts));
        var viewModel = CreateViewModel(
            service,
            dialogs: ConfirmingDialogs().Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedMergeConflicts = viewModel.MergeConflicts.ToList();
        viewModel.SelectedMergeConflict = viewModel.MergeConflicts[0];

        await ExecuteAsync(viewModel.UseCurrentCommand);

        service.Verify(item => item.ResolveMergeConflict(
            ProjectRoot,
            "first-conflict",
            FolderProjectMergeChoice.Current), Times.Once);
        service.Verify(item => item.ResolveMergeConflict(
            ProjectRoot,
            "second-conflict",
            FolderProjectMergeChoice.Current), Times.Once);
    }

    [NUnit.Framework.Test]
    public async Task ResolveCompleteAndAbort_UseOpaqueIdAndCoordinator()
    {
        var phase = FolderProjectMergePhase.Conflicts;
        const string conflictId = "opaque|do-not-parse|42";
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(() => Status(
                phase == FolderProjectMergePhase.None
                    ? FolderProjectRepositoryOperationState.None
                    : FolderProjectRepositoryOperationState.Merge));
        service.Setup(item => item.GetMergeState(ProjectRoot))
            .Returns(
                () => MergeState(
                    phase,
                    phase == FolderProjectMergePhase.Conflicts
                        ?
                        [
                            new FolderProjectMergeConflict(
                                conflictId,
                                null,
                                new FolderProjectMergeSide(
                                    "file.txt",
                                    MainCommit,
                                    FolderProjectGitFileMode.NonExecutable,
                                    4,
                                    false),
                                null),
                        ]
                        : []));
        service.Setup(
                item => item.ResolveMergeConflict(
                    ProjectRoot,
                    conflictId,
                    FolderProjectMergeChoice.Incoming))
            .Returns(
                () =>
                {
                    phase = FolderProjectMergePhase.ReadyToCommit;
                    return MergeState(phase);
                });
        service.Setup(item => item.CompleteMerge(
                ProjectRoot,
                "完成合并\n\n合并后的说明"))
            .Returns(
                () =>
                {
                    phase = FolderProjectMergePhase.None;
                    return Commit();
                });
        service.Setup(item => item.AbortMerge(ProjectRoot))
            .Callback(() => phase = FolderProjectMergePhase.None);
        var coordinator = new RecordingCoordinator();
        var dialogs = ConfirmingDialogs();
        var viewModel = CreateViewModel(
            service,
            coordinator,
            dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", true);
        viewModel.SelectedMergeConflict =
            viewModel.MergeConflicts.Single();

        await ExecuteAsync(viewModel.UseIncomingCommand);
        viewModel.MergeMessage = "完成合并";
        await ExecuteAsync(viewModel.CompleteMergeCommand);
        phase = FolderProjectMergePhase.Conflicts;
        await ExecuteAsync(viewModel.RefreshCommand);
        await ExecuteAsync(viewModel.AbortMergeCommand);

        service.Verify(
            item => item.ResolveMergeConflict(
                ProjectRoot,
                conflictId,
                FolderProjectMergeChoice.Incoming),
            Times.Once);
        service.Verify(
            item => item.CompleteMerge(
                ProjectRoot,
                "完成合并\n\n合并后的说明"),
            Times.Once);
        service.Verify(
            item => item.AbortMerge(ProjectRoot),
            Times.Once);
        dialogs.Verify(item => item.ShowTitleDescriptionInputDialog(
            "提交文件",
            "提交标题",
            "提交说明",
            "完成合并",
            ""), Times.Once);
        NUnit.Framework.Assert.That(coordinator.Calls, Has.Count.EqualTo(3));
        NUnit.Framework.Assert.That(
            coordinator.Calls.All(item => item.OpenWhenComplete),
            Is.True);
    }

    [NUnit.Framework.Test]
    public async Task VersionControlError_UsesLocalizedCodeAndRefreshes()
    {
        var statusReads = 0;
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                () =>
                {
                    statusReads++;
                    return Status(
                        changes:
                        [
                            new FolderProjectWorkingChange(
                                "file.txt",
                                FolderProjectWorkingChangeKind.Modified |
                                FolderProjectWorkingChangeKind.Staged),
                        ]);
                });
        service.Setup(item => item.CommitStaged(ProjectRoot, "保存修改"))
            .Throws(
                new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.NothingToCommit,
                    "SECRET RAW ERROR"));
        var viewModel = CreateViewModel(
            service,
            dialogs: ConfirmingDialogs().Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.CommitMessage = "保存修改";

        await ExecuteAsync(viewModel.CommitCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(statusReads, Is.GreaterThanOrEqualTo(2));
            NUnit.Framework.Assert.That(
                viewModel.StatusMessage,
                Does.Not.Contain("SECRET RAW ERROR"));
            NUnit.Framework.Assert.That(
                viewModel.StatusMessage,
                Does.Contain("提交"));
        });
    }

    [NUnit.Framework.Test]
    public async Task RecoveryClose_AbortFailureRequiresSeparateForceCloseConfirmation()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status(FolderProjectRepositoryOperationState.Merge));
        service.Setup(item => item.GetMergeState(ProjectRoot))
            .Returns(MergeState(FolderProjectMergePhase.RecoveryRequired));
        service.Setup(item => item.AbortMerge(ProjectRoot))
            .Throws(
                new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.RepositoryFailure,
                    "raw abort failure"));
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.SetupSequence(
                item => item.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK)
            .Returns(ShowMessageBoxResult.OK);
        var coordinator = new RecordingCoordinator();
        var viewModel = CreateViewModel(
            service,
            coordinator,
            dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", true);

        var canClose = viewModel.CanCloseWindow();

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(canClose, Is.True);
            NUnit.Framework.Assert.That(coordinator.Calls, Has.Count.EqualTo(1));
            dialogs.Verify(
                item => item.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Exactly(2));
        });
    }

    [NUnit.Framework.Test]
    public async Task Close_WhenInitialMergeStateReadFailed_RechecksAndBlocks()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status(FolderProjectRepositoryOperationState.Merge));
        service.SetupSequence(item => item.GetMergeState(ProjectRoot))
            .Throws(
                new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.RepositoryBusy,
                    "raw initial failure"))
            .Returns(MergeState(FolderProjectMergePhase.RecoveryRequired));
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(
                item => item.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.Cancel);
        var viewModel = CreateViewModel(
            service,
            dialogs: dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", true);

        var canClose = viewModel.CanCloseWindow();

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(canClose, Is.False);
            NUnit.Framework.Assert.That(
                viewModel.MergePhase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            service.Verify(
                item => item.GetMergeState(ProjectRoot),
                Times.Exactly(2));
            dialogs.Verify(
                item => item.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Once);
        });
    }

    [NUnit.Framework.Test]
    public async Task Close_WhenInitialStatusReadFailed_RechecksPendingRecoveryAndBlocks()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Throws(
                new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.RepositoryBusy,
                    "raw initial failure"));
        service.Setup(item => item.GetMergeState(ProjectRoot))
            .Returns(MergeState(FolderProjectMergePhase.RecoveryRequired));
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(
                item => item.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.Cancel);
        var viewModel = CreateViewModel(
            service,
            dialogs: dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", true);

        var canClose = viewModel.CanCloseWindow();

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(canClose, Is.False);
            NUnit.Framework.Assert.That(
                viewModel.MergePhase,
                Is.EqualTo(FolderProjectMergePhase.RecoveryRequired));
            service.Verify(
                item => item.GetMergeState(ProjectRoot),
                Times.Once);
            dialogs.Verify(
                item => item.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Once);
        });
    }

    [NUnit.Framework.Test]
    public async Task Refresh_PreservesUserMergeMessageWhenPhaseChanges()
    {
        var phase = FolderProjectMergePhase.Conflicts;
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(Status(FolderProjectRepositoryOperationState.Merge));
        service.Setup(item => item.GetMergeState(ProjectRoot))
            .Returns(() => MergeState(phase));
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", true);
        viewModel.MergeMessage = "保留用户填写的说明";
        phase = FolderProjectMergePhase.ReadyToCommit;

        await ExecuteAsync(viewModel.RefreshCommand);

        NUnit.Framework.Assert.That(
            viewModel.MergeMessage,
            Is.EqualTo("保留用户填写的说明"));
    }

    [NUnit.Framework.Test]
    public async Task InitializeIdentityCommitAndReferenceChanges_DoNotUseCoordinator()
    {
        var initialized = false;
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                () => initialized
                    ? Status(
                        changes:
                        [
                            new FolderProjectWorkingChange(
                                "file.txt",
                                FolderProjectWorkingChangeKind.Modified |
                                FolderProjectWorkingChangeKind.Staged),
                        ])
                    : new FolderProjectRepositoryStatus(
                        false,
                        null,
                        null,
                        false,
                        FolderProjectRepositoryOperationState.None,
                        []));
        service.Setup(
                item => item.Initialize(
                    ProjectRoot,
                    new FolderProjectGitIdentity(
                        "测试者",
                        "test@example.com")))
            .Returns(
                () =>
                {
                    initialized = true;
                    return Commit();
                });
        service.Setup(
            item => item.SetIdentity(
                ProjectRoot,
                It.IsAny<FolderProjectGitIdentity>()));
        service.Setup(item => item.CommitStaged(ProjectRoot, "保存修改"))
            .Returns(Commit());
        service.Setup(
                item => item.CreateRecoveryBranch(
                    ProjectRoot,
                    "recovery/test",
                    MainCommit))
            .Returns(
                new FolderProjectBranchInfo(
                    "recovery/test",
                    MainCommit,
                    false));
        service.Setup(
                item => item.CreateBranch(
                    ProjectRoot,
                    "new-branch",
                    null))
            .Returns(
                new FolderProjectBranchInfo(
                    "new-branch",
                    MainCommit,
                    false));
        service.Setup(
                item => item.RenameBranch(
                    ProjectRoot,
                    "feature",
                    "renamed"))
            .Returns(
                new FolderProjectBranchInfo(
                    "renamed",
                    FeatureCommit,
                    false));
        service.Setup(item => item.DeleteBranch(ProjectRoot, "feature"));
        var coordinator = new RecordingCoordinator();
        var dialogs = ConfirmingDialogs();
        var viewModel = CreateViewModel(
            service,
            coordinator,
            dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.IdentityName = "测试者";
        viewModel.IdentityEmail = "test@example.com";

        await ExecuteAsync(viewModel.InitializeCommand);
        await ExecuteAsync(viewModel.SaveIdentityCommand);
        viewModel.CommitMessage = "保存修改";
        await ExecuteAsync(viewModel.CommitCommand);
        viewModel.RecoveryBranchName = "recovery/test";
        await ExecuteAsync(viewModel.CreateRecoveryBranchCommand);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");
        viewModel.BranchName = "new-branch";
        await ExecuteAsync(viewModel.CreateBranchCommand);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");
        viewModel.BranchName = "renamed";
        await ExecuteAsync(viewModel.RenameBranchCommand);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");
        await ExecuteAsync(viewModel.DeleteBranchCommand);

        service.Verify(
            item => item.Initialize(
                ProjectRoot,
                new FolderProjectGitIdentity(
                    "测试者",
                    "test@example.com")),
            Times.Once);
        service.Verify(
            item => item.SetIdentity(
                ProjectRoot,
                It.IsAny<FolderProjectGitIdentity>()),
            Times.Once);
        service.Verify(
            item => item.CommitStaged(ProjectRoot, "保存修改"),
            Times.Once);
        service.Verify(
            item => item.CreateRecoveryBranch(
                ProjectRoot,
                "recovery/test",
                MainCommit),
            Times.Once);
        service.Verify(
            item => item.CreateBranch(
                ProjectRoot,
                "new-branch",
                null),
            Times.Once);
        service.Verify(
            item => item.RenameBranch(
                ProjectRoot,
                "feature",
                "renamed"),
            Times.Once);
        service.Verify(
            item => item.DeleteBranch(ProjectRoot, "feature"),
            Times.Once);
        dialogs.Verify(item => item.ShowTitleDescriptionInputDialog(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
        dialogs.Verify(item => item.ShowTextInputDialog(
            "保护分支名称",
            "recovery/test"), Times.Once);
        dialogs.Verify(item => item.ShowTextInputDialog(
            "新建分支",
            "new-branch"), Times.Once);
        dialogs.Verify(item => item.ShowTextInputDialog(
            "重命名所选分支",
            "renamed"), Times.Once);
        NUnit.Framework.Assert.That(coordinator.Calls, Is.Empty);
    }

    [NUnit.Framework.Test]
    public async Task DirtyRepository_AllowsSafeBranchSwitchButNotMerge()
    {
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                Status(
                    changes:
                    [
                        new FolderProjectWorkingChange(
                            "file.txt",
                            FolderProjectWorkingChangeKind.Modified),
                    ]));
        var viewModel = CreateViewModel(service);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");
        viewModel.SelectedMergeSource =
            viewModel.MergeSources.Single(item => item.Name == "feature");

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.SwitchBranchCommand.CanExecute(null),
                Is.True);
            NUnit.Framework.Assert.That(
                viewModel.PrepareMergeCommand.CanExecute(null),
                Is.False);
            NUnit.Framework.Assert.That(
                viewModel.BeginMergeCommand.CanExecute(null),
                Is.False);
            NUnit.Framework.Assert.That(
                viewModel.BranchActionHint,
                Does.Contain("安全保留"));
        });
    }

    [NUnit.Framework.Test]
    public async Task RealRepository_InitializeCommitCreateAndSwitchBranch()
    {
        using var directory = new TemporaryDirectory();
        new FolderProjectSettings { Name = "真实仓库测试" }
            .Save(directory.Path);
        var resourcePath = Path.Combine(directory.Path, "file.txt");
        File.WriteAllText(resourcePath, "first");
        var coordinator = new RecordingCoordinator();
        var viewModel = new FolderProjectVersionControlViewModel(
            new FolderProjectVersionControlService(),
            coordinator,
            ConfirmingDialogs().Object,
            LocalizationManager.Instance);
        await OpenProjectAsync(
            viewModel,
            directory.Path,
            "真实仓库测试",
            false);
        viewModel.IdentityName = "测试者";
        viewModel.IdentityEmail = "test@example.com";

        await ExecuteAsync(viewModel.InitializeCommand);
        File.WriteAllText(resourcePath, "second");
        await ExecuteAsync(viewModel.RefreshCommand);
        await ExecuteAsync(viewModel.StageAllCommand);
        viewModel.CommitMessage = "保存真实修改";
        await ExecuteAsync(viewModel.CommitCommand);
        viewModel.BranchName = "feature";
        await ExecuteAsync(viewModel.CreateBranchCommand);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");
        await ExecuteAsync(viewModel.SwitchBranchCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(viewModel.IsInitialized, Is.True);
            NUnit.Framework.Assert.That(viewModel.IsClean, Is.True);
            NUnit.Framework.Assert.That(
                viewModel.CurrentBranch,
                Is.EqualTo("feature"));
            NUnit.Framework.Assert.That(viewModel.History, Has.Count.EqualTo(1));
            NUnit.Framework.Assert.That(
                coordinator.Calls,
                Has.Count.EqualTo(1));
        });
    }

    [NUnit.Framework.Test]
    public async Task RealRepository_RestoreCommitFileEnablesRecommit()
    {
        using var directory = new TemporaryDirectory();
        new FolderProjectSettings { Name = "真实重新提交测试" }
            .Save(directory.Path);
        var resourcePath = Path.Combine(directory.Path, "file.txt");
        var otherPath = Path.Combine(directory.Path, "other.txt");
        File.WriteAllText(resourcePath, "first");
        File.WriteAllText(otherPath, "first");
        var viewModel = new FolderProjectVersionControlViewModel(
            new FolderProjectVersionControlService(),
            new RecordingCoordinator(),
            ConfirmingDialogs().Object,
            LocalizationManager.Instance);
        await OpenProjectAsync(
            viewModel,
            directory.Path,
            "真实重新提交测试",
            false);
        viewModel.IdentityName = "测试者";
        viewModel.IdentityEmail = "test@example.com";

        await ExecuteAsync(viewModel.InitializeCommand);
        File.WriteAllText(resourcePath, "second");
        File.WriteAllText(otherPath, "second");
        await ExecuteAsync(viewModel.RefreshCommand);
        await ExecuteAsync(viewModel.StageAllCommand);
        viewModel.CommitMessage = "提交 A";
        await ExecuteAsync(viewModel.CommitCommand);
        viewModel.SelectedCommit = viewModel.History.First();
        viewModel.SelectedTabIndex = 1;
        await viewModel.CommitChangesLoadTask;
        viewModel.SelectedCommitChange = viewModel.CommitChanges.Single(
            change => change.RepositoryPath == "file.txt");

        await ExecuteAsync(viewModel.RestoreCommitChangesToStageCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(viewModel.SelectedTabIndex, Is.Zero);
            NUnit.Framework.Assert.That(
                viewModel.StagedChanges.Select(change => change.RepositoryPath),
                Does.Contain("file.txt"));
            NUnit.Framework.Assert.That(
                viewModel.ReturnChangesToOriginalCommitCommand.CanExecute(null),
                Is.True);
        });
    }

    [NUnit.Framework.Test]
    public async Task RealRepository_CurrentFeatureMergesBackToMaster()
    {
        using var directory = new TemporaryDirectory();
        new FolderProjectSettings { Name = "真实合并测试" }
            .Save(directory.Path);
        var resourcePath = Path.Combine(directory.Path, "file.txt");
        File.WriteAllText(resourcePath, "master");
        var coordinator = new RecordingCoordinator();
        var viewModel = new FolderProjectVersionControlViewModel(
            new FolderProjectVersionControlService(),
            coordinator,
            ConfirmingDialogs().Object,
            LocalizationManager.Instance);
        await OpenProjectAsync(
            viewModel,
            directory.Path,
            "真实合并测试",
            false);
        viewModel.IdentityName = "测试者";
        viewModel.IdentityEmail = "test@example.com";

        await ExecuteAsync(viewModel.InitializeCommand);
        viewModel.BranchName = "feature";
        await ExecuteAsync(viewModel.CreateBranchCommand);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");
        await ExecuteAsync(viewModel.SwitchBranchCommand);
        File.WriteAllText(resourcePath, "feature");
        await ExecuteAsync(viewModel.RefreshCommand);
        await ExecuteAsync(viewModel.StageAllCommand);
        viewModel.CommitMessage = "功能分支修改";
        await ExecuteAsync(viewModel.CommitCommand);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.IsCurrent);
        viewModel.PrepareMergeCommand.Execute(null);

        await ExecuteAsync(viewModel.BeginMergeCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.CurrentBranch,
                Is.EqualTo("master"));
            NUnit.Framework.Assert.That(viewModel.IsClean, Is.True);
            NUnit.Framework.Assert.That(
                File.ReadAllText(resourcePath),
                Is.EqualTo("feature"));
            NUnit.Framework.Assert.That(
                coordinator.Calls,
                Has.Count.EqualTo(2));
        });
    }

    [NUnit.Framework.Test]
    public async Task HostCancellation_IsSilentAndRefreshesStatus()
    {
        var statusReads = 0;
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                () =>
                {
                    statusReads++;
                    return Status();
                });
        var dialogs = ConfirmingDialogs();
        var viewModel = CreateViewModel(
            service,
            new ThrowingCoordinator(
                new FolderProjectGitOperationCanceledException()),
            dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");

        await ExecuteAsync(viewModel.SwitchBranchCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.StatusMessage,
                Is.EqualTo("操作已取消。"));
            NUnit.Framework.Assert.That(statusReads, Is.GreaterThanOrEqualTo(2));
            dialogs.Verify(
                item => item.ShowDialogBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Never);
        });
    }

    [NUnit.Framework.Test]
    public async Task HostFailure_ShowsGenericChineseAndRefreshesStatus()
    {
        var statusReads = 0;
        var service = CreateService();
        service.Setup(item => item.GetStatus(ProjectRoot))
            .Returns(
                () =>
                {
                    statusReads++;
                    return Status();
                });
        var dialogs = ConfirmingDialogs();
        var viewModel = CreateViewModel(
            service,
            new ThrowingCoordinator(
                new FolderProjectGitHostException(
                    "SECRET HOST MESSAGE",
                    new IOException("SECRET IO MESSAGE"))),
            dialogs.Object);
        await OpenProjectAsync(viewModel, ProjectRoot, "测试工程", false);
        viewModel.SelectedBranch =
            viewModel.Branches.Single(item => item.Name == "feature");

        await ExecuteAsync(viewModel.SwitchBranchCommand);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(
                viewModel.StatusMessage,
                Does.Not.Contain("SECRET"));
            NUnit.Framework.Assert.That(
                viewModel.StatusMessage,
                Does.Contain("失败"));
            NUnit.Framework.Assert.That(statusReads, Is.GreaterThanOrEqualTo(2));
            dialogs.Verify(
                item => item.ShowDialogBox(
                    viewModel.StatusMessage,
                    It.IsAny<string>()),
                Times.Once);
        });
    }

    private static async Task OpenProjectAsync(
        FolderProjectVersionControlViewModel viewModel,
        string projectRoot,
        string projectName,
        bool openWhenComplete)
    {
        viewModel.OpenProject(
            projectRoot,
            projectName,
            openWhenComplete);
        if (viewModel.RefreshCommand.ExecutionTask != null)
            await viewModel.RefreshCommand.ExecutionTask;
    }

    private static Task ExecuteAsync(IAsyncRelayCommand command)
    {
        return command.ExecuteAsync(null);
    }

    private static Mock<IFolderProjectVersionControlService> CreateService()
    {
        var service = new Mock<IFolderProjectVersionControlService>();
        service.Setup(item => item.GetIdentity(ProjectRoot))
            .Returns(new FolderProjectGitIdentity("测试者", "test@example.com"));
        service.Setup(item => item.GetHistory(
                ProjectRoot,
                It.IsAny<string>(),
                100))
            .Returns([Commit()]);
        service.Setup(item => item.GetCommitChanges(ProjectRoot, MainCommit))
            .Returns([]);
        service.Setup(item => item.GetBranches(ProjectRoot))
            .Returns(
                [
                    new FolderProjectBranchInfo("main", MainCommit, true),
                    new FolderProjectBranchInfo(
                        "feature",
                        FeatureCommit,
                        false),
                ]);
        service.Setup(item => item.GetStashes(ProjectRoot)).Returns([]);
        service.Setup(item => item.GetMergeState(ProjectRoot))
            .Returns(MergeState(FolderProjectMergePhase.None));
        return service;
    }

    private static FolderProjectVersionControlViewModel CreateViewModel(
        Mock<IFolderProjectVersionControlService> service,
        IFolderProjectGitOperationCoordinator? coordinator = null,
        IStandardDialogs? dialogs = null)
    {
        return new FolderProjectVersionControlViewModel(
            service.Object,
            coordinator ?? new RecordingCoordinator(),
            dialogs ?? Mock.Of<IStandardDialogs>(),
            LocalizationManager.Instance);
    }

    private static Mock<IStandardDialogs> ConfirmingDialogs()
    {
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(
                item => item.ShowYesNoBox(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
            .Returns(ShowMessageBoxResult.OK);
        dialogs.Setup(
                item => item.ShowTextInputDialog(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
            .Returns(
                (string _, string initialText) =>
                    new TextInputDialogResult(true, initialText));
        dialogs.Setup(
                item => item.ShowTitleDescriptionInputDialog(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
            .Returns(
                (
                    string _,
                    string _,
                    string _,
                    string initialTitle,
                    string initialDescription) =>
                    new TitleDescriptionInputDialogResult(
                        true,
                        initialTitle,
                        initialTitle == "完成合并"
                            ? "合并后的说明"
                            : initialDescription));
        return dialogs;
    }

    private static FolderProjectRepositoryStatus Status(
        FolderProjectRepositoryOperationState operation =
            FolderProjectRepositoryOperationState.None,
        IReadOnlyList<FolderProjectWorkingChange>? changes = null)
    {
        return new FolderProjectRepositoryStatus(
            true,
            "main",
            MainCommit,
            false,
            operation,
            changes ?? []);
    }

    private static FolderProjectCommitSummary Commit()
    {
        return new FolderProjectCommitSummary(
            MainCommit,
            "提交说明",
            "测试者",
            "test@example.com",
            DateTimeOffset.Parse("2026-07-31T10:00:00+08:00"),
            []);
    }

    private static FolderProjectMergeConflict MergeConflict(
        string id,
        string path)
    {
        return new FolderProjectMergeConflict(
            id,
            null,
            new FolderProjectMergeSide(
                path,
                MainCommit,
                FolderProjectGitFileMode.NonExecutable,
                4,
                false),
            null);
    }

    private static FolderProjectMergeState MergeState(
        FolderProjectMergePhase phase,
        IReadOnlyList<FolderProjectMergeConflict>? conflicts = null)
    {
        return new FolderProjectMergeState(
            phase,
            "main",
            phase == FolderProjectMergePhase.None ? null : "feature",
            MainCommit,
            phase == FolderProjectMergePhase.None ? null : FeatureCommit,
            phase == FolderProjectMergePhase.None
                ? null
                : "合并 feature",
            conflicts ?? [],
            phase == FolderProjectMergePhase.RecoveryRequired
                ? "raw recovery reason"
                : null);
    }

    private sealed class RecordingCoordinator :
        IFolderProjectGitOperationCoordinator
    {
        public List<(string ProjectRoot, bool OpenWhenComplete)> Calls { get; } =
            [];

        public T Execute<T>(
            string projectRoot,
            Func<T> detachedOperation,
            bool openWhenComplete = false)
        {
            Calls.Add((projectRoot, openWhenComplete));
            return detachedOperation();
        }

        public Task<T> ExecuteAsync<T>(
            string projectRoot,
            Func<T> detachedOperation,
            bool openWhenComplete = false)
        {
            Calls.Add((projectRoot, openWhenComplete));
            return Task.Run(detachedOperation);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-version-control-vm-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

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

    private sealed class ThrowingCoordinator(Exception exception) :
        IFolderProjectGitOperationCoordinator
    {
        public T Execute<T>(
            string projectRoot,
            Func<T> detachedOperation,
            bool openWhenComplete = false)
        {
            throw exception;
        }

        public Task<T> ExecuteAsync<T>(
            string projectRoot,
            Func<T> detachedOperation,
            bool openWhenComplete = false)
        {
            return Task.FromException<T>(exception);
        }
    }
}
