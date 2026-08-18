using Moq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

namespace AssetEditorTests;

public class FolderProjectPackGenerationGuardTests
{
    [SetUp]
    public void SetUp()
    {
        new LocalizationManager().LoadLanguage();
    }

    [TestCase(ShowMessageBoxResult.OK, true)]
    [TestCase(ShowMessageBoxResult.Cancel, false)]
    public void CanGenerate_DirtyFolderProject_UsesUserChoice(
        ShowMessageBoxResult choice,
        bool expected)
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(service => service.GetDisplayStatus(directory.Path))
            .Returns(CreateStatus(changeCount: 2));
        var dialogs = new Mock<IStandardDialogs>();
        dialogs.Setup(service => service.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(choice);
        var guard = new FolderProjectPackGenerationGuard(
            history.Object,
            dialogs.Object);

        var result = guard.CanGenerate(project);

        NUnitAssert.That(result, Is.EqualTo(expected));
        dialogs.Verify(service => service.ShowYesNoBox(
            It.Is<string>(message =>
                message.Contains("尚未创建还原点") &&
                message.Contains("2")),
            It.Is<string>(title => title.Contains("还原点"))),
            Times.Once);
    }

    [Test]
    public void CanGenerate_CleanFolderProject_DoesNotPrompt()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(service => service.GetDisplayStatus(directory.Path))
            .Returns(CreateStatus(changeCount: 0));
        var dialogs = new Mock<IStandardDialogs>();
        var guard = new FolderProjectPackGenerationGuard(
            history.Object,
            dialogs.Object);

        var result = guard.CanGenerate(project);

        NUnitAssert.That(result, Is.True);
        dialogs.Verify(service => service.ShowYesNoBox(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void CanGenerate_RegularPack_DoesNotReadProjectHistory()
    {
        var history = new Mock<IFolderProjectHistoryService>();
        var dialogs = new Mock<IStandardDialogs>();
        var guard = new FolderProjectPackGenerationGuard(
            history.Object,
            dialogs.Object);

        var result = guard.CanGenerate(
            new PackFileContainer("regular.pack"));

        NUnitAssert.That(result, Is.True);
        history.Verify(service => service.GetDisplayStatus(
            It.IsAny<string>()), Times.Never);
        dialogs.Verify(service => service.ShowYesNoBox(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void CanGenerate_StatusCheckFails_BlocksGeneration()
    {
        using var directory = new TemporaryDirectory();
        using var project = FolderProjectContainer.Create(
            directory.Path,
            new FolderProjectSettings { Name = "测试工程" });
        var history = new Mock<IFolderProjectHistoryService>();
        history.Setup(service => service.GetDisplayStatus(directory.Path))
            .Throws(new FolderProjectHistoryException(
                FolderProjectHistoryError.StorageFailure,
                "status failed"));
        var dialogs = new Mock<IStandardDialogs>();
        var guard = new FolderProjectPackGenerationGuard(
            history.Object,
            dialogs.Object);

        var result = guard.CanGenerate(project);

        NUnitAssert.That(result, Is.False);
        dialogs.Verify(service => service.ShowDialogBox(
            It.Is<string>(message => message.Contains("取消生成 Pack")),
            It.Is<string>(title => title.Contains("还原点"))), Times.Once);
        dialogs.Verify(service => service.ShowYesNoBox(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    private static FolderProjectHistoryStatus CreateStatus(int changeCount) =>
        new(
            FolderProjectHistoryAvailability.Ready,
            new string('1', 40),
            Enumerable.Range(0, changeCount)
                .Select(index => new FolderProjectUnrecordedChange(
                    $"db/change-{index}.tsv",
                    FolderProjectUnrecordedChangeKind.Modified))
                .ToArray());

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-pack-generation-guard-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
