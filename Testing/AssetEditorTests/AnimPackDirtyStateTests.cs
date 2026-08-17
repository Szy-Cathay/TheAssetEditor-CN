using CommonControls.Editors.AnimationPack;
using AssetEditor.Services;
using AssetEditor.ViewModels;
using Editors.AnimationFragmentEditor.AnimationPack.ViewModels;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Ui.Editors.TextEditor;
using System.Threading;
using System.Windows;
using System.Xml.Linq;

namespace AssetEditorTests
{
    [TestClass]
    public class AnimPackDirtyStateTests
    {
        [TestMethod]
        public void HeaderEdit_MarksTableDirty()
        {
            var editor = CreateTableEditor();

            editor.Name = "changed";

            Assert.IsTrue(editor.IsDirty);
        }

        [TestMethod]
        public void RowCollectionEdit_MarksTableDirty()
        {
            var editor = CreateTableEditor();

            editor.Rows.Add(new AnimationEntryRowViewModel());

            Assert.IsTrue(editor.IsDirty);
        }

        [TestMethod]
        public void RowPropertyEdit_MarksTableDirty()
        {
            var editor = CreateTableEditor();
            var row = new AnimationEntryRowViewModel();
            editor.Rows.Add(row);
            editor.IsDirty = false;

            row.AnimationFile = "animations/test.anim";

            Assert.IsTrue(editor.IsDirty);
        }

        [TestMethod]
        public void RowFileNames_HideDirectoriesWithoutChangingStoredPaths()
        {
            var row = new AnimationEntryRowViewModel
            {
                AnimationFile = "animations\\battle\\run.anim",
                MetaFile = "animations/battle/run.meta",
                SoundFile = "audio\\battle\\run.sound",
            };

            Assert.AreEqual("run.anim", row.AnimationFileName);
            Assert.AreEqual("run.meta", row.MetaFileName);
            Assert.AreEqual("run.sound", row.SoundFileName);
            Assert.AreEqual("animations\\battle\\run.anim", row.AnimationFile);
            Assert.AreEqual("animations/battle/run.meta", row.MetaFile);
            Assert.AreEqual("audio\\battle\\run.sound", row.SoundFile);
        }

        [TestMethod]
        public void LoadFromBinary_LeavesTableClean()
        {
            var editor = CreateTableEditor();
            var bin = CreateAnimationBin();

            editor.LoadFromBinary(bin.ToByteArray(), bin.FileName);

            Assert.IsFalse(editor.IsDirty);
            Assert.AreEqual(bin.Name, editor.Name);
        }

        [TestMethod]
        public void Undo_AfterFiftyOneAdds_RetainsTheMostRecentFiftyActions()
        {
            var editor = CreateTableEditor();

            for (var index = 0; index < 51; index++)
                editor.AddEntryCommand.Execute(null);

            for (var index = 0; index < 50; index++)
                editor.UndoCommand.Execute(null);

            Assert.AreEqual(1, editor.Rows.Count);
            Assert.IsFalse(editor.UndoCommand.CanExecute(null));
        }

        [TestMethod]
        public void Undo_AfterFirstAdd_RestoresTheCleanLoadedState()
        {
            var editor = CreateTableEditor();

            editor.AddEntryCommand.Execute(null);
            editor.UndoCommand.Execute(null);

            Assert.AreEqual(0, editor.Rows.Count);
            Assert.IsFalse(editor.IsDirty);
        }

        [TestMethod]
        public void RowCommands_ReflectTheCurrentSelectionAndPosition()
        {
            var editor = CreateTableEditor();

            Assert.IsFalse(editor.DeleteEntriesCommand.CanExecute(null));
            Assert.IsFalse(editor.DuplicateEntryCommand.CanExecute(null));
            Assert.IsFalse(editor.MoveUpCommand.CanExecute(null));
            Assert.IsFalse(editor.MoveDownCommand.CanExecute(null));
            Assert.IsFalse(editor.CopyRowsCommand.CanExecute(null));

            editor.AddEntryCommand.Execute(null);
            editor.MultiSelectedRows = new[] { editor.SelectedRow! };

            Assert.IsTrue(editor.DeleteEntriesCommand.CanExecute(null));
            Assert.IsTrue(editor.DuplicateEntryCommand.CanExecute(null));
            Assert.IsFalse(editor.MoveUpCommand.CanExecute(null));
            Assert.IsFalse(editor.MoveDownCommand.CanExecute(null));
            Assert.IsTrue(editor.CopyRowsCommand.CanExecute(null));

            editor.AddEntryCommand.Execute(null);
            editor.MultiSelectedRows = new[] { editor.SelectedRow! };

            Assert.IsTrue(editor.MoveUpCommand.CanExecute(null));
            Assert.IsFalse(editor.MoveDownCommand.CanExecute(null));
        }

