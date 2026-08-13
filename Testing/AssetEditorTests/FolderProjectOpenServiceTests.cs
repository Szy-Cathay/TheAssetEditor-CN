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
    [Test]
    public void Open_RecoveryRequired_ShowsHistoryRecoveryBeforeFactoryOpen()
    {
        using var project = new TemporaryFolderProject();
        var calls = new List<string>();
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(item => item.GetStatus(project.Path))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.RecoveryRequired,
                "head",
                []));
        var factory = new Mock<IFolderProjectFactory>(
            MockBehavior.Strict);
        var window =
            new Mock<IFolderProjectHistoryWindowService>(
                MockBehavior.Strict);
        window.Setup(
                item => item.ShowRecoveryDialog(
                    project.Path,
                    project.Name))
            .Callback(() => calls.Add("window"));
        var service = new FolderProjectOpenService(
            Mock.Of<IPackFileService>(),
            factory.Object,
            history.Object,
            window.Object,
            new ApplicationSettingsService(),
            Mock.Of<IStandardDialogs>(),
            new LocalizationManager());

        service.Open(project.Path);

        NUnit.Framework.Assert.That(calls, Is.EqualTo(
            new[] { "window" }));
        factory.Verify(
            item => item.Open(It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public void Open_NoPendingMerge_OpensAndAddsProject()
    {
        using var project = new TemporaryFolderProject();
        var calls = new List<string>();
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(item => item.GetStatus(project.Path))
            .Callback(() => calls.Add("history-status"))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                []));
        using var container =
            FolderProjectContainer.Open(project.Path);
        var factory = new Mock<IFolderProjectFactory>(
            MockBehavior.Strict);
        factory.Setup(item => item.Open(project.Path))
            .Callback(() => calls.Add("factory-open"))
            .Returns(container);
        var packFiles = new Mock<IPackFileService>(
            MockBehavior.Strict);
        packFiles.Setup(item => item.TryActivateFolderProject(project.Path))
            .Returns(false);
        packFiles.Setup(item => item.AddEditableFolderProject(container))
            .Callback(() => calls.Add("add"))
            .Returns(container);
        var service = new FolderProjectOpenService(
            packFiles.Object,
            factory.Object,
            history.Object,
            Mock.Of<IFolderProjectHistoryWindowService>(),
            new ApplicationSettingsService(
                GameTypeEnum.Warhammer3),
            Mock.Of<IStandardDialogs>(),
            new LocalizationManager());

        service.Open(project.Path);

        NUnit.Framework.Assert.That(
            calls,
            Is.EqualTo(
                new[] { "history-status", "factory-open", "add" }));
    }

    [Test]
    public void Open_LegacyCnSettings_DoesNotRewriteSettingsFile()
    {
        using var project = new TemporaryFolderProject();
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        File.WriteAllText(
            settingsPath,
            "{\r\n  \"Name\": \"旧工程\"\r\n}\r\n");
        var originalBytes = File.ReadAllBytes(settingsPath);
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(item => item.GetStatus(project.Path))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                []));
        FolderProjectContainer? opened = null;
        var packFiles = new Mock<IPackFileService>();
        packFiles.Setup(item => item.TryActivateFolderProject(project.Path))
            .Returns(false);
        packFiles.Setup(item => item.AddEditableFolderProject(
                It.IsAny<FolderProjectContainer>()))
            .Callback<FolderProjectContainer>(container => opened = container)
            .Returns<FolderProjectContainer>(container => container);
        var service = new FolderProjectOpenService(
            packFiles.Object,
            new FolderProjectFactory(),
            history.Object,
            Mock.Of<IFolderProjectHistoryWindowService>(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IStandardDialogs>(),
            new LocalizationManager());

        try
        {
            service.Open(project.Path);

            NUnit.Framework.Assert.That(
                File.ReadAllBytes(settingsPath),
                Is.EqualTo(originalBytes));
        }
        finally
        {
            opened?.Dispose();
        }
    }

    [Test]
    public void Open_LegacyCorruptionDetectionFiles_DoesNotChangeDisk()
    {
        using var project = new TemporaryFolderProject();
        var placeholderPath = Path.Combine(
            project.Path,
            "!!!packfile_corruction_detection",
            "packfile_corruction_detection_1.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(placeholderPath)!);
        File.WriteAllBytes(placeholderPath, [1, 2, 3]);
        var settingsPath = Path.Combine(
            project.Path,
            FolderProjectSettings.CnFileName);
        var settingsBytes = File.ReadAllBytes(settingsPath);
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(item => item.GetStatus(project.Path))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                []));
        FolderProjectContainer? opened = null;
        var packFiles = new Mock<IPackFileService>();
        packFiles.Setup(item => item.TryActivateFolderProject(project.Path))
            .Returns(false);
        packFiles.Setup(item => item.AddEditableFolderProject(
                It.IsAny<FolderProjectContainer>()))
            .Callback<FolderProjectContainer>(container => opened = container)
            .Returns<FolderProjectContainer>(container => container);
        var service = new FolderProjectOpenService(
            packFiles.Object,
            new FolderProjectFactory(),
            history.Object,
            Mock.Of<IFolderProjectHistoryWindowService>(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IStandardDialogs>(),
            new LocalizationManager());

        try
        {
            service.Open(project.Path);
            opened!.StartWatching();
            Thread.Sleep(500);

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(
                    File.ReadAllBytes(placeholderPath),
                    Is.EqualTo(new byte[] { 1, 2, 3 }));
                NUnit.Framework.Assert.That(
                    File.ReadAllBytes(settingsPath),
                    Is.EqualTo(settingsBytes));
            });
        }
        finally
        {
            opened?.Dispose();
        }
    }

    [Test]
    public void Open_AlreadyLoadedProject_ActivatesWithoutOpeningAgain()
    {
        using var project = new TemporaryFolderProject();
        using var container = FolderProjectContainer.Open(project.Path);
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(item => item.GetStatus(project.Path))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                []));
        var packFiles = new Mock<IPackFileService>(MockBehavior.Strict);
        packFiles.Setup(item => item.TryActivateFolderProject(project.Path))
            .Returns(true);
        var factory = new Mock<IFolderProjectFactory>(MockBehavior.Strict);
        var service = new FolderProjectOpenService(
            packFiles.Object,
            factory.Object,
            history.Object,
            Mock.Of<IFolderProjectHistoryWindowService>(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IStandardDialogs>(),
            new LocalizationManager());

        service.Open(project.Path);

        packFiles.Verify(
            item => item.TryActivateFolderProject(project.Path),
            Times.Once);
        factory.Verify(
            item => item.Open(It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public void Open_UninitializedProject_RequiresInitializationBeforeOpening()
    {
        using var project = new TemporaryFolderProject();
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(item => item.GetStatus(project.Path))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.NotInitialized,
                null,
                []));
        var factory = new Mock<IFolderProjectFactory>();
        var window = new Mock<IFolderProjectHistoryWindowService>();
        var dialogs = new Mock<IStandardDialogs>();
        var service = new FolderProjectOpenService(
            Mock.Of<IPackFileService>(),
            factory.Object,
            history.Object,
            window.Object,
            new ApplicationSettingsService(),
            dialogs.Object,
            LoadLocalization());

        service.Open(project.Path);

        factory.Verify(item => item.Open(It.IsAny<string>()), Times.Never);
        dialogs.Verify(item => item.ShowDialogBox(
            "必须先建立工程历史，才能打开并修改这个文件夹工程。",
            "文件夹工程错误"), Times.Once);
        window.Verify(item => item.ShowRecoveryDialog(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void Open_FactoryFails_RemovesRecentAndShowsGenericPrompt()
    {
        using var project = new TemporaryFolderProject();
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(item => item.GetStatus(project.Path))
            .Returns(new FolderProjectHistoryStatus(
                FolderProjectHistoryAvailability.Ready,
                "head",
                []));
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
            history.Object,
            Mock.Of<IFolderProjectHistoryWindowService>(),
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
