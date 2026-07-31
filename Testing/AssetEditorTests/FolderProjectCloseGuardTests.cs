using System.Windows;
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
    public async Task CanCloseAsync_DirtyProjectReview_OpensVersionControl()
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
        var windowService =
            new Mock<IFolderProjectVersionControlWindowService>();
        var guard = new FolderProjectCloseGuard(
            service.Object,
            windowService.Object,
            (_, _, _) => MessageBoxResult.No);

        var canClose = await guard.CanCloseAsync(project);

        NUnit.Framework.Assert.That(canClose, NUnit.Framework.Is.False);
        windowService.Verify(
            item => item.ShowDialog(
                project.ProjectRoot,
                project.ProjectSettings.Name,
                false),
            Times.Once);
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
            Mock.Of<IFolderProjectVersionControlWindowService>(),
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