        [TestMethod]
        public void HasUnsavedChanges_IncludesDirtyTable()
        {
            var editor = CreateOuterEditor();
            editor.TableEditorVM = CreateTableEditor();
            editor.TableEditorVM.IsDirty = true;

            Assert.IsTrue(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void HasUnsavedChanges_IncludesDirtyXml()
        {
            var editor = CreateOuterEditor();
            editor.SelectedItemViewModel = new SimpleTextEditorViewModel();
            editor.SelectedItemViewModel.Text = "<changed/>";

            Assert.IsTrue(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Save_CommitsDirtyChildBeforeSavingParent()
        {
            var harness = CreateLoadedOuterEditor(parentSaveSucceeds: true);
            harness.Editor.TableEditorVM.MountBin = "changed_mount";

            var result = harness.Editor.Save();

            Assert.IsTrue(result);
            Assert.IsFalse(harness.Editor.TableEditorVM.IsDirty);
            Assert.IsFalse(harness.Editor.HasUnsavedChanges);

            var savedPack = PackFile.CreateFromBytes("saved.animpack", harness.SavedBytes!);
            var savedDatabase = AnimationPackSerializer.Load(savedPack, harness.PackFileService.Object);
            var savedBin = (AnimationBinWh3)savedDatabase.Files.Single();
            Assert.AreEqual("changed_mount", savedBin.MountBin);
        }

        [TestMethod]
        public async Task Save_FolderProject_UpdatesDiskAndHistoryWorkspace()
        {
            using var project = new TemporaryFolderProject();
            var eventHub = new TestEventHub();
            var packFileService = new PackFileService(eventHub)
            {
                EnforceGameFilesMustBeLoaded = false,
                MessageBoxProvider = Mock.Of<ISimpleMessageBox>(),
            };
            using var container = FolderProjectContainer.Create(
                project.Path,
                new FolderProjectSettings { Name = "动画包历史测试" });
            packFileService.AddContainer(container);
            packFileService.SetEditablePack(container);

            const string animPackPath =
                "animations\\database\\battle\\bin\\test.animpack";
            var database = new AnimationPackFileDatabase(animPackPath);
            database.AddFile(CreateAnimationBin());
            packFileService.AddFilesToPack(
                container,
                [
                    new NewPackFileEntry(
                        Path.GetDirectoryName(animPackPath)!,
                        PackFile.CreateFromBytes(
                            Path.GetFileName(animPackPath),
                            AnimationPackSerializer.ConvertToBytes(database))),
                    new NewPackFileEntry(
                        "",
                        PackFile.CreateFromBytes("graph.xml", [1])),
                ]);
            var parentPack = packFileService.FindFile(
                animPackPath,
                container)!;
            var diskPath = Path.Combine(project.Path, animPackPath);
            var originalBytes = File.ReadAllBytes(diskPath);

            var localization = new LocalizationManager();
            localization.LoadLanguage();
            var historyService = new FolderProjectHistoryService(localization);
            historyService.Initialize(project.Path);
            var history = new FolderProjectHistoryViewModel(
                historyService,
                Mock.Of<IFolderProjectUnsavedChangesService>(),
                Mock.Of<IFolderProjectUnsavedChangesPrompt>(),
                Mock.Of<IFolderProjectGitOperationCoordinator>(),
                Mock.Of<IStandardDialogs>(),
                localization);
            var historyWorkspace = new FolderProjectHistoryWorkspaceViewModel(
                history,
                eventHub);
            historyWorkspace.SetEditableContainer(container);
            historyWorkspace.ShowHistory();
            await WaitForAsync(() => history.IsReady && !history.IsBusy);

            FolderProjectChangedEvent? savedChange = null;
            var changeOwner = new object();
            eventHub.Register<FolderProjectChangedEvent>(
                changeOwner,
                change => savedChange = change);
            var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
            skeletonLookup
                .Setup(x => x.GetSkeletonFileFromName("skeleton"))
                .Returns(new AnimationFile());
            var editor = new AnimPackViewModel(
                Mock.Of<IUiCommandFactory>(),
                packFileService,
                skeletonLookup.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                new FileSaveService(
                    packFileService,
                    Mock.Of<IStandardDialogs>()),
                new MetaDataFileParser(Mock.Of<IMetaDataDatabase>()),
                Mock.Of<IStandardDialogs>());
            editor.LoadFile(parentPack);
            editor.AnimationPackItems.SelectedItem =
                editor.AnimationPackItems.PossibleValues.Single();
            editor.TableEditorVM.MountBin = "changed_mount";

            var result = editor.Save();
            await WaitForAsync(() =>
                !history.IsBusy &&
                history.UnrecordedChanges.Any(change =>
                    change.Path.Equals(
                        animPackPath.Replace('\\', '/'),
                        StringComparison.OrdinalIgnoreCase)));

            Assert.IsTrue(result);
            CollectionAssert.AreNotEqual(
                originalBytes,
                File.ReadAllBytes(diskPath));
            Assert.IsNotNull(savedChange);
            Assert.AreEqual(
                animPackPath,
                savedChange.ChangeSet.FileChanges.Single().Path);
            Assert.IsTrue(history.HasUnrecordedChanges);
        }

        [TestMethod]
        public void Save_TableDirtyWhileXmlViewActive_CommitsTable()
        {
            var harness = CreateLoadedOuterEditor(parentSaveSucceeds: true);
            harness.Editor.TableEditorVM.MountBin = "changed_mount";
            harness.Editor.IsTableView = false;

            var result = harness.Editor.Save();

            Assert.IsTrue(result);
            Assert.IsFalse(harness.Editor.TableEditorVM.IsDirty);
            Assert.IsFalse(harness.Editor.HasUnsavedChanges);

            var savedPack = PackFile.CreateFromBytes("saved.animpack", harness.SavedBytes!);
            var savedDatabase = AnimationPackSerializer.Load(savedPack, harness.PackFileService.Object);
            var savedBin = (AnimationBinWh3)savedDatabase.Files.Single();
            Assert.AreEqual("changed_mount", savedBin.MountBin);
        }

        [TestMethod]
        public void SaveActiveFile_TableDirty_SynchronizesXmlSnapshot()
        {
            var harness = CreateLoadedOuterEditor(parentSaveSucceeds: true);
            harness.Editor.TableEditorVM.MountBin = "changed_mount";

            var result = harness.Editor.SaveActiveFile();

            Assert.IsTrue(result);
            Assert.AreEqual("changed_mount", GetXmlMountBin(harness.Editor));
            Assert.IsFalse(harness.Editor.TableEditorVM.IsDirty);
            Assert.IsFalse(harness.Editor.SelectedItemViewModel.HasUnsavedChanges());
            Assert.IsTrue(harness.Editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void ToggleViewMode_AppliesCurrentTableEditsBeforeShowingXml()
        {
            var harness = CreateLoadedOuterEditor(parentSaveSucceeds: true);
            harness.Editor.TableEditorVM.MountBin = "changed_mount";

            harness.Editor.ToggleViewModeCommand.Execute(null);

            Assert.IsFalse(harness.Editor.IsTableView);
            Assert.IsFalse(harness.Editor.TableEditorVM.IsDirty);
            Assert.AreEqual("changed_mount", GetXmlMountBin(harness.Editor));
            Assert.IsTrue(harness.Editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void SaveActiveFile_XmlDirty_SynchronizesTableSnapshot()
        {
            var harness = CreateLoadedOuterEditor(parentSaveSucceeds: true);
            SetXmlMountBin(harness.Editor, "changed_mount");

            var result = harness.Editor.SaveActiveFile();

            Assert.IsTrue(result);
            Assert.AreEqual("changed_mount", harness.Editor.TableEditorVM.MountBin);
            Assert.IsFalse(harness.Editor.TableEditorVM.IsDirty);
            Assert.IsFalse(harness.Editor.SelectedItemViewModel.HasUnsavedChanges());
            Assert.IsTrue(harness.Editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void SaveActiveFile_WhenBothChildrenDirty_DoesNotModifyEitherRepresentation()
        {
            var harness = CreateLoadedOuterEditor(parentSaveSucceeds: true);
            var selectedFile = harness.Editor.AnimationPackItems.SelectedItem!;
            var originalBytes = selectedFile.ToByteArray().ToArray();
            harness.Editor.TableEditorVM.MountBin = "table_mount";
            SetXmlMountBin(harness.Editor, "xml_mount");

            var result = harness.Editor.SaveActiveFile();

            Assert.IsFalse(result);
            Assert.IsTrue(harness.Editor.TableEditorVM.IsDirty);
            Assert.IsTrue(harness.Editor.SelectedItemViewModel.HasUnsavedChanges());
            CollectionAssert.AreEqual(originalBytes, selectedFile.ToByteArray());
        }

        [TestMethod]
        public void EditingTableAndXml_ExposesAVisibleConflictState()
        {
            var harness = CreateLoadedOuterEditor(parentSaveSucceeds: true);

            harness.Editor.TableEditorVM.MountBin = "table_mount";
            SetXmlMountBin(harness.Editor, "xml_mount");

            Assert.IsTrue(harness.Editor.HasEditConflict);
            Assert.IsTrue(harness.Editor.HasEditStatus);
            Assert.IsFalse(string.IsNullOrWhiteSpace(harness.Editor.EditStatusMessage));
        }

        [TestMethod]
        public void Save_WhenBothChildrenDirty_DoesNotWriteParentAndKeepsUncommittedChildDirty()
        {
            var harness = CreateLoadedOuterEditor(parentSaveSucceeds: true);
            harness.Editor.TableEditorVM.MountBin = "changed_mount";
            harness.Editor.SelectedItemViewModel.Text += Environment.NewLine;
            harness.Editor.IsTableView = true;
            var selectedFile = harness.Editor.AnimationPackItems.SelectedItem!;
            var originalBytes = selectedFile.ToByteArray().ToArray();

            var result = harness.Editor.Save();

            Assert.IsFalse(result);
            Assert.IsTrue(harness.Editor.TableEditorVM.IsDirty);
            Assert.IsTrue(harness.Editor.SelectedItemViewModel.HasUnsavedChanges());
            Assert.IsTrue(harness.Editor.HasUnsavedChanges);
            CollectionAssert.AreEqual(originalBytes, selectedFile.ToByteArray());
            harness.FileSaveService.Verify(
                x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestMethod]
        public void Save_WhenParentSaveFails_KeepsEditorDirty()
        {
            var harness = CreateLoadedOuterEditor(parentSaveSucceeds: false);
            harness.Editor.TableEditorVM.MountBin = "changed_mount";

            var result = harness.Editor.Save();

            Assert.IsFalse(result);
            Assert.IsFalse(harness.Editor.TableEditorVM.IsDirty);
            Assert.IsTrue(harness.Editor.HasUnsavedChanges);
        }

        private static string GetXmlMountBin(AnimPackViewModel editor)
        {
            var document = XDocument.Parse(editor.SelectedItemViewModel.Text);
            var mountBin = document.Root?
                .Element("GeneralBinData")?
                .Element("MountBin");
            Assert.IsNotNull(mountBin);
            return mountBin.Value;
        }

        private static void SetXmlMountBin(AnimPackViewModel editor, string value)
        {
            var document = XDocument.Parse(editor.SelectedItemViewModel.Text);
            var mountBin = document.Root?
                .Element("GeneralBinData")?
                .Element("MountBin");
            Assert.IsNotNull(mountBin);
            mountBin.Value = value;
            editor.SelectedItemViewModel.Text = document.ToString();
        }

        private static AnimSetTableEditorViewModel CreateTableEditor(
            Mock<IPackFileService>? packFileService = null,
            Mock<ISkeletonAnimationLookUpHelper>? skeletonLookup = null,
            PackFile? animPack = null)
        {
            packFileService ??= CreatePackFileService();
            skeletonLookup ??= new Mock<ISkeletonAnimationLookUpHelper>();
            var metadataParser = new MetaDataFileParser(new Mock<IMetaDataDatabase>().Object);

            return new AnimSetTableEditorViewModel(
                packFileService.Object,
                skeletonLookup.Object,
                metadataParser,
                animPack!,
                GameTypeEnum.Warhammer3);
        }

        private static AnimPackViewModel CreateOuterEditor(
            Mock<IPackFileService>? packFileService = null,
            Mock<ISkeletonAnimationLookUpHelper>? skeletonLookup = null,
            Mock<IFileSaveService>? fileSaveService = null)
        {
            packFileService ??= CreatePackFileService();
            skeletonLookup ??= new Mock<ISkeletonAnimationLookUpHelper>();
            fileSaveService ??= new Mock<IFileSaveService>();

            return new AnimPackViewModel(
                new Mock<IUiCommandFactory>().Object,
                packFileService.Object,
                skeletonLookup.Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                fileSaveService.Object,
                new MetaDataFileParser(new Mock<IMetaDataDatabase>().Object),
                Mock.Of<IStandardDialogs>());
        }

        private static LoadedEditorHarness CreateLoadedOuterEditor(bool parentSaveSucceeds)
        {
            var packFileService = CreatePackFileService();
            var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
            skeletonLookup
                .Setup(x => x.GetSkeletonFileFromName("skeleton"))
                .Returns(new AnimationFile());

            var database = new AnimationPackFileDatabase("test.animpack");
            database.AddFile(CreateAnimationBin());
            var parentPack = PackFile.CreateFromBytes(
                "test.animpack",
                AnimationPackSerializer.ConvertToBytes(database));

            byte[]? savedBytes = null;
            var fileSaveService = new Mock<IFileSaveService>();
            fileSaveService
                .Setup(x => x.Save("test.animpack", It.IsAny<byte[]>(), false))
                .Callback<string, byte[], bool>((_, bytes, _) => savedBytes = bytes)
                .Returns(parentSaveSucceeds ? parentPack : null);

            var editor = CreateOuterEditor(packFileService, skeletonLookup, fileSaveService);
            editor.LoadFile(parentPack);
            editor.AnimationPackItems.SelectedItem = editor.AnimationPackItems.PossibleValues.Single();

            return new LoadedEditorHarness(editor, packFileService, fileSaveService, () => savedBytes);
        }

        private static Mock<IPackFileService> CreatePackFileService()
        {
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns(new List<PackFileContainer>());
            packFileService
                .Setup(x => x.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>()))
                .Returns("test.animpack");
            packFileService
                .Setup(x => x.FindFile("graph.xml", It.IsAny<PackFileContainer?>()))
                .Returns(PackFile.CreateFromBytes("graph.xml", [1]));
            return packFileService;
        }

        private static AnimationBinWh3 CreateAnimationBin()
        {
            return new AnimationBinWh3("animations/database/battle/bin/test_bin.bin")
            {
                Name = "test_bin",
                SkeletonName = "skeleton",
                LocomotionGraph = "graph.xml",
                TableVersion = 4,
                TableSubVersion = 3,
            };
        }

        private sealed class LoadedEditorHarness
        {
            private readonly Func<byte[]?> _getSavedBytes;

            public LoadedEditorHarness(
                AnimPackViewModel editor,
                Mock<IPackFileService> packFileService,
                Mock<IFileSaveService> fileSaveService,
                Func<byte[]?> getSavedBytes)
            {
                Editor = editor;
                PackFileService = packFileService;
                FileSaveService = fileSaveService;
                _getSavedBytes = getSavedBytes;
            }

            public AnimPackViewModel Editor { get; }
            public Mock<IPackFileService> PackFileService { get; }
            public Mock<IFileSaveService> FileSaveService { get; }
            public byte[]? SavedBytes => _getSavedBytes();
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition() && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.IsTrue(condition(), "等待工程历史刷新超时。");
        }

        private sealed class TemporaryFolderProject : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ae-animpack-history-{Guid.NewGuid():N}");

            public TemporaryFolderProject() => Directory.CreateDirectory(Path);

            public void Dispose()
            {
                if (!Directory.Exists(Path))
                    return;
                foreach (var file in Directory.EnumerateFiles(
                             Path,
                             "*",
                             SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(Path, true);
            }
        }
    }

    public class AnimPackClipboardTests
    {
        [NUnit.Framework.Test]
        [NUnit.Framework.Apartment(ApartmentState.STA)]
        public void PasteRows_MalformedEditorClipboard_ShowsFeedbackWithoutChangingRows()
        {
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns(new List<PackFileContainer>());
            var editor = new AnimSetTableEditorViewModel(
                packFileService.Object,
                Mock.Of<ISkeletonAnimationLookUpHelper>(),
                new MetaDataFileParser(Mock.Of<IMetaDataDatabase>()),
                null!,
                GameTypeEnum.Warhammer3);

            try
            {
                Clipboard.SetText("AE_ANIM_ROWS|{");
                editor.RefreshCommandStates();

                Assert.IsTrue(editor.PasteRowsCommand.CanExecute(null));
                editor.PasteRowsCommand.Execute(null);

                Assert.AreEqual(0, editor.Rows.Count);
                Assert.IsTrue(editor.HasFeedback);
                Assert.IsFalse(string.IsNullOrWhiteSpace(editor.FeedbackMessage));
            }
            finally
            {
                Clipboard.Clear();
            }
        }
    }
}
