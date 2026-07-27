using System.Windows;
using AssetEditor.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;

namespace AssetEditorTests
{
    [NUnit.Framework.TestFixture]
    public class EditorManagerTests
    {
        [NUnit.Framework.Test]
        public void BeforePackFileContainerRemoved_WhenCloseIsCancelled_KeepsEditorOpen()
        {
            _ = new LocalizationManager();
            Action<BeforePackFileContainerRemovedEvent>? beforeRemove = null;
            var eventHub = new Mock<IGlobalEventHub>();
            eventHub
                .Setup(hub => hub.Register(
                    It.IsAny<object>(),
                    It.IsAny<Action<BeforePackFileContainerRemovedEvent>>()))
                .Callback<object, Action<BeforePackFileContainerRemovedEvent>>(
                    (_, callback) => beforeRemove = callback);
            var packFileService = new Mock<IPackFileService>();
            var editorDatabase = new Mock<IEditorDatabase>();
            var container = new PackFileContainer("test.pack");
            var file = PackFile.CreateFromBytes("test.bin", [1]);
            var editor = new Mock<IEditorInterface>();
            editor.SetupProperty(item => item.DisplayName, "Test editor");
            editor.As<IFileEditor>().SetupGet(item => item.CurrentFile).Returns(file);
            packFileService
                .Setup(service => service.GetPackFileContainer(file))
                .Returns(container);
            var prompt = (Message: string.Empty, Caption: string.Empty, Buttons: default(MessageBoxButton));
            var manager = new EditorManager(
                eventHub.Object,
                packFileService.Object,
                editorDatabase.Object,
                (message, caption, buttons) =>
                {
                    prompt = (message, caption, buttons);
                    return MessageBoxResult.No;
                });
            manager.CurrentEditorsList.Add(editor.Object);
            var args = new BeforePackFileContainerRemovedEvent(container);

            beforeRemove!(args);

            NUnit.Framework.Assert.That(prompt.Buttons, NUnit.Framework.Is.EqualTo(MessageBoxButton.YesNo));
            NUnit.Framework.Assert.That(args.AllowClose, NUnit.Framework.Is.False);
            NUnit.Framework.Assert.That(manager.CurrentEditorsList, NUnit.Framework.Does.Contain(editor.Object));
            editor.Verify(item => item.Close(), Times.Never);
            editorDatabase.Verify(
                database => database.DestroyEditor(editor.Object),
                Times.Never);
        }

        [NUnit.Framework.Test]
        public void CloseAllTools_ClosesAndDestroysEveryEditorOnce()
        {
            var eventHub = new Mock<IGlobalEventHub>();
            var packFileService = new Mock<IPackFileService>();
            var editorDatabase = new Mock<IEditorDatabase>();
            var firstEditor = new Mock<IEditorInterface>();
            var secondEditor = new Mock<IEditorInterface>();
            var manager = new EditorManager(
                eventHub.Object,
                packFileService.Object,
                editorDatabase.Object,
                (_, _, _) => MessageBoxResult.Yes);
            manager.CurrentEditorsList.Add(firstEditor.Object);
            manager.CurrentEditorsList.Add(secondEditor.Object);

            NUnit.Framework.Assert.DoesNotThrow(() => manager.CloseAllTools(firstEditor.Object));

            NUnit.Framework.Assert.That(manager.CurrentEditorsList, NUnit.Framework.Is.Empty);
            firstEditor.Verify(editor => editor.Close(), Times.Once);
            secondEditor.Verify(editor => editor.Close(), Times.Once);
            editorDatabase.Verify(database => database.DestroyEditor(firstEditor.Object), Times.Once);
            editorDatabase.Verify(database => database.DestroyEditor(secondEditor.Object), Times.Once);
        }
    }
}
