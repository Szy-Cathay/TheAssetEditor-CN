using CommonControls.Editors.AnimationPack;
using Editors.AnimationFragmentEditor.AnimationPack.ViewModels;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Ui.Editors.TextEditor;
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
        public void LoadFromBinary_LeavesTableClean()
        {
            var editor = CreateTableEditor();
            var bin = CreateAnimationBin();

            editor.LoadFromBinary(bin.ToByteArray(), bin.FileName);

            Assert.IsFalse(editor.IsDirty);
            Assert.AreEqual(bin.Name, editor.Name);
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
                new MetaDataFileParser(new Mock<IMetaDataDatabase>().Object));
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
    }
}
