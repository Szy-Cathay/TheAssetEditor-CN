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
                    true,
                    "main"));
        var versionControl =
            new Mock<IFolderProjectVersionControlService>();
        var command = new CreateFolderProjectCommand(
            Mock.Of<IPackFileService>(),
            new FolderProjectFactory(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IStandardDialogs>(),
            LoadLocalization(),
            setupDialogs.Object,
            versionControl.Object);

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
        });
        versionControl.Verify(item => item.Initialize(
            project.Path,
            It.IsAny<FolderProjectGitIdentity>(),
            "main"), Times.Once);
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
