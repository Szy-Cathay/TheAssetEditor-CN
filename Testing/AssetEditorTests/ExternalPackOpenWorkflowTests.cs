using AssetEditor.Services;
using AssetEditor.UiCommands;
using Editors.Ipc;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;

namespace AssetEditorTests
{
    public class ExternalPackOpenWorkflowTests
    {
        [Test]
        public async Task OpenAsync_ChoiceCancelled_DoesNotTouchContainers()
        {
            const string packPath = @"D:\packs\reference.pack";
            var packFiles = new Mock<IPackFileService>(
                MockBehavior.Strict);
            var choices = new Mock<IExternalPackOpenChoiceDialog>(
                MockBehavior.Strict);
            choices.Setup(item => item.Choose(packPath))
                .Returns(ExternalPackOpenChoice.Cancelled);
            var workflow = new ExternalPackOpenWorkflow(
                packFiles.Object,
                choices.Object,
                Mock.Of<IUiCommandFactory>(),
                Mock.Of<IStandardDialogs>(),
                LoadLocalization());

            var result = await workflow.OpenAsync(
                packPath,
                CancellationToken.None);

            NUnit.Framework.Assert.That(
                result.Status,
                Is.EqualTo(ExternalPackOpenStatus.Cancelled));
            packFiles.VerifyNoOtherCalls();
        }

        [Test]
        public async Task OpenAsync_ReferenceChoice_AddsReadOnlyReferenceOnly()
        {
            const string packPath = @"D:\packs\reference.pack";
            var reference = new PackFileContainer("reference.pack")
            {
                SystemFilePath = packPath,
            };
            var packFiles = new Mock<IPackFileService>(
                MockBehavior.Strict);
            packFiles.Setup(item => item.GetAllPackfileContainers())
                .Returns([]);
            packFiles.Setup(item => item.AddReferencePack(reference))
                .Returns(reference);
            var loader = new Mock<IPackFileContainerLoader>(
                MockBehavior.Strict);
            loader.Setup(item => item.Load(packPath)).Returns(reference);
            var command = new OpenReferencePackCommand(
                packFiles.Object,
                loader.Object,
                Mock.Of<IStandardDialogs>(),
                LoadLocalization());
            var commandFactory = new Mock<IUiCommandFactory>(
                MockBehavior.Strict);
            commandFactory.Setup(item => item.Create<OpenReferencePackCommand>(
                    It.IsAny<Action<OpenReferencePackCommand>?>()))
                .Returns(command);
            var choices = new Mock<IExternalPackOpenChoiceDialog>(
                MockBehavior.Strict);
            choices.Setup(item => item.Choose(packPath))
                .Returns(ExternalPackOpenChoice.OpenAsReference);
            var workflow = new ExternalPackOpenWorkflow(
                packFiles.Object,
                choices.Object,
                commandFactory.Object,
                Mock.Of<IStandardDialogs>(),
                LoadLocalization());

            var result = await workflow.OpenAsync(
                packPath,
                CancellationToken.None);

            NUnit.Framework.Assert.That(
                result.Status,
                Is.EqualTo(ExternalPackOpenStatus.OpenedAsReference));
            packFiles.Verify(
                item => item.AddReferencePack(reference),
                Times.Once);
            packFiles.Verify(
                item => item.AddEditableFolderProject(
                    It.IsAny<FolderProjectContainer>()),
                Times.Never);
            packFiles.Verify(
                item => item.SetEditablePack(
                    It.IsAny<PackFileContainer?>()),
                Times.Never);
            packFiles.Verify(
                item => item.AddContainer(
                    It.IsAny<PackFileContainer>()),
                Times.Never);
        }

