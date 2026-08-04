using CommonControls.Editors.AnimationPack;
using Editors.AnimationFragmentEditor.AnimationPack.Commands;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;

namespace AssetEditorTests
{
    [TestClass]
    public class AnimPackStructureCommandTests
    {
        [TestMethod]
        public void Create_Success_AddsFileAndMarksDirty()
        {
            var editor = CreateEditor();
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowTextInputDialog("Fragment name", ""))
                .Returns(new TextInputDialogResult(true, "new.frg"));
            var command = new CreateEmptyWarhammer3AnimSetFileCommand(dialogs.Object);

            command.Execute(editor);

            Assert.AreEqual(1, editor.AnimationPackItems.PossibleValues.Count);
            Assert.IsTrue(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Create_Cancel_DoesNotAddFileOrMarkDirty()
        {
            var editor = CreateEditor();
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowTextInputDialog("Fragment name", ""))
                .Returns(new TextInputDialogResult(false, ""));
            var command = new CreateEmptyWarhammer3AnimSetFileCommand(dialogs.Object);

            command.Execute(editor);

            Assert.AreEqual(0, editor.AnimationPackItems.PossibleValues.Count);
            Assert.IsFalse(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Rename_Success_ChangesFileNameAndMarksDirty()
        {
            var editor = CreateEditor();
            var file = AddAndSelectFile(editor);
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowTextInputDialog("Rename Anim File", "original.bin"))
                .Returns(new TextInputDialogResult(true, "renamed.bin"));
            var command = new RenameSelectedFileCommand(dialogs.Object);

            command.Execute(editor);

            Assert.AreEqual("renamed.bin", file.FileName);
            Assert.IsTrue(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Rename_Cancel_DoesNotChangeFileNameOrMarkDirty()
        {
            var editor = CreateEditor();
            var file = AddAndSelectFile(editor);
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowTextInputDialog("Rename Anim File", "original.bin"))
                .Returns(new TextInputDialogResult(false, ""));
            var command = new RenameSelectedFileCommand(dialogs.Object);

            command.Execute(editor);

            Assert.AreEqual("original.bin", file.FileName);
            Assert.IsFalse(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Rename_NoSelection_DoesNotMarkDirty()
        {
            var editor = CreateEditor();
            var command = new RenameSelectedFileCommand(Mock.Of<IStandardDialogs>());

            command.Execute(editor);

            Assert.IsFalse(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Remove_Success_RemovesFileAndMarksDirty()
        {
            var editor = CreateEditor();
            AddAndSelectFile(editor);
            var command = new RemoveSelectedFileCommand();

            command.Execute(editor);

            Assert.AreEqual(0, editor.AnimationPackItems.PossibleValues.Count);
            Assert.IsTrue(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Remove_NoSelection_DoesNotMarkDirty()
        {
            var editor = CreateEditor();
            var command = new RemoveSelectedFileCommand();

            command.Execute(editor);

            Assert.IsFalse(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Remove_SelectedFileNotInList_DoesNotMarkDirty()
        {
            var editor = CreateEditor();
            editor.AnimationPackItems.SelectedItem = new UnknownAnimFile("missing.bin", [1]);
            var command = new RemoveSelectedFileCommand();

            command.Execute(editor);

            Assert.IsFalse(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void CreateAnimationDbWarhammer3_Success_SavesGeneratedAnimPack()
        {
            const string filePath = @"animations/database/battle/bin/new.animpack";
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowTextInputDialog("New AnimPack name", ""))
                .Returns(new TextInputDialogResult(true, "new"));
            var editablePack = new PackFileContainer("output.pack");
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(x => x.GetEditablePack()).Returns(editablePack);
            packFileService.Setup(x => x.FindFile(filePath, editablePack)).Returns((PackFile?)null);
            var savedFile = new PackFile("new.animpack", new MemorySource([]));
            var saveService = new Mock<IFileSaveService>();
            saveService.Setup(x => x.Save(filePath, It.IsAny<byte[]>(), false)).Returns(savedFile);
            var command = new CreateExampleAnimationDbCommand(saveService.Object, packFileService.Object, dialogs.Object);

            var result = command.CreateAnimationDbWarhammer3();

            Assert.AreSame(savedFile, result);
            saveService.Verify(x => x.Save(filePath, It.IsAny<byte[]>(), false), Times.Once);
        }

        [TestMethod]
        public void CreateAnimationDbWarhammer3_Cancel_DoesNotSave()
        {
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowTextInputDialog("New AnimPack name", ""))
                .Returns(new TextInputDialogResult(false, ""));
            var saveService = new Mock<IFileSaveService>();
            var command = new CreateExampleAnimationDbCommand(
                saveService.Object,
                Mock.Of<IPackFileService>(),
                dialogs.Object);

            var result = command.CreateAnimationDbWarhammer3();

            Assert.IsNull(result);
            saveService.Verify(x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()), Times.Never);
        }

        [TestMethod]
        public void CreateAnimationDbWarhammer3_DuplicateName_ShowsStandardDialogAndDoesNotSave()
        {
            const string filePath = @"animations/database/battle/bin/existing.animpack";
            _ = new LocalizationManager();
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowTextInputDialog("New AnimPack name", ""))
                .Returns(new TextInputDialogResult(true, "existing"));
            var editablePack = new PackFileContainer("output.pack");
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(x => x.GetEditablePack()).Returns(editablePack);
            packFileService.Setup(x => x.FindFile(filePath, editablePack))
                .Returns(new PackFile("existing.animpack", new MemorySource([])));
            var saveService = new Mock<IFileSaveService>();
            var command = new CreateExampleAnimationDbCommand(saveService.Object, packFileService.Object, dialogs.Object);

            var result = command.CreateAnimationDbWarhammer3();

            Assert.IsNull(result);
            dialogs.Verify(x => x.ShowDialogBox(It.IsAny<string>(), "Error"), Times.Once);
            saveService.Verify(x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()), Times.Never);
        }

        [TestMethod]
        public void CreateAnimationDb3k_Success_UsesStandardDialogAndSaves()
        {
            const string filePath = @"animations/database/battle/bin/new.animpack";
            var dialogs = new Mock<IStandardDialogs>();
            dialogs.Setup(x => x.ShowTextInputDialog("New AnimPack name", ""))
                .Returns(new TextInputDialogResult(true, "new"));
            var editablePack = new PackFileContainer("output.pack");
            var packFileService = new Mock<IPackFileService>();
            packFileService.Setup(x => x.GetEditablePack()).Returns(editablePack);
            packFileService.Setup(x => x.FindFile(filePath, editablePack)).Returns((PackFile?)null);
            var saveService = new Mock<IFileSaveService>();
            var command = new CreateExampleAnimationDbCommand(saveService.Object, packFileService.Object, dialogs.Object);

            command.CreateAnimationDb3k();

            saveService.Verify(x => x.Save(filePath, It.IsAny<byte[]>(), false), Times.Once);
        }

        private static AnimPackViewModel CreateEditor()
        {
            return new AnimPackViewModel(
                new Mock<IUiCommandFactory>().Object,
                new Mock<IPackFileService>().Object,
                new Mock<ISkeletonAnimationLookUpHelper>().Object,
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                new Mock<IFileSaveService>().Object,
                new MetaDataFileParser(new Mock<IMetaDataDatabase>().Object));
        }

        private static UnknownAnimFile AddAndSelectFile(AnimPackViewModel editor)
        {
            var file = new UnknownAnimFile("original.bin", [1]);
            editor.AnimationPackItems.PossibleValues.Add(file);
            editor.AnimationPackItems.SelectedItem = file;
            return file;
        }

    }
}
