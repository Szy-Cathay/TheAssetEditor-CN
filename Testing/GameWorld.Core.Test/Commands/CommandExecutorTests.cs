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
    }
}