        [Test]
        public async Task OpenAsync_SameReferencePath_DoesNotAddDuplicate()
        {
            const string packPath = @"D:\packs\reference.pack";
            var existing = new PackFileContainer("reference.pack")
            {
                SystemFilePath = packPath,
            };
            var packFiles = new Mock<IPackFileService>(
                MockBehavior.Strict);
            packFiles.Setup(item => item.GetAllPackfileContainers())
                .Returns([existing]);
            var choices = new Mock<IExternalPackOpenChoiceDialog>(
                MockBehavior.Strict);
            choices.Setup(item => item.Choose(packPath))
                .Returns(ExternalPackOpenChoice.OpenAsReference);
            var workflow = new ExternalPackOpenWorkflow(
                packFiles.Object,
                choices.Object,
                Mock.Of<IUiCommandFactory>(),
                Mock.Of<IStandardDialogs>(),
                LoadLocalization());

            var result = await workflow.OpenAsync(
                packPath,
                CancellationToken.None);

            NUnit.Framework.Assert.That(
                result.Status,
                Is.EqualTo(ExternalPackOpenStatus.OpenedAsReference));
            packFiles.Verify(
                item => item.GetAllPackfileContainers(),
                Times.Once);
            packFiles.VerifyNoOtherCalls();
        }

        [Test]
        public async Task OpenAsync_ImportChoiceWithReferenceLoaded_ReturnsConflict()
        {
            const string packPath = @"D:\packs\reference.pack";
            var existing = new PackFileContainer("reference.pack")
            {
                SystemFilePath = packPath,
            };
            var packFiles = new Mock<IPackFileService>(
                MockBehavior.Strict);
            packFiles.Setup(item => item.GetAllPackfileContainers())
                .Returns([existing]);
            var choices = new Mock<IExternalPackOpenChoiceDialog>(
                MockBehavior.Strict);
            choices.Setup(item => item.Choose(packPath))
                .Returns(ExternalPackOpenChoice.ImportAsFolderProject);
            var dialogs = new Mock<IStandardDialogs>();
            var workflow = new ExternalPackOpenWorkflow(
                packFiles.Object,
                choices.Object,
                Mock.Of<IUiCommandFactory>(),
                dialogs.Object,
                LoadLocalization());

            var result = await workflow.OpenAsync(
                packPath,
                CancellationToken.None);

            NUnit.Framework.Assert.That(
                result.Status,
                Is.EqualTo(ExternalPackOpenStatus.RoleConflict));
            dialogs.Verify(item => item.ShowDialogBox(
                It.Is<string>(message =>
                    message.Contains("参考", StringComparison.Ordinal) &&
                    message.Contains("工程", StringComparison.Ordinal)),
                It.IsAny<string>()), Times.Once);
            packFiles.Verify(
                item => item.GetAllPackfileContainers(),
                Times.Once);
            packFiles.VerifyNoOtherCalls();
        }

