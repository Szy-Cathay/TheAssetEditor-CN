using System.Windows;
using AssetEditor.Events;
using AssetEditor.Services;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace AssetEditorTests;

public class FolderProjectCloseGuardTests
{
    [NUnit.Framework.SetUp]
    public void SetUp()
    {
        new LocalizationManager().LoadLanguage();
    }

    [NUnit.Framework.Test]
    public async Task CanCloseAsync_DirtyProjectReview_OpensGitPanel()
    {
        using var project = CreateInitializedProject();
        var service = new Mock<IFolderProjectVersionControlService>();
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "master",
                    "1111111111111111111111111111111111111111",
                    false,
                    FolderProjectRepositoryOperationState.None,
                    [
                        new FolderProjectWorkingChange(
                            "db/test.tsv",
                            FolderProjectWorkingChangeKind.Modified),
                    ]));
        var eventHub = new TestEventHub();
        OpenFolderProjectGitPanelEvent? published = null;
        eventHub.Register<OpenFolderProjectGitPanelEvent>(
            this,
            item => published = item);
        var guard = new FolderProjectCloseGuard(
            service.Object,
            eventHub,
            (_, _, _) => MessageBoxResult.No);

        var canClose = await guard.CanCloseAsync(project);

        NUnit.Framework.Assert.That(canClose, NUnit.Framework.Is.False);
        NUnit.Framework.Assert.That(published, NUnit.Framework.Is.Not.Null);
    }

    [NUnit.Framework.Test]
    public async Task CanCloseAsync_CleanProject_DoesNotPrompt()
    {
        using var project = CreateInitializedProject();
        var service = new Mock<IFolderProjectVersionControlService>();
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "master",
                    "1111111111111111111111111111111111111111",
                    false,
                    FolderProjectRepositoryOperationState.None,
                    []));
        var promptCalls = 0;
        var guard = new FolderProjectCloseGuard(
            service.Object,
            new TestEventHub(),
            (_, _, _) =>
            {
                promptCalls++;
                return MessageBoxResult.Cancel;
            });

        var canClose = await guard.CanCloseAsync(project);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(canClose, NUnit.Framework.Is.True);
            NUnit.Framework.Assert.That(promptCalls, NUnit.Framework.Is.Zero);
        });
    }

    [NUnit.Framework.Test]
    public async Task CanCloseAsync_ReportsCloseCheckStagesAndChangeCount()
    {
        using var project = CreateInitializedProject();
        var service = new Mock<IFolderProjectVersionControlService>();
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(
                new FolderProjectRepositoryStatus(
                    true,
                    "master",
                    "1111111111111111111111111111111111111111",
                    false,
                    FolderProjectRepositoryOperationState.None,
                    [
                        new FolderProjectWorkingChange(
                            "audio/voice.wem",
                            FolderProjectWorkingChangeKind.Modified),
                        new FolderProjectWorkingChange(
                            "audio/voice.wav",
                            FolderProjectWorkingChangeKind.Added),
                    ]));
        var progress = new List<FolderProjectCloseProgress>();
        var guard = new FolderProjectCloseGuard(
            service.Object,
            new TestEventHub(),
            (_, _, _) => MessageBoxResult.Yes);

        var canClose = await guard.CanCloseAsync(
            project,
            progress.Add);

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(canClose, NUnit.Framework.Is.True);
            NUnit.Framework.Assert.That(
                progress.Select(item => item.Stage),
                NUnit.Framework.Is.EqualTo(
                    new[]
                    {
                        FolderProjectCloseProgressStage.Preparing,
                        FolderProjectCloseProgressStage.ReadingRepositoryStatus,
                        FolderProjectCloseProgressStage.SummarizingChanges,
                    }));
            NUnit.Framework.Assert.That(
                progress[^1].ChangeCount,
                NUnit.Framework.Is.EqualTo(2));
        });
    }

    private static FolderProjectContainer CreateInitializedProject()
    {
        var projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"ae-close-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var project = FolderProjectContainer.Create(
            projectRoot,
            new FolderProjectSettings { Name = "测试工程" });
        Directory.CreateDirectory(Path.Combine(projectRoot, ".git"));
        return project;
    }
}
