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
        public void BeforePackFileContainerRemoved_DefersClosingUntilUnloadIsApproved()
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
            var promptCount = 0;
            var manager = new EditorManager(
                eventHub.Object,
                packFileService.Object,
                editorDatabase.Object,
                (message, caption, buttons) =>
                {
                    promptCount++;
                    return MessageBoxResult.No;
                });
            manager.CurrentEditorsList.Add(editor.Object);
            var args = new BeforePackFileContainerRemovedEvent(container);

            beforeRemove!(args);

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(args.AllowClose, NUnit.Framework.Is.True);
                NUnit.Framework.Assert.That(
                    args.HasApprovedCloseAction,
                    NUnit.Framework.Is.True);
                NUnit.Framework.Assert.That(promptCount, NUnit.Framework.Is.Zero);
                NUnit.Framework.Assert.That(
                    manager.CurrentEditorsList,
                    NUnit.Framework.Does.Contain(editor.Object));
            });
            editor.Verify(item => item.Close(), Times.Never);
            editorDatabase.Verify(
                database => database.DestroyEditor(editor.Object),
                Times.Never);
        }

        [NUnit.Framework.Test]
        public void TryCloseEditorsForContainer_WithNoEditors_ReturnsTrueWithoutPrompt()
        {
            var promptCount = 0;
            var manager = new EditorManager(
                Mock.Of<IGlobalEventHub>(),
                Mock.Of<IPackFileService>(),
                Mock.Of<IEditorDatabase>(),
                (_, _, _) =>
                {
                    promptCount++;
                    return MessageBoxResult.No;
                });

            var result = manager.TryCloseEditorsForContainer(
                new PackFileContainer("target"));

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(result, NUnit.Framework.Is.True);
                NUnit.Framework.Assert.That(promptCount, NUnit.Framework.Is.Zero);
            });
        }

        [NUnit.Framework.TestCase(MessageBoxResult.Yes, true)]
        [NUnit.Framework.TestCase(MessageBoxResult.No, false)]
        public void TryCloseEditorsForContainer_OrdinaryEditor_UsesGeneralConfirmation(
            MessageBoxResult promptResult,
            bool expectedResult)
        {
            _ = new LocalizationManager();
            var container = new PackFileContainer("target");
            var file = PackFile.CreateFromBytes("target.bin", [1]);
            var editor = CreateFileEditor(file, "Target editor");
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.GetPackFileContainer(file))
                .Returns(container);
            var editorDatabase = new Mock<IEditorDatabase>();
            var promptCount = 0;
            var promptButtons = default(MessageBoxButton);
            var manager = new EditorManager(
                Mock.Of<IGlobalEventHub>(),
                packFileService.Object,
                editorDatabase.Object,
                (_, _, buttons) =>
                {
                    promptCount++;
                    promptButtons = buttons;
                    return promptResult;
                });
            manager.CurrentEditorsList.Add(editor.Object);

            var result = manager.TryCloseEditorsForContainer(container);

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(
                    result,
                    NUnit.Framework.Is.EqualTo(expectedResult));
                NUnit.Framework.Assert.That(promptCount, NUnit.Framework.Is.EqualTo(1));
                NUnit.Framework.Assert.That(
                    promptButtons,
                    NUnit.Framework.Is.EqualTo(MessageBoxButton.YesNo));
                NUnit.Framework.Assert.That(
                    manager.CurrentEditorsList.Contains(editor.Object),
                    NUnit.Framework.Is.EqualTo(!expectedResult));
            });
            editor.Verify(
                item => item.Close(),
                expectedResult ? Times.Once() : Times.Never());
            editorDatabase.Verify(
                database => database.DestroyEditor(editor.Object),
                expectedResult ? Times.Once() : Times.Never());
        }

        [NUnit.Framework.TestCase(MessageBoxResult.OK, true)]
        [NUnit.Framework.TestCase(MessageBoxResult.Cancel, false)]
        public void TryCloseEditorsForContainer_MultipleEditorsWithUnsavedChanges_IsAtomic(
            MessageBoxResult promptResult,
            bool expectedResult)
        {
            _ = new LocalizationManager();
            var target = new PackFileContainer("target");
            var other = new PackFileContainer("other");
            var unsavedFile = PackFile.CreateFromBytes("unsaved.bin", [1]);
            var targetFile = PackFile.CreateFromBytes("target.bin", [2]);
            var otherFile = PackFile.CreateFromBytes("other.bin", [3]);
            var unknownFile = PackFile.CreateFromBytes("unknown.bin", [4]);
            var unsavedEditor =
                CreateFileEditor(unsavedFile, "Unsaved editor", true);
            var targetEditor =
                CreateFileEditor(targetFile, "Target editor");
            var otherEditor =
                CreateFileEditor(otherFile, "Other editor");
            var unknownEditor =
                CreateFileEditor(unknownFile, "Unknown editor");
            var nonFileEditor = new Mock<IEditorInterface>();
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(
                    service => service.GetPackFileContainer(
                        It.IsAny<PackFile>()))
                .Returns(
                    (PackFile file) =>
                    {
                        if (ReferenceEquals(file, unsavedFile) ||
                            ReferenceEquals(file, targetFile))
                        {
                            return target;
                        }
                        if (ReferenceEquals(file, otherFile))
                            return other;
                        return null;
                    });
            var editorDatabase = new Mock<IEditorDatabase>();
            var promptCount = 0;
            var promptButtons = default(MessageBoxButton);
            var manager = new EditorManager(
                Mock.Of<IGlobalEventHub>(),
                packFileService.Object,
                editorDatabase.Object,
                (_, _, buttons) =>
                {
                    promptCount++;
                    promptButtons = buttons;
                    return promptResult;
                });
            manager.CurrentEditorsList.Add(unsavedEditor.Object);
            manager.CurrentEditorsList.Add(targetEditor.Object);
            manager.CurrentEditorsList.Add(otherEditor.Object);
            manager.CurrentEditorsList.Add(unknownEditor.Object);
            manager.CurrentEditorsList.Add(nonFileEditor.Object);

            var result = manager.TryCloseEditorsForContainer(target);

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(
                    result,
                    NUnit.Framework.Is.EqualTo(expectedResult));
                NUnit.Framework.Assert.That(promptCount, NUnit.Framework.Is.EqualTo(1));
                NUnit.Framework.Assert.That(
                    promptButtons,
                    NUnit.Framework.Is.EqualTo(MessageBoxButton.OKCancel));
                NUnit.Framework.Assert.That(
                    manager.CurrentEditorsList.Contains(unsavedEditor.Object),
                    NUnit.Framework.Is.EqualTo(!expectedResult));
                NUnit.Framework.Assert.That(
                    manager.CurrentEditorsList.Contains(targetEditor.Object),
                    NUnit.Framework.Is.EqualTo(!expectedResult));
                NUnit.Framework.Assert.That(
                    manager.CurrentEditorsList,
                    NUnit.Framework.Does.Contain(otherEditor.Object));
                NUnit.Framework.Assert.That(
                    manager.CurrentEditorsList,
                    NUnit.Framework.Does.Contain(unknownEditor.Object));
                NUnit.Framework.Assert.That(
                    manager.CurrentEditorsList,
                    NUnit.Framework.Does.Contain(nonFileEditor.Object));
            });
            foreach (var editor in new[] { unsavedEditor, targetEditor })
            {
                editor.Verify(
                    item => item.Close(),
                    expectedResult ? Times.Once() : Times.Never());
                editorDatabase.Verify(
                    database => database.DestroyEditor(editor.Object),
                    expectedResult ? Times.Once() : Times.Never());
            }
            foreach (var editor in new[] { otherEditor, unknownEditor })
            {
                editor.Verify(item => item.Close(), Times.Never);
                editorDatabase.Verify(
                    database => database.DestroyEditor(editor.Object),
                    Times.Never);
            }
            nonFileEditor.Verify(item => item.Close(), Times.Never);
            editorDatabase.Verify(
                database => database.DestroyEditor(nonFileEditor.Object),
                Times.Never);
        }

        [NUnit.Framework.Test]
        public void TryCloseEditorsForContainer_PromptThrows_ClosesNothing()
        {
            _ = new LocalizationManager();
            var container = new PackFileContainer("target");
            var firstFile = PackFile.CreateFromBytes("first.bin", [1]);
            var secondFile = PackFile.CreateFromBytes("second.bin", [2]);
            var firstEditor =
                CreateFileEditor(firstFile, "First editor");
            var secondEditor =
                CreateFileEditor(secondFile, "Second editor");
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(
                    service => service.GetPackFileContainer(
                        It.IsAny<PackFile>()))
                .Returns(container);
            var editorDatabase = new Mock<IEditorDatabase>();
            var manager = new EditorManager(
                Mock.Of<IGlobalEventHub>(),
                packFileService.Object,
                editorDatabase.Object,
                (_, _, _) => throw new InvalidOperationException("prompt"));
            manager.CurrentEditorsList.Add(firstEditor.Object);
            manager.CurrentEditorsList.Add(secondEditor.Object);

            NUnit.Framework.Assert.That(
                () => manager.TryCloseEditorsForContainer(container),
                NUnit.Framework.Throws.TypeOf<InvalidOperationException>());

            NUnit.Framework.Assert.That(
                manager.CurrentEditorsList,
                NUnit.Framework.Is.EquivalentTo(
                    new[] { firstEditor.Object, secondEditor.Object }));
            firstEditor.Verify(item => item.Close(), Times.Never);
            secondEditor.Verify(item => item.Close(), Times.Never);
            editorDatabase.Verify(
                database => database.DestroyEditor(It.IsAny<IEditorInterface>()),
                Times.Never);
        }

        [NUnit.Framework.Test]
        public void TryCloseEditorsForContainer_UsesOwnerCapturedWhenEditorWasOpened()
        {
            _ = new LocalizationManager();
            var container = new PackFileContainer("target");
            var file = PackFile.CreateFromBytes("removed.bin", [1]);
            var editor = CreateFileEditor(file, "Removed file editor");
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.GetFullPath(file, null))
                .Returns("removed.bin");
            packFileService
                .Setup(service => service.GetPackFileContainer(file))
                .Returns(container);
            var editorDatabase = new Mock<IEditorDatabase>();
            editorDatabase
                .Setup(database => database.Create("removed.bin", null))
                .Returns(editor.Object);
            var manager = new EditorManager(
                Mock.Of<IGlobalEventHub>(),
                packFileService.Object,
                editorDatabase.Object,
                (_, _, _) => MessageBoxResult.Yes);
            manager.CreateFromFile(file, null);
            packFileService
                .Setup(service => service.GetPackFileContainer(file))
                .Returns((PackFileContainer?)null);

            var result = manager.TryCloseEditorsForContainer(container);

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(result, NUnit.Framework.Is.True);
                NUnit.Framework.Assert.That(manager.CurrentEditorsList, NUnit.Framework.Is.Empty);
            });
            editor.Verify(item => item.Close(), Times.Once);
            editorDatabase.Verify(
                database => database.DestroyEditor(editor.Object),
                Times.Once);
            packFileService.Verify(
                service => service.GetPackFileContainer(file),
                Times.Once);
        }

        [NUnit.Framework.Test]
        public void BeforePackFileContainerRemoved_WhenAlreadyVetoed_DoesNotScheduleOrClose()
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
            var container = new PackFileContainer("target");
            var firstFile = PackFile.CreateFromBytes("first.bin", [1]);
            var secondFile = PackFile.CreateFromBytes("second.bin", [2]);
            var firstEditor =
                CreateFileEditor(firstFile, "Unsaved editor", true);
            var secondEditor =
                CreateFileEditor(secondFile, "Second editor");
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(
                    service => service.GetPackFileContainer(
                        It.IsAny<PackFile>()))
                .Returns(container);
            var editorDatabase = new Mock<IEditorDatabase>();
            var manager = new EditorManager(
                eventHub.Object,
                packFileService.Object,
                editorDatabase.Object,
                (_, _, _) => MessageBoxResult.Cancel);
            manager.CurrentEditorsList.Add(firstEditor.Object);
            manager.CurrentEditorsList.Add(secondEditor.Object);
            var args = new BeforePackFileContainerRemovedEvent(container)
            {
                AllowClose = false,
            };

            beforeRemove!(args);

            NUnit.Framework.Assert.Multiple(() =>
            {
                NUnit.Framework.Assert.That(args.AllowClose, NUnit.Framework.Is.False);
                NUnit.Framework.Assert.That(
                    args.HasApprovedCloseAction,
                    NUnit.Framework.Is.False);
                NUnit.Framework.Assert.That(
                    manager.CurrentEditorsList,
                    NUnit.Framework.Is.EquivalentTo(
                        new[] { firstEditor.Object, secondEditor.Object }));
            });
            firstEditor.Verify(item => item.Close(), Times.Never);
            secondEditor.Verify(item => item.Close(), Times.Never);
            editorDatabase.Verify(
                database => database.DestroyEditor(It.IsAny<IEditorInterface>()),
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

        private static Mock<IEditorInterface> CreateFileEditor(
            PackFile file,
            string displayName,
            bool? hasUnsavedChanges = null)
        {
            var editor = new Mock<IEditorInterface>();
            editor.SetupProperty(item => item.DisplayName, displayName);
            editor.As<IFileEditor>()
                .SetupGet(item => item.CurrentFile)
                .Returns(file);
            if (hasUnsavedChanges.HasValue)
            {
                editor.As<ISaveableEditor>()
                    .SetupGet(item => item.HasUnsavedChanges)
                    .Returns(hasUnsavedChanges.Value);
            }

            return editor;
        }
    }
}