        [Test]
        public async Task OpenAsync_ImportChoice_ReusesImportCommandAndRecordsSource()
        {
            const string packPath = @"D:\packs\source.pack";
            using var projectRoot = new TemporaryDirectory();
            using var outputRoot = new TemporaryDirectory();
            var source = new PackFileContainer("source.pack")
            {
                Header = new PFHeader("PFH6", PackFileCAType.MOD),
                SystemFilePath = packPath,
            };
            source.FileList[@"db\example"] =
                PackFile.CreateFromBytes("example", [1]);
            var packFiles = new Mock<IPackFileService>(
                MockBehavior.Strict);
            packFiles.Setup(item => item.GetAllPackfileContainers())
                .Returns([]);
            FolderProjectContainer? importedProject = null;
            packFiles.Setup(item => item.AddEditableFolderProject(
                    It.IsAny<FolderProjectContainer>()))
                .Callback<FolderProjectContainer>(project =>
                    importedProject = project)
                .Returns<FolderProjectContainer>(project => project);
            var loader = new Mock<IPackFileContainerLoader>(
                MockBehavior.Strict);
            loader.Setup(item => item.Load(packPath)).Returns(source);
            var setupDialogs = new Mock<IFolderProjectSetupDialogs>(
                MockBehavior.Strict);
            setupDialogs.Setup(item => item.ShowSetup(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns(new FolderProjectSetupDialogResult(
                    projectRoot.Path,
                    outputRoot.Path,
                    false));
            var history =
                new Mock<IFolderProjectHistoryService>();
            var progressRunner = new Mock<IFolderProjectProgressRunner>();
            progressRunner.Setup(item => item.RunCancelable(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<Func<
                        Action<OperationProgressUpdate>,
                        CancellationToken,
                        FolderProjectContainer?>>()))
                .Returns((
                    string _,
                    string _,
                    CancellationToken cancellationToken,
                    Func<Action<OperationProgressUpdate>,
                        CancellationToken,
                        FolderProjectContainer?> operation) =>
                    new FolderProjectProgressResult(
                        operation(_ => { }, cancellationToken),
                        false));
            var importCommand = new ImportPackAsFolderProjectCommand(
                packFiles.Object,
                loader.Object,
                new FolderProjectFactory(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                Mock.Of<IStandardDialogs>(),
                LoadLocalization(),
                Mock.Of<IFolderProjectImportDialogs>(),
                setupDialogs.Object,
                null,
                history.Object,
                progressRunner.Object);
            var commandFactory = new Mock<IUiCommandFactory>(
                MockBehavior.Strict);
            commandFactory.Setup(item =>
                    item.Create<ImportPackAsFolderProjectCommand>(
                        It.IsAny<Action<
                            ImportPackAsFolderProjectCommand>?>()))
                .Returns(importCommand);
            var choices = new Mock<IExternalPackOpenChoiceDialog>(
                MockBehavior.Strict);
            choices.Setup(item => item.Choose(packPath))
                .Returns(ExternalPackOpenChoice.ImportAsFolderProject);
            var workflow = new ExternalPackOpenWorkflow(
                packFiles.Object,
                choices.Object,
                commandFactory.Object,
                Mock.Of<IStandardDialogs>(),
                LoadLocalization());

            try
            {
                var result = await workflow.OpenAsync(
                    packPath,
                    CancellationToken.None);

                NUnit.Framework.Assert.Multiple(() =>
                {
                    NUnit.Framework.Assert.That(
                        result.Status,
                        Is.EqualTo(
                            ExternalPackOpenStatus
                                .ImportedAsFolderProject));
                    NUnit.Framework.Assert.That(
                        importedProject?.SourcePackFilePaths,
                        Does.Contain(Path.GetFullPath(packPath)));
                    NUnit.Framework.Assert.That(
                        FolderProjectSettings.Load(projectRoot.Path)
                            .SourcePackPath,
                        Is.EqualTo(Path.GetFullPath(packPath)));
                });
            }
            finally
            {
                importedProject?.Dispose();
            }
        }

        [Test]
        public async Task OpenAsync_SameImportedPath_ActivatesExistingProject()
        {
            const string packPath = @"D:\packs\source.pack";
            using var projectRoot = new TemporaryDirectory();
            var settings = new FolderProjectSettings
            {
                Name = "existing",
                SourcePackPath = packPath,
                OutputPackPath = Path.Combine(
                    Path.GetTempPath(),
                    $"existing-{Guid.NewGuid():N}.pack"),
                GameVersion = GameTypeEnum.Warhammer3,
            };
            using (FolderProjectContainer.Create(
                       projectRoot.Path,
                       settings))
            {
            }
            using var project = FolderProjectContainer.Open(projectRoot.Path);
            var packFiles = new Mock<IPackFileService>(
                MockBehavior.Strict);
            packFiles.Setup(item => item.GetAllPackfileContainers())
                .Returns([project]);
            packFiles.Setup(item => item.TryActivateFolderProject(
                    project.ProjectRoot))
                .Returns(true);
            var choices = new Mock<IExternalPackOpenChoiceDialog>(
                MockBehavior.Strict);
            choices.Setup(item => item.Choose(packPath))
                .Returns(ExternalPackOpenChoice.ImportAsFolderProject);
            var workflow = new ExternalPackOpenWorkflow(
                packFiles.Object,
                choices.Object,
                Mock.Of<IUiCommandFactory>(),
                Mock.Of<IStandardDialogs>(),
                LoadLocalization());

            var result = await workflow.OpenAsync(
                packPath,
                CancellationToken.None);

            NUnit.Framework.Assert.That(
                result.Status,
                Is.EqualTo(
                    ExternalPackOpenStatus.ImportedAsFolderProject));
            packFiles.Verify(item => item.TryActivateFolderProject(
                project.ProjectRoot), Times.Once);
            packFiles.Verify(
                item => item.GetAllPackfileContainers(),
                Times.Once);
            packFiles.VerifyNoOtherCalls();
        }

        [Test]
        public async Task OpenAsync_ImportFailure_KeepsExistingWorkspaceUntouched()
        {
            const string packPath = @"D:\packs\broken.pack";
            using var projectRoot = new TemporaryDirectory();
            using var outputRoot = new TemporaryDirectory();
            var existingWorkspace = new PackFileContainer("workspace");
            var packFiles = new Mock<IPackFileService>(
                MockBehavior.Strict);
            packFiles.Setup(item => item.GetAllPackfileContainers())
                .Returns([existingWorkspace]);
            var loader = new Mock<IPackFileContainerLoader>(
                MockBehavior.Strict);
            loader.Setup(item => item.Load(packPath))
                .Returns((PackFileContainer?)null);
            var setupDialogs = new Mock<IFolderProjectSetupDialogs>(
                MockBehavior.Strict);
            setupDialogs.Setup(item => item.ShowSetup(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns(new FolderProjectSetupDialogResult(
                    projectRoot.Path,
                    outputRoot.Path,
                    false));
            var progressRunner = new Mock<IFolderProjectProgressRunner>();
            progressRunner.Setup(item => item.RunCancelable(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<Func<
                        Action<OperationProgressUpdate>,
                        CancellationToken,
                        FolderProjectContainer?>>()))
                .Returns((
                    string _,
                    string _,
                    CancellationToken cancellationToken,
                    Func<Action<OperationProgressUpdate>,
                        CancellationToken,
                        FolderProjectContainer?> operation) =>
                    new FolderProjectProgressResult(
                        operation(_ => { }, cancellationToken),
                        false));
            var command = new ImportPackAsFolderProjectCommand(
                packFiles.Object,
                loader.Object,
                Mock.Of<IFolderProjectFactory>(),
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                Mock.Of<IStandardDialogs>(),
                LoadLocalization(),
                Mock.Of<IFolderProjectImportDialogs>(),
                setupDialogs.Object,
                null,
                Mock.Of<IFolderProjectHistoryService>(),
                progressRunner.Object);
            var commandFactory = new Mock<IUiCommandFactory>(
                MockBehavior.Strict);
            commandFactory.Setup(item =>
                    item.Create<ImportPackAsFolderProjectCommand>(
                        It.IsAny<Action<
                            ImportPackAsFolderProjectCommand>?>()))
                .Returns(command);
            var choices = new Mock<IExternalPackOpenChoiceDialog>(
                MockBehavior.Strict);
            choices.Setup(item => item.Choose(packPath))
                .Returns(ExternalPackOpenChoice.ImportAsFolderProject);
            var workflow = new ExternalPackOpenWorkflow(
                packFiles.Object,
                choices.Object,
                commandFactory.Object,
                Mock.Of<IStandardDialogs>(),
                LoadLocalization());

            var result = await workflow.OpenAsync(
                packPath,
                CancellationToken.None);

            NUnit.Framework.Assert.That(
                result.Status,
                Is.EqualTo(ExternalPackOpenStatus.Failed));
            packFiles.Verify(
                item => item.GetAllPackfileContainers(),
                Times.Once);
            packFiles.VerifyNoOtherCalls();
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
                $"ae-external-pack-{Guid.NewGuid():N}");

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
}
