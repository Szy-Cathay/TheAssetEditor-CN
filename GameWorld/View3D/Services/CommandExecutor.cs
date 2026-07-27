using GameWorld.Core.Commands;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;

namespace GameWorld.Core.Services
{
    public class CommandStackChangedEvent
    {
        public string HintText { get; internal set; } = "";
        public bool IsMutation { get; internal set; }
    }

    public class CommandStackUndoEvent
    {
        public string HintText { get; set; } = "";
        public bool IsMutation { get; internal set; }
    }

    public class CommandExecutor
    {
        protected ILogger _logger = Logging.Create<CommandExecutor>();
        private readonly Stack<CommandHistoryEntry> _commands = new();
        private readonly Stack<CommandHistoryEntry> _redoCommands = new();
        private readonly IEventHub _eventHub;
        private long _nextDocumentStateId;

        public long CurrentDocumentStateId { get; private set; }

        public CommandExecutor(IEventHub eventHub)
        {
            _eventHub = eventHub;
        }

        public bool ExecuteCommand(ICommand command, bool isUndoable = true)
        {
            if (command == null)
                throw new ArgumentNullException("Command is null");

            _logger.Here().Information($"Executing {command.GetType().Name}");
            try
            {
                command.Execute();
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Failed to execute command : {e}");
                return false;
            }

            var previousDocumentStateId = CurrentDocumentStateId;
            var nextDocumentStateId = previousDocumentStateId;
            if (command.AffectsDocument)
            {
                nextDocumentStateId = ++_nextDocumentStateId;
                CurrentDocumentStateId = nextDocumentStateId;
            }

            // Only add successfully executed mutation commands to history.
            if (isUndoable && command.IsMutation)
            {
                _redoCommands.Clear();
                _commands.Push(new CommandHistoryEntry(
                    command,
                    previousDocumentStateId,
                    nextDocumentStateId));
            }
            else if (command.AffectsDocument)
            {
                _redoCommands.Clear();
            }

            if (isUndoable || command.AffectsDocument)
            {
                _eventHub.Publish(new CommandStackChangedEvent()
                {
                    HintText = command.HintText,
                    IsMutation = command.AffectsDocument,
                });
            }

            return true;
        }

        public bool CanUndo() => _commands.Count != 0;

        public bool Undo()
        {
            if (!CanUndo())
                return false;

            var historyEntry = _commands.Peek();
            var command = historyEntry.Command;
            _logger.Here().Information($"Undoing {command.GetType().Name}");
            try
            {
                command.Undo();
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Failed to Undoing command : {e}");
                return false;
            }

            _commands.Pop();
            _redoCommands.Push(historyEntry);
            CurrentDocumentStateId = historyEntry.PreviousDocumentStateId;
            _eventHub.Publish(new CommandStackUndoEvent()
            {
                HintText = command.HintText,
                IsMutation = command.AffectsDocument,
            });
            return true;
        }

        public bool CanRedo() => _redoCommands.Count != 0;

        public bool Redo()
        {
            if (!CanRedo())
                return false;

            var historyEntry = _redoCommands.Peek();
            var command = historyEntry.Command;
            _logger.Here().Information($"Redoing {command.GetType().Name}");
            try
            {
                if (command is IRedoableCommand redoable)
                    redoable.Redo();
                else
                    command.Execute();
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Failed to Redo command : {e}");
                return false;
            }

            _redoCommands.Pop();
            _commands.Push(historyEntry);
            CurrentDocumentStateId = historyEntry.NextDocumentStateId;
            _eventHub.Publish(new CommandStackChangedEvent()
            {
                HintText = command.HintText,
                IsMutation = command.AffectsDocument,
            });
            return true;
        }

        public string GetRedoHint()
        {
            if (!CanRedo())
                return "No items to redo";

            return _redoCommands.Peek().Command.HintText;
        }

        public string GetUndoHint()
        {
            if (!CanUndo())
                return "No items to undo";

            return _commands.Peek().Command.HintText;
        }

        private sealed record CommandHistoryEntry(
            ICommand Command,
            long PreviousDocumentStateId,
            long NextDocumentStateId);
    }
}

