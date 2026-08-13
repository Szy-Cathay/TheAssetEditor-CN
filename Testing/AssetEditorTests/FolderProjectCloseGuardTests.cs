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
        var service = new Mock<IFolderProjectHistoryService>();
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(
                new FolderProjectHistoryStatus(
                    FolderProjectHistoryAvailability.Ready,
                    "1111111111111111111111111111111111111111",
                    [
                        new FolderProjectUnrecordedChange(
                            "db/test.tsv",
                            FolderProjectUnrecordedChangeKind.Modified),
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
        var service = new Mock<IFolderProjectHistoryService>();
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(
                new FolderProjectHistoryStatus(
                    FolderProjectHistoryAvailability.Ready,
                    "1111111111111111111111111111111111111111",
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
        var service = new Mock<IFolderProjectHistoryService>();
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(
                new FolderProjectHistoryStatus(
                    FolderProjectHistoryAvailability.Ready,
                    "1111111111111111111111111111111111111111",
                    [
                        new FolderProjectUnrecordedChange(
                            "audio/voice.wem",
                            FolderProjectUnrecordedChangeKind.Modified),
                        new FolderProjectUnrecordedChange(
                            "audio/voice.wav",
                            FolderProjectUnrecordedChangeKind.Added),
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

    [NUnit.Framework.Test]
    public async Task CanCloseAsync_DirtyProject_ClosesProgressBeforePrompt()
    {
        using var project = CreateInitializedProject();
        var service = new Mock<IFolderProjectHistoryService>();
        service.Setup(item => item.GetStatus(project.ProjectRoot))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "1111111111111111111111111111111111111111",
                [
                    new FolderProjectUnrecordedChange(
                        "db/test.tsv",
                        FolderProjectUnrecordedChangeKind.Modified),
                ]));
        var order = new List<string>();
        var guard = new FolderProjectCloseGuard(
            service.Object,
            new TestEventHub(),
            (_, _, _) =>
            {
                order.Add("prompt");
                return MessageBoxResult.Cancel;
            });

        var canClose = await guard.CanCloseAsync(
            project,
            completeProgressBeforePrompt: () =>
            {
                order.Add("progress-closed");
                return Task.CompletedTask;
            });

        NUnit.Framework.Assert.Multiple(() =>
        {
            NUnit.Framework.Assert.That(canClose, NUnit.Framework.Is.False);
            NUnit.Framework.Assert.That(
                order,
                NUnit.Framework.Is.EqualTo(
                    new[] { "progress-closed", "prompt" }));
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
