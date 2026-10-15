using GameWorld.Core.Commands;
using GameWorld.Core.Services;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ViewModels.SceneNodeEditor
{
    public sealed class SceneNodePropertyEditor : IDocumentPropertyEditor
    {
        private readonly CommandExecutor _commandExecutor;
        private readonly string _hintText;
        private bool _isApplyingCommand;
        private bool _isEditing;
        private ICommand? _firstEdit;
        private ICommand? _lastEdit;
        private object? _initialValue;
        private object? _latestValue;

        public SceneNodePropertyEditor(CommandExecutor commandExecutor)
        {
            _commandExecutor = commandExecutor;
            _hintText = LocalizationManager.Instance?.Get("Kitbash.CommandHint.EditSidebarProperty")
                ?? "Kitbash.CommandHint.EditSidebarProperty";
        }

        public void Update<T>(T currentValue, T newValue, Action<T> apply)
        {
            if (_isApplyingCommand ||
                EqualityComparer<T>.Default.Equals(currentValue, newValue))
            {
                return;
            }

            var command = new SceneNodePropertyChangeCommand<T>(
                currentValue,
                newValue,
                value => Apply(value, apply),
                _hintText);
            if (_isEditing)
            {
                if (_firstEdit == null)
                {
                    _firstEdit = command;
                    _initialValue = currentValue;
                }
                _lastEdit = command;
                _latestValue = newValue;
                command.Execute();
            }
            else
                _commandExecutor.ExecuteCommand(command);
        }

        public void BeginEdit()
        {
            EndEdit(true);
            _isEditing = true;
        }

        public void EndEdit(bool cancelled)
        {
            var first = _firstEdit;
            var last = _lastEdit;
            var unchanged = Equals(_initialValue, _latestValue);
            _isEditing = false;
            _firstEdit = _lastEdit = null;
            _initialValue = _latestValue = null;
            if (first == null || last == null)
                return;
            if (cancelled)
                first.Undo();
            else if (!unchanged)
                _commandExecutor.ExecuteCommand(new NumericPropertyEditCommand(first, last, _hintText));
        }

        public void Update<T>(
            T currentValue,
            T newValue,
            Action<T> updateModel,
            Action<T> updateView) =>
            Update(
                currentValue,
                newValue,
                value =>
                {
                    updateModel(value);
                    updateView(value);
                });

        private void Apply<T>(T value, Action<T> apply)
        {
            _isApplyingCommand = true;
            try
            {
                apply(value);
            }
            finally
            {
                _isApplyingCommand = false;
            }
        }
    }

    internal sealed class NumericPropertyEditCommand(ICommand first, ICommand last, string hintText) : IRedoableCommand
    {
        public string HintText => hintText;
        public bool IsMutation => true;
        public void Execute() { }
        public void Undo() => first.Undo();
        public void Redo() => last.Execute();
    }

    internal sealed class SceneNodePropertyChangeCommand<T> : ICommand
    {
        private readonly T _oldValue;
        private readonly T _newValue;
        private readonly Action<T> _apply;

        public SceneNodePropertyChangeCommand(
            T oldValue,
            T newValue,
            Action<T> apply,
            string hintText)
        {
            _oldValue = oldValue;
            _newValue = newValue;
            _apply = apply;
            HintText = hintText;
        }

        public string HintText { get; }
        public bool IsMutation => true;

        public void Execute() => _apply(_newValue);
        public void Undo() => _apply(_oldValue);
    }
}
