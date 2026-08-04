using System.Windows.Input;
using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using Shared.Core.Services;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    public class MergeObjectsCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Merge selected meshes";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.TwoOrMoreObjectsSelected;
        public Hotkey? HotKey { get; } = new Hotkey(Key.M, ModifierKeys.Control);

        private readonly SelectionManager _selectionManager;
        private readonly ObjectEditor _objectEditor;
        private readonly IStandardDialogs _standardDialogs;

        public MergeObjectsCommand(SelectionManager selectionManager, ObjectEditor objectEditor, IStandardDialogs standardDialogs)
        {
            _selectionManager = selectionManager;
            _objectEditor = objectEditor;
            _standardDialogs = standardDialogs;
        }

        public void Execute()
        {
            if (_selectionManager.GetState() is ObjectSelectionState objectSelectionState)
            {
                if (objectSelectionState.CurrentSelection().Count >= 2)
                {
                    if (!_objectEditor.CombineMeshes(objectSelectionState, out var errorList))
                        _standardDialogs.ShowErrorViewDialog(LocalizationManager.Instance.Get("Kitbash.Combine.Errors"), errorList, false);
                }
            }
        }
    }
}
