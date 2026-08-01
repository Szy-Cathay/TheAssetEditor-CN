using AssetEditor.UiCommands;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common;

using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class ImportPackAsFolderProjectCommandTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void Execute_UsesSetupDialogValues()
    {
        using var project = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        var source = new PackFileContainer("source")
        {
            Header = new PFHeader("PFH6", PackFileCAType.MOD),
        };
        var loader = new Mock<IPackFileContainerLoader>();
        loader.Setup(item => item.Load("source.pack"))
            .Returns(source);
        var importDialogs = new Mock<IFolderProjectImportDialogs>();
        importDialogs.Setup(item => item.SelectSourcePack(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns("source.pack");
        var setupDialogs = new Mock<IFolderProjectSetupDialogs>();
        setupDialogs.Setup(item => item.ShowSetup(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(
                new FolderProjectSetupDialogResult(
                    project.Path,
                    output.Path,
                    true));
        FolderProjectContainer? importedProject = null;
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(item => item.AddContainer(
                It.IsAny<PackFileContainer>(),
                true))
            .Callback<PackFileContainer, bool>((container, _) =>
                importedProject = (FolderProjectContainer)container)
            .Returns<PackFileContainer, bool>((container, _) => container);
        var versionControl =
            new Mock<IFolderProjectVersionControlService>();
        var command = new ImportPackAsFolderProjectCommand(
            packFileService.Object,
            loader.Object,
            new FolderProjectFactory(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            Mock.Of<IStandardDialogs>(),
            LoadLocalization(),
            importDialogs.Object,
            setupDialogs.Object,
            null,
            versionControl.Object);

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
            });
            versionControl.Verify(item => item.Initialize(
                project.Path,
                It.IsAny<FolderProjectGitIdentity>(),
                "master"), Times.Once);
        }
        finally
        {
            importedProject?.Dispose();
        }
    }

    [Test]
    public void Execute_NonEmptyTarget_StopsBeforeSelectingOutput()
    {
        var target = Path.Combine(
            Path.GetTempPath(),
            $"ae-import-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "existing.bin"), [1]);
        try
        {
            var importDialogs =
                new Mock<IFolderProjectImportDialogs>();
            importDialogs.Setup(x => x.SelectSourcePack(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns("source.pack");
            var setupDialogs = new Mock<IFolderProjectSetupDialogs>();
            setupDialogs.Setup(x => x.ShowSetup(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns(
                    new FolderProjectSetupDialogResult(
                        target,
                        Path.GetDirectoryName(target)!,
                        false));
            var dialogs = new Mock<IStandardDialogs>();
            var factory = new Mock<IFolderProjectFactory>();
            var command = new ImportPackAsFolderProjectCommand(
                Mock.Of<IPackFileService>(),
                Mock.Of<IPackFileContainerLoader>(),
                factory.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                dialogs.Object,
                LoadLocalization(),
                importDialogs.Object,
                setupDialogs.Object);

            command.Execute();

            factory.Verify(x => x.ImportPack(
                It.IsAny<PackFileContainer>(),
                It.IsAny<string>(),
                It.IsAny<FolderProjectSettings>()), Times.Never);
            dialogs.Verify(x => x.ShowDialogBox(
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }
        finally
        {
            Directory.Delete(target, true);
        }
    }

    [Test]
    public void Execute_TargetValidationAccessDenied_ShowsNormalError()
    {
        var importDialogs = new Mock<IFolderProjectImportDialogs>();
        importDialogs.Setup(x => x.SelectSourcePack(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns("source.pack");
        var setupDialogs = new Mock<IFolderProjectSetupDialogs>();
        setupDialogs.Setup(x => x.ShowSetup(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(
                new FolderProjectSetupDialogResult(
                    "C:\\restricted",
                    "C:\\output",
                    false));
        var dialogs = new Mock<IStandardDialogs>();
        var factory = new Mock<IFolderProjectFactory>();
        var command = new ImportPackAsFolderProjectCommand(
            Mock.Of<IPackFileService>(),
            Mock.Of<IPackFileContainerLoader>(),
            factory.Object,
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            dialogs.Object,
            LoadLocalization(),
            importDialogs.Object,
            setupDialogs.Object,
            _ => throw new UnauthorizedAccessException());

        command.Execute();

        factory.Verify(x => x.ImportPack(
            It.IsAny<PackFileContainer>(),
            It.IsAny<string>(),
            It.IsAny<FolderProjectSettings>()), Times.Never);
        dialogs.Verify(x => x.ShowDialogBox(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }

    private static LocalizationManager LoadLocalization()
    {
        var localizationManager = new LocalizationManager();
        localizationManager.LoadLanguage();
        return localizationManager;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-project-import-dialog-{Guid.NewGuid():N}");

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
