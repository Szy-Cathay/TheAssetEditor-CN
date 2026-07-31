using AssetEditor.UiCommands;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common;

namespace AssetEditorTests;

public class ImportPackAsFolderProjectCommandTests
{
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
            importDialogs.Setup(x => x.SelectTargetFolder(
                    It.IsAny<string>()))
                .Returns(target);
            var dialogs = new Mock<IStandardDialogs>();
            var command = new ImportPackAsFolderProjectCommand(
                Mock.Of<IPackFileService>(),
                Mock.Of<IPackFileContainerLoader>(),
                Mock.Of<IFolderProjectFactory>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                dialogs.Object,
                LoadLocalization(),
                importDialogs.Object);

            command.Execute();

            importDialogs.Verify(x => x.SelectOutputPack(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
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
        importDialogs.Setup(x => x.SelectTargetFolder(It.IsAny<string>()))
            .Returns("C:\\restricted");
        var dialogs = new Mock<IStandardDialogs>();
        var command = new ImportPackAsFolderProjectCommand(
            Mock.Of<IPackFileService>(),
            Mock.Of<IPackFileContainerLoader>(),
            Mock.Of<IFolderProjectFactory>(),
            new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            dialogs.Object,
            LoadLocalization(),
            importDialogs.Object,
            _ => throw new UnauthorizedAccessException());

        command.Execute();

        importDialogs.Verify(x => x.SelectOutputPack(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
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
}
