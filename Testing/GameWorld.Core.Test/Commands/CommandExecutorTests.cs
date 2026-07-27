using GameWorld.Core.Commands;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;

namespace Testing.GameWorld.Core.Commands
{
    [TestFixture]
    public class CommandExecutorTests
    {
        [Test]
        public void Redo_PlainCommandExecutesAgainAndRestoresUndoStack()
        {
            var eventHub = new Mock<IEventHub>();
            var plain = new Mock<ICommand>();
            plain.SetupGet(command => command.HintText).Returns("Plain command");
            plain.SetupGet(command => command.IsMutation).Returns(true);
            plain.SetupGet(command => command.AffectsDocument).Returns(true);
            var executor = new CommandExecutor(eventHub.Object);

            executor.ExecuteCommand(plain.Object);
            Assert.That(executor.CanUndo(), Is.True);
            Assert.That(executor.CanRedo(), Is.False);

            executor.Undo();
            Assert.That(executor.CanUndo(), Is.False);
            Assert.That(executor.CanRedo(), Is.True);

            executor.Redo();

            plain.Verify(command => command.Execute(), Times.Exactly(2));
            Assert.That(executor.CanUndo(), Is.True);
            Assert.That(executor.CanRedo(), Is.False);
            eventHub.Verify(hub => hub.Publish(It.Is<CommandStackChangedEvent>(notification =>
                notification.HintText == "Plain command" && notification.IsMutation)), Times.Exactly(2));
        }

        [Test]
        public void Redo_RedoableCommandUsesExplicitRedoAndRestoresUndoStack()
        {
            var eventHub = new Mock<IEventHub>();
            var redoable = new Mock<IRedoableCommand>();
            redoable.SetupGet(command => command.HintText).Returns("Redoable command");
            redoable.SetupGet(command => command.IsMutation).Returns(true);
            redoable.SetupGet(command => command.AffectsDocument).Returns(true);
            var executor = new CommandExecutor(eventHub.Object);

            executor.ExecuteCommand(redoable.Object);
            Assert.That(executor.CanUndo(), Is.True);
            Assert.That(executor.CanRedo(), Is.False);

            executor.Undo();
            Assert.That(executor.CanUndo(), Is.False);
            Assert.That(executor.CanRedo(), Is.True);

            executor.Redo();

            redoable.Verify(command => command.Execute(), Times.Once);
            redoable.Verify(command => command.Redo(), Times.Once);
            Assert.That(executor.CanUndo(), Is.True);
            Assert.That(executor.CanRedo(), Is.False);
            eventHub.Verify(hub => hub.Publish(It.Is<CommandStackChangedEvent>(notification =>
                notification.HintText == "Redoable command" && notification.IsMutation)), Times.Exactly(2));
        }

        [Test]
        public void ExecuteCommand_WhenExecuteThrows_DoesNotAddUndoEntryOrPublishChange()
        {
            var eventHub = new Mock<IEventHub>();
            var command = new Mock<ICommand>();
            command.SetupGet(item => item.IsMutation).Returns(true);
            command.Setup(item => item.Execute()).Throws<InvalidOperationException>();
            var executor = new CommandExecutor(eventHub.Object);

            executor.ExecuteCommand(command.Object);

            Assert.Multiple(() =>
            {
                Assert.That(executor.CanUndo(), Is.False);
                Assert.That(executor.CanRedo(), Is.False);
            });
            eventHub.Verify(
                hub => hub.Publish(It.IsAny<CommandStackChangedEvent>()),
                Times.Never);
        }

        [Test]
        public void ExecuteCommand_WhenExecuteThrows_PreservesExistingRedoHistory()
        {
            var eventHub = new Mock<IEventHub>();
            var successful = new Mock<ICommand>();
            successful.SetupGet(item => item.IsMutation).Returns(true);
            var failing = new Mock<ICommand>();
            failing.SetupGet(item => item.IsMutation).Returns(true);
            failing.Setup(item => item.Execute()).Throws<InvalidOperationException>();
            var executor = new CommandExecutor(eventHub.Object);
            executor.ExecuteCommand(successful.Object);
            executor.Undo();

            executor.ExecuteCommand(failing.Object);

            Assert.Multiple(() =>
            {
                Assert.That(executor.CanUndo(), Is.False);
                Assert.That(executor.CanRedo(), Is.True);
            });
        }

