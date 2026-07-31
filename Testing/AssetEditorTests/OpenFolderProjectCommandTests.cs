using AssetEditor.Services;
using AssetEditor.UiCommands;
using Moq;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditorTests;

public class OpenFolderProjectCommandTests
{
    [Test]
    public void Execute_SelectedFolder_UsesUnifiedOpenService()
    {
        const string projectRoot = @"D:\projects\selected";
        var openService = new Mock<IFolderProjectOpenService>();
        var command = new OpenFolderProjectCommand(
            openService.Object,
            LoadLocalization(),
            () => projectRoot);

        command.Execute();

        openService.Verify(
            service => service.Open(projectRoot),
            Times.Once);
    }

    [Test]
    public void Execute_SelectionCanceled_DoesNotOpenProject()
    {
        var openService = new Mock<IFolderProjectOpenService>();
        var command = new OpenFolderProjectCommand(
            openService.Object,
            LoadLocalization(),
            () => null);

        command.Execute();

        openService.Verify(
            service => service.Open(It.IsAny<string>()),
            Times.Never);
    }

    private static LocalizationManager LoadLocalization()
    {
        var localizationManager = new LocalizationManager();
        localizationManager.LoadLanguage();
        return localizationManager;
    }
}
