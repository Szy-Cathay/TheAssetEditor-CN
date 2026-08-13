using AssetEditor.UiCommands;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace AssetEditorTests
{
    public class OpenReferencePackCommandTests
    {
        [Test]
        public void Execute_SelectedPack_LoadsReferenceWithoutChangingWorkspace()
        {
            const string packPath = @"D:\packs\reference.pack";
            var reference = new PackFileContainer("reference.pack")
            {
                SystemFilePath = packPath,
            };
            var loader = new Mock<IPackFileContainerLoader>(
                MockBehavior.Strict);
            loader.Setup(item => item.Load(packPath)).Returns(reference);
            var packFileService = new Mock<IPackFileService>(
                MockBehavior.Strict);
            packFileService.Setup(item => item.AddReferencePack(reference))
                .Returns(reference);
            var command = new OpenReferencePackCommand(
                packFileService.Object,
                loader.Object,
                Mock.Of<IStandardDialogs>(),
                LoadLocalization(),
                () => packPath);

            command.Execute();

            packFileService.Verify(
                item => item.AddReferencePack(reference),
                Times.Once);
            packFileService.Verify(
                item => item.SetEditablePack(
                    It.IsAny<PackFileContainer?>()),
                Times.Never);
            packFileService.Verify(
                item => item.AddContainer(
                    It.IsAny<PackFileContainer>()),
                Times.Never);
        }

        [Test]
        public void Execute_SelectionCanceled_DoesNotLoadOrChangeWorkspace()
        {
            var loader = new Mock<IPackFileContainerLoader>(
                MockBehavior.Strict);
            var packFileService = new Mock<IPackFileService>(
                MockBehavior.Strict);
            var command = new OpenReferencePackCommand(
                packFileService.Object,
                loader.Object,
                Mock.Of<IStandardDialogs>(),
                LoadLocalization(),
                () => null);

            command.Execute();

            loader.VerifyNoOtherCalls();
            packFileService.VerifyNoOtherCalls();
        }

        [Test]
        public void Execute_LoadFailure_ShowsChineseErrorWithoutChangingWorkspace()
        {
            const string packPath = @"D:\packs\broken.pack";
            var loader = new Mock<IPackFileContainerLoader>();
            loader.Setup(item => item.Load(packPath))
                .Returns((PackFileContainer?)null);
            var packFileService = new Mock<IPackFileService>(
                MockBehavior.Strict);
            var dialogs = new Mock<IStandardDialogs>();
            var command = new OpenReferencePackCommand(
                packFileService.Object,
                loader.Object,
                dialogs.Object,
                LoadLocalization(),
                () => packPath);

            command.Execute();

            dialogs.Verify(item => item.ShowDialogBox(
                It.IsAny<string>(),
                "错误"), Times.Once);
            packFileService.VerifyNoOtherCalls();
        }

        private static LocalizationManager LoadLocalization()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            return localizationManager;
        }
    }
}
