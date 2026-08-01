using System.Text;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.Ui.Editors.TextEditor;

namespace Test.Editors.Shared.Core;

public class TextEditorUnsavedChangesTests
{
    [Test]
    public void EditAndSave_TracksUnsavedMemoryState()
    {
        var file = PackFile.CreateFromBytes(
            "test.txt",
            Encoding.ASCII.GetBytes("disk"));
        var fileSaveService = new Mock<IFileSaveService>();
        fileSaveService.Setup(service => service.Save(
                "test.txt",
                It.IsAny<byte[]>(),
                false))
            .Returns(file);
        var packFileService = new Mock<IPackFileService>();
        packFileService.Setup(service => service.GetFullPath(file))
            .Returns("test.txt");
        var editor = new TextEditorViewModel<DefaultTextConverter>(
            fileSaveService.Object,
            packFileService.Object,
            new DefaultTextConverter());

        editor.LoadFile(file);
        var initiallyUnsaved =
            ((ISaveableEditor)editor).HasUnsavedChanges;
        editor.Text = "memory";
        var changed = ((ISaveableEditor)editor).HasUnsavedChanges;
        var saved = ((ISaveableEditor)editor).Save();

        Assert.Multiple(() =>
        {
            Assert.That(initiallyUnsaved, Is.False);
            Assert.That(changed, Is.True);
            Assert.That(saved, Is.True);
            Assert.That(
                ((ISaveableEditor)editor).HasUnsavedChanges,
                Is.False);
        });
        fileSaveService.Verify(service => service.Save(
            "test.txt",
            It.Is<byte[]>(data =>
                Encoding.ASCII.GetString(data) == "memory"),
            false));
    }
}
