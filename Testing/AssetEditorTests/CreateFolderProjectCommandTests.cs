using AssetEditor.UiCommands;

using Moq;

using NUnit.Framework;

using System.Xml.Linq;

using NUnitAssert = NUnit.Framework.Assert;

using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;

namespace AssetEditorTests;

public class CreateFolderProjectCommandTests
{
    [Test]
    public void SetupWindow_PathFieldsDoNotShowHorizontalScrollBars()
    {
        var solutionRoot = FindSolutionRoot();
        var document = XDocument.Load(Path.Combine(
            solutionRoot,
            "AssetEditor",
            "Views",
            "FolderProject",
            "FolderProjectSetupWindow.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var pathFields = document
            .Descendants(presentation + "TextBox")
            .Where(element =>
                element.Attribute(xaml + "Name")?.Value is
                    "ProjectFolderTextBox" or "OutputFolderTextBox")
            .ToArray();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(pathFields, Has.Length.EqualTo(2));
            NUnitAssert.That(
                pathFields.Select(element =>
                    element.Attribute("HorizontalScrollBarVisibility")?.Value),
                Is.All.EqualTo("Hidden"));
        });
    }

    [Test]
    public void SetupWindow_DoesNotExposeGitConfiguration()
    {
        var path = Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Views",
            "FolderProject",
            "FolderProjectSetupWindow.xaml");
        var xaml = File.ReadAllText(path);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(xaml, Does.Not.Contain("PrimaryBranch"));
            NUnitAssert.That(xaml, Does.Not.Contain("Git"));
            NUnitAssert.That(
                xaml,
                Does.Contain("FolderProject.Setup.HistoryRequired"));
        });
    }

    [Test]
    public void FolderProjectCreation_UsesExpandableOperationProgress()
    {
        var viewDirectory = Path.Combine(
            FindSolutionRoot(),
            "AssetEditor",
            "Views",
            "FolderProject");
        XNamespace xaml =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        var progressViews = Directory
            .EnumerateFiles(viewDirectory, "*.xaml")
            .Select(XDocument.Load)
            .SelectMany(document =>
                document.Descendants())
            .Where(element =>
                element.Name.LocalName == "OperationProgressView" &&
                element.Attribute(xaml + "Name")?.Value ==
                "FolderProjectOperationProgress")
            .ToArray();

        NUnitAssert.That(progressViews, Has.Length.EqualTo(1));
    }

    [Test]
    public void Execute_UsesSetupDialogValues()
    {
        using var project = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        var setupDialogs = new Mock<IFolderProjectSetupDialogs>();
        setupDialogs.Setup(item => item.ShowSetup(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(
                new FolderProjectSetupDialogResult(
                    project.Path,
                    output.Path,
                    true));
        var history = new Mock<IFolderProjectHistoryService>();
        var progressRunner = new Mock<IFolderProjectProgressRunner>();
        var progressVisible = false;
        var initializedWhileProgressVisible = false;
        var progressUpdates = new List<OperationProgressUpdate>();
        progressRunner.Setup(item => item.Run(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<
                    Action<OperationProgressUpdate>,
                    FolderProjectContainer?>>()))
            .Returns(
                (string _,
                 string _,
                 Func<Action<OperationProgressUpdate>,
                     FolderProjectContainer?> operation) =>
                {
                    progressVisible = true;
                    try
                    {
                        return operation(progressUpdates.Add);
                    }
                    finally
                    {
                        progressVisible = false;
                    }
                });
        history.Setup(item => item.Initialize(
                project.Path,
                It.IsAny<Action<FolderProjectHistoryProgress>>()))
            .Callback(
                (
                    string _,
                    Action<FolderProjectHistoryProgress> progress) =>
                {
                    initializedWhileProgressVisible = progressVisible;
                    progress(new FolderProjectHistoryProgress(
                        FolderProjectHistoryProgressStage
                            .CreatingInitialRestorePoint,
                        "audio/voice.wem",
                        1,
                        2));
                });
        FolderProjectContainer? createdProject = null;
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(item => item.AddEditableFolderProject(
                It.IsAny<FolderProjectContainer>()))
            .Callback<FolderProjectContainer>(container =>
                createdProject = container)
            .Returns<FolderProjectContainer>(container => container);
        var command = new CreateFolderProjectCommand(
            packFileService.Object,
            new FolderProjectFactory(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IStandardDialogs>(),
            LoadLocalization(),
            setupDialogs.Object,
            history.Object,
            progressRunner.Object);

        try
        {
            command.Execute();

            var settings = FolderProjectSettings.Load(project.Path);
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    settings.OutputPackPath,
                    Is.EqualTo(
                        Path.Combine(
                            output.Path,
                            Path.GetFileName(project.Path) + ".pack")));
                NUnitAssert.That(
                    settings.EnablePackFileCorruptionDetection,
                    Is.True);
                NUnitAssert.That(initializedWhileProgressVisible, Is.True);
                NUnitAssert.That(
                    progressUpdates.Any(update =>
                        update.Status == "正在创建初始还原点" &&
                        update.Detail == "audio/voice.wem" &&
                        update.Completed == 1 &&
                        update.Total == 2),
                    Is.True);
            });
            progressRunner.Verify(item => item.Run(
                It.IsAny<string>(),
                It.Is<string>(message =>
                    message.Contains(
                        "初始还原点",
                        StringComparison.Ordinal)),
                It.IsAny<Func<
                    Action<OperationProgressUpdate>,
                    FolderProjectContainer?>>()), Times.Once);
            history.Verify(item => item.Initialize(
                project.Path,
                It.IsAny<Action<FolderProjectHistoryProgress>>()),
                Times.Once);
            packFileService.Verify(item =>
                item.AddEditableFolderProject(
                    It.IsAny<FolderProjectContainer>()),
                Times.Once);
        }
        finally
        {
            createdProject?.Dispose();
        }
    }

    [Test]
    public void Execute_SetupCancellationDoesNotCreateProject()
    {
        var setupDialogs = new Mock<IFolderProjectSetupDialogs>();
        setupDialogs.Setup(item => item.ShowSetup(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns((FolderProjectSetupDialogResult?)null);
        var factory = new Mock<IFolderProjectFactory>();
        var command = new CreateFolderProjectCommand(
            Mock.Of<IPackFileService>(),
            factory.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IStandardDialogs>(),
            LoadLocalization(),
            setupDialogs.Object);

        command.Execute();

        factory.Verify(item => item.Create(
            It.IsAny<string>(),
            It.IsAny<FolderProjectSettings>()), Times.Never);
    }

    private static LocalizationManager LoadLocalization()
    {
        var localizationManager = new LocalizationManager();
        localizationManager.LoadLanguage();
        return localizationManager;
    }

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
            $"ae-folder-project-dialog-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
