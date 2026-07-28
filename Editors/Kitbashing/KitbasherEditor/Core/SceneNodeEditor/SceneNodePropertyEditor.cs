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

            _commandExecutor.ExecuteCommand(new SceneNodePropertyChangeCommand<T>(
                currentValue,
                newValue,
                value => Apply(value, apply),
                _hintText));
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