        [Test]
        public void Undo_WhenUndoThrows_KeepsCommandOnUndoStack()
        {
            var eventHub = new Mock<IEventHub>();
            var command = new Mock<ICommand>();
            command.SetupGet(item => item.IsMutation).Returns(true);
            var executor = new CommandExecutor(eventHub.Object);
            executor.ExecuteCommand(command.Object);
            eventHub.Invocations.Clear();
            command.Setup(item => item.Undo()).Throws<InvalidOperationException>();

            executor.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(executor.CanUndo(), Is.True);
                Assert.That(executor.CanRedo(), Is.False);
            });
            eventHub.Verify(
                hub => hub.Publish(It.IsAny<CommandStackUndoEvent>()),
                Times.Never);
        }

        [Test]
        public void Redo_WhenExecuteThrows_KeepsCommandOnRedoStack()
        {
            var eventHub = new Mock<IEventHub>();
            var command = new Mock<ICommand>();
            command.SetupGet(item => item.IsMutation).Returns(true);
            var executor = new CommandExecutor(eventHub.Object);
            executor.ExecuteCommand(command.Object);
            executor.Undo();
            eventHub.Invocations.Clear();
            command.Setup(item => item.Execute()).Throws<InvalidOperationException>();

            executor.Redo();

            Assert.Multiple(() =>
            {
                Assert.That(executor.CanUndo(), Is.False);
                Assert.That(executor.CanRedo(), Is.True);
            });
            eventHub.Verify(
                hub => hub.Publish(It.IsAny<CommandStackChangedEvent>()),
                Times.Never);
        }

        [Test]
        public void ExecuteCommand_UndoableUiStateChange_DoesNotReportDocumentMutation()
        {
            var eventHub = new Mock<IEventHub>();
            var command = new Mock<ICommand>();
            command.SetupGet(item => item.HintText).Returns("Selection changed");
            command.SetupGet(item => item.IsMutation).Returns(true);
            command.SetupGet(item => item.AffectsDocument).Returns(false);
            var executor = new CommandExecutor(eventHub.Object);

            executor.ExecuteCommand(command.Object);

            Assert.That(executor.CanUndo(), Is.True);
            eventHub.Verify(
                hub => hub.Publish(It.Is<CommandStackChangedEvent>(
                    notification =>
                        notification.HintText == "Selection changed" &&
                        !notification.IsMutation)),
                Times.Once);
        }

        [Test]
        public void Undo_ReportsWhetherTheUndoneCommandAffectedTheDocument()
        {
            var eventHub = new Mock<IEventHub>();
            var command = new Mock<ICommand>();
            command.SetupGet(item => item.HintText).Returns("Document change");
            command.SetupGet(item => item.IsMutation).Returns(true);
            command.SetupGet(item => item.AffectsDocument).Returns(true);
            var executor = new CommandExecutor(eventHub.Object);
            executor.ExecuteCommand(command.Object);
            eventHub.Invocations.Clear();

            executor.Undo();

            eventHub.Verify(
                hub => hub.Publish(It.Is<CommandStackUndoEvent>(
                    notification =>
                        notification.HintText == "Document change" &&
                        notification.IsMutation)),
                Times.Once);
        }

        [Test]
        public void DocumentStateId_UndoAndRedoReturnToTheSameDocumentStates()
        {
            var eventHub = new Mock<IEventHub>();
            var documentCommand = new Mock<ICommand>();
            documentCommand.SetupGet(item => item.IsMutation).Returns(true);
            documentCommand.SetupGet(item => item.AffectsDocument).Returns(true);
            var selectionCommand = new Mock<ICommand>();
            selectionCommand.SetupGet(item => item.IsMutation).Returns(true);
            selectionCommand.SetupGet(item => item.AffectsDocument).Returns(false);
            var executor = new CommandExecutor(eventHub.Object);
            var initialState = executor.CurrentDocumentStateId;

            executor.ExecuteCommand(documentCommand.Object);
            var changedState = executor.CurrentDocumentStateId;
            executor.ExecuteCommand(selectionCommand.Object);

            Assert.That(changedState, Is.Not.EqualTo(initialState));
            Assert.That(executor.CurrentDocumentStateId, Is.EqualTo(changedState));

            executor.Undo();
            Assert.That(executor.CurrentDocumentStateId, Is.EqualTo(changedState));
            executor.Undo();
            Assert.That(executor.CurrentDocumentStateId, Is.EqualTo(initialState));

            executor.Redo();
            Assert.That(executor.CurrentDocumentStateId, Is.EqualTo(changedState));
        }
    }
}
