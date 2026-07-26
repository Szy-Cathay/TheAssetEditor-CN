using CommonControls.Editors.AnimationPack;
using Editors.AnimationFragmentEditor.AnimationPack.Commands;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
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
            var command = new StubCreateCommand("new.frg");

            command.Execute(editor);

            Assert.AreEqual(1, editor.AnimationPackItems.PossibleValues.Count);
            Assert.IsTrue(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Create_Cancel_DoesNotAddFileOrMarkDirty()
        {
            var editor = CreateEditor();
            var command = new StubCreateCommand(null);

            command.Execute(editor);

            Assert.AreEqual(0, editor.AnimationPackItems.PossibleValues.Count);
            Assert.IsFalse(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Rename_Success_ChangesFileNameAndMarksDirty()
        {
            var editor = CreateEditor();
            var file = AddAndSelectFile(editor);
            var command = new StubRenameCommand("renamed.bin");

            command.Execute(editor);

            Assert.AreEqual("renamed.bin", file.FileName);
            Assert.IsTrue(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Rename_Cancel_DoesNotChangeFileNameOrMarkDirty()
        {
            var editor = CreateEditor();
            var file = AddAndSelectFile(editor);
            var command = new StubRenameCommand(null);

            command.Execute(editor);

            Assert.AreEqual("original.bin", file.FileName);
            Assert.IsFalse(editor.HasUnsavedChanges);
        }

        [TestMethod]
        public void Rename_NoSelection_DoesNotMarkDirty()
        {
            var editor = CreateEditor();
            var command = new StubRenameCommand("renamed.bin");

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

        private sealed class StubCreateCommand : CreateEmptyWarhammer3AnimSetFileCommand
        {
            private readonly string? _fileName;

            public StubCreateCommand(string? fileName)
            {
                _fileName = fileName;
            }

            protected override string? GetAnimSetFileName() => _fileName;
        }

        private sealed class StubRenameCommand : RenameSelectedFileCommand
        {
            private readonly string? _fileName;

            public StubRenameCommand(string? fileName)
            {
                _fileName = fileName;
            }

            protected override string? GetNewFileName(string currentFileName) => _fileName;
        }
    }
}
