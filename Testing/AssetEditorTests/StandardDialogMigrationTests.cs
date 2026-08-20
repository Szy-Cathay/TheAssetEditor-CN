using AssetEditor.UiCommands;
using CommonControls.Editors.AnimationBatchExporter;
using Editors.Reports.DeepSearch;
using GameWorld.Core.Services;
using Moq;
using NUnit.Framework;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;

namespace AssetEditorTests
{

    public class StandardDialogMigrationTests
    {
        [Test]
        public void AnimationBatchExporter_NoEditablePack_UsesStandardDialog()
        {
            _ = new LocalizationManager();
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(service => service.GetAllPackfileContainers())
                .Returns([]);
            var dialogs = new Mock<IStandardDialogs>();
            var viewModel = new AnimationBatchExportViewModel(
                packFileService.Object,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                dialogs.Object);

            viewModel.Process();

            dialogs.Verify(service => service.ShowDialogBox(
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
            dialogs.Verify(service => service.ShowYesNoBox(
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void AnimationBatchExporter_Confirmed_ShowsResultsWithStandardDialog()
        {
            _ = new LocalizationManager();
            var editablePack = new PackFileContainer("output.pack");
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(service => service.GetEditablePack())
                .Returns(editablePack);
            packFileService.Setup(service => service.GetAllPackfileContainers())
                .Returns([editablePack]);
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(service => service.ShowYesNoBox(
                    It.IsAny<string>(),
                    ""))
                .Returns(ShowMessageBoxResult.OK);
            var viewModel = new AnimationBatchExportViewModel(
                packFileService.Object,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                dialogs.Object);

            viewModel.Process();

            dialogs.Verify(service => service.ShowErrorViewDialog(
                "Bach result",
                It.IsAny<ErrorList>(),
                true), Times.Once);
        }

        [Test]
        public void OpenGamePack_MissingGamePath_UsesStandardDialog()
        {
            _ = new LocalizationManager();
            var dialogs = new Mock<IStandardDialogs>();
            var command = new OpenGamePackCommand(
                Mock.Of<IPackFileService>(),
                Mock.Of<IPackFileContainerLoader>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                dialogs.Object);

            command.Execute(GameTypeEnum.Warhammer3);

            dialogs.Verify(service => service.ShowDialogBox(
                It.IsAny<string>(),
                "Error"), Times.Once);
        }

        [Test]
        public void OpenGamePack_AlreadyLoaded_UsesStandardDialog()
        {
            _ = new LocalizationManager();
            const string gamePath = @"D:\Games\Warhammer3";
            var settingsService = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            settingsService.CurrentSettings.GameDirectories.Add(
                new ApplicationSettings.GamePathPair
                {
                    Game = GameTypeEnum.Warhammer3,
                    Path = gamePath,
                });
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(service => service.GetAllPackfileContainers())
                .Returns([new PackFileContainer("data.pack")
            {
                SystemFilePath = gamePath,
            }]);
            var dialogs = new Mock<IStandardDialogs>();
            var command = new OpenGamePackCommand(
                packFileService.Object,
                Mock.Of<IPackFileContainerLoader>(),
                settingsService,
                dialogs.Object);

            command.Execute(GameTypeEnum.Warhammer3);

            dialogs.Verify(service => service.ShowDialogBox(
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void SavePack_Failure_UsesStandardDialog()
        {
            _ = new LocalizationManager();
            var pack = new PackFileContainer("output.pack")
            {
                SystemFilePath = @"D:\output.pack",
            };
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(service => service.GetEditablePack())
                .Returns(pack);
            packFileService.Setup(service => service.SavePackContainer(
                    pack,
                    pack.SystemFilePath,
                    It.IsAny<GameInformation>()))
                .Throws(new IOException("save failed"));
            var dialogs = new Mock<IStandardDialogs>();
            var generationGuard = new Mock<
                IFolderProjectPackGenerationGuard>();
            generationGuard.Setup(guard => guard.CanGenerate(pack))
                .Returns(true);
            var command = new SavePackFileContainerCommand(
                packFileService.Object,
                dialogs.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                generationGuard.Object);

            command.Execute();

            dialogs.Verify(service => service.ShowDialogBox(
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void DeepSearch_Confirmed_UsesStandardTextInput()
        {
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(service => service.GetAllPackfileContainers())
                .Returns([]);
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(service => service.ShowTextInputDialog(
                    "Deep search - Output in console",
                    ""))
                .Returns(new TextInputDialogResult(true, "needle"));
            var command = new DeepSearchCommand(
                new DeepSearchReport(
                    packFileService.Object,
                    Mock.Of<IPackFileContainerLoader>()),
                dialogs.Object);

            command.Execute();

            packFileService.Verify(service => service.GetAllPackfileContainers(), Times.Once);
        }

        [Test]
        public void DeepSearch_Cancel_DoesNotSearch()
        {
            var packFileService = new Mock<IPackFileService>();
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(service => service.ShowTextInputDialog(
                    "Deep search - Output in console",
                    ""))
                .Returns(new TextInputDialogResult(false, "ignored"));
            var command = new DeepSearchCommand(
                new DeepSearchReport(
                    packFileService.Object,
                    Mock.Of<IPackFileContainerLoader>()),
                dialogs.Object);

            command.Execute();

            packFileService.Verify(service => service.GetAllPackfileContainers(), Times.Never);
        }

        [Test]
        public void DeepSearch_EmptyInput_UsesStandardDialogAndDoesNotSearch()
        {
            _ = new LocalizationManager();
            var packFileService = new Mock<IPackFileService>();
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(service => service.ShowTextInputDialog(
                    "Deep search - Output in console",
                    ""))
                .Returns(new TextInputDialogResult(true, "  "));
            var command = new DeepSearchCommand(
                new DeepSearchReport(
                    packFileService.Object,
                    Mock.Of<IPackFileContainerLoader>()),
                dialogs.Object);

            command.Execute();

            dialogs.Verify(service => service.ShowDialogBox(
                It.IsAny<string>(),
                "Error"), Times.Once);
            packFileService.Verify(service => service.GetAllPackfileContainers(), Times.Never);
        }
    }
}
