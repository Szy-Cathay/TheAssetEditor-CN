using AssetEditor.Services;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditorTests;

public class FolderProjectOpenServiceTests
{
    [TestCase(FolderProjectMergePhase.ReadyToCommit)]
    [TestCase(FolderProjectMergePhase.Conflicts)]
    [TestCase(FolderProjectMergePhase.RecoveryRequired)]
    public void Open_PendingMerge_ShowsRecoveryBeforeFactoryOpen(
        FolderProjectMergePhase phase)
    {
        using var project = new TemporaryFolderProject();
        var calls = new List<string>();
        var versionControl =
            new Mock<IFolderProjectVersionControlService>(
                MockBehavior.Strict);
        versionControl.Setup(
                item => item.GetMergeState(project.Path))
            .Callback(() => calls.Add("merge-state"))
            .Returns(
                new FolderProjectMergeState(
                    phase,
                    "main",
                    "feature",
                    new string('1', 40),
                    new string('2', 40),
                    "合并 feature",
                    [],
                    phase == FolderProjectMergePhase.RecoveryRequired
                        ? "recovery"
                        : null));
        var factory = new Mock<IFolderProjectFactory>(
            MockBehavior.Strict);
        var window =
            new Mock<IFolderProjectVersionControlWindowService>(
                MockBehavior.Strict);
        window.Setup(
                item => item.ShowDialog(
                    project.Path,
                    project.Name,
                    true))
            .Callback(() => calls.Add("window"));
        var service = new FolderProjectOpenService(
            Mock.Of<IPackFileService>(),
            factory.Object,
            versionControl.Object,
            window.Object,
            new ApplicationSettingsService(),
            Mock.Of<IStandardDialogs>(),
            new LocalizationManager());

        service.Open(project.Path);

        NUnit.Framework.Assert.That(calls, Is.EqualTo(
            new[] { "merge-state", "window" }));
        factory.Verify(
            item => item.Open(It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public void Open_NoPendingMerge_OpensAndAddsProject()
    {
        using var project = new TemporaryFolderProject();
        var calls = new List<string>();
        var versionControl =
            new Mock<IFolderProjectVersionControlService>(
                MockBehavior.Strict);
        versionControl.Setup(
                item => item.GetMergeState(project.Path))
            .Callback(() => calls.Add("merge-state"))
            .Returns(
                new FolderProjectMergeState(
                    FolderProjectMergePhase.None,
                    null,
                    null,
                    null,
                    null,
                    null,
                    [],
                    null));
        using var container =
            FolderProjectContainer.Open(project.Path);
        var factory = new Mock<IFolderProjectFactory>(
            MockBehavior.Strict);
        factory.Setup(item => item.Open(project.Path))
            .Callback(() => calls.Add("factory-open"))
            .Returns(container);
        var packFiles = new Mock<IPackFileService>(
            MockBehavior.Strict);
        packFiles.Setup(item => item.AddContainer(container, true))
            .Callback(() => calls.Add("add"))
            .Returns(container);
        var service = new FolderProjectOpenService(
            packFiles.Object,
            factory.Object,
            versionControl.Object,
            Mock.Of<IFolderProjectVersionControlWindowService>(),
            new ApplicationSettingsService(
                GameTypeEnum.Warhammer3),
            Mock.Of<IStandardDialogs>(),
            new LocalizationManager());

        service.Open(project.Path);

        NUnit.Framework.Assert.That(
            calls,
            Is.EqualTo(
                new[] { "merge-state", "factory-open", "add" }));
    }

    [Test]
    public void Open_UninitializedProject_RequiresInitializationBeforeOpening()
    {
        using var project = new TemporaryFolderProject();
        var versionControl = new Mock<IFolderProjectVersionControlService>();
        versionControl.Setup(item => item.GetMergeState(project.Path))
            .Throws(new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryNotInitialized,
                "not initialized"));
        var factory = new Mock<IFolderProjectFactory>();
        var window = new Mock<IFolderProjectVersionControlWindowService>();
        var dialogs = new Mock<IStandardDialogs>();
        var service = new FolderProjectOpenService(
            Mock.Of<IPackFileService>(),
            factory.Object,
            versionControl.Object,
            window.Object,
            new ApplicationSettingsService(),
            dialogs.Object,
            LoadLocalization());

        service.Open(project.Path);

        factory.Verify(item => item.Open(It.IsAny<string>()), Times.Never);
        dialogs.Verify(item => item.ShowDialogBox(
            "必须先初始化本地版本管理，才能打开并修改这个文件夹工程。",
            "文件夹工程错误"), Times.Once);
        window.Verify(item => item.ShowDialog(
            project.Path,
            project.Name,
            true), Times.Once);
    }

    [Test]
    public void Open_FactoryFails_RemovesRecentAndShowsGenericPrompt()
    {
        using var project = new TemporaryFolderProject();
        var versionControl =
            new Mock<IFolderProjectVersionControlService>();
        versionControl.Setup(
                item => item.GetMergeState(project.Path))
            .Returns(
                new FolderProjectMergeState(
                    FolderProjectMergePhase.None,
                    null,
                    null,
                    null,
                    null,
                    null,
                    [],
                    null));
        var factory = new Mock<IFolderProjectFactory>();
        factory.Setup(item => item.Open(project.Path))
            .Throws(new InvalidDataException("SECRET RAW FAILURE"));
        var settings = new ApplicationSettingsService();
        settings.CurrentSettings.RecentFolderProjectPaths.Add(
            project.Path);
        var dialogs = new Mock<IStandardDialogs>();
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        var service = new FolderProjectOpenService(
            Mock.Of<IPackFileService>(),
            factory.Object,
            versionControl.Object,
            Mock.Of<IFolderProjectVersionControlWindowService>(),
            settings,
            dialogs.Object,
            localization);

        service.Open(project.Path);

        NUnit.Framework.Assert.That(
            settings.CurrentSettings.RecentFolderProjectPaths,
            Is.Empty);
        dialogs.Verify(
            item => item.ShowDialogBox(
                "无法打开文件夹工程。请检查工程配置和目录权限。",
                "文件夹工程错误"),
            Times.Once);
        dialogs.Verify(
            item => item.ShowExceptionWindow(
                It.IsAny<Exception>(),
                It.IsAny<string>()),
            Times.Never);
    }

    private sealed class TemporaryFolderProject : IDisposable
    {
        public string Path { get; }
        public string Name { get; } = "测试工程";

        public TemporaryFolderProject()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ae-folder-open-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            new FolderProjectSettings { Name = Name }.Save(Path);
            Path = System.IO.Path.TrimEndingDirectorySeparator(
                System.IO.Path.GetFullPath(Path));
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }

    private static LocalizationManager LoadLocalization()
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        return localization;
    }
}
