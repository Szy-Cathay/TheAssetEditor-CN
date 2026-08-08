using Editors.KitbasherEditor.Core.MenuBarViews;
using Editors.KitbasherEditor.Components;
using Shared.Ui.Common.MenuSystem;
using System.Windows.Input;

namespace Editors.KitbasherEditor.UiCommands
{
    internal class ScaleGizmoUpCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Increase Gizmo size";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = new Hotkey(Key.Add, ModifierKeys.None);

        private readonly KitbashModelGizmoComponent _gizmoComponent;


        public ScaleGizmoUpCommand(
            KitbashModelGizmoComponent gizmoComponent)
        {
            _gizmoComponent = gizmoComponent;
        }

        public void Execute() => _gizmoComponent.ModifyGizmoScale(0.5f);
    }

    internal class ScaleGizmoDownCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Decrease Gizmo size";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey HotKey { get; } =
            new Hotkey(Key.Subtract, ModifierKeys.None);

        private readonly KitbashModelGizmoComponent _gizmoComponent;


        public ScaleGizmoDownCommand(
            KitbashModelGizmoComponent gizmoComponent)
        {
            _gizmoComponent = gizmoComponent;
        }

        public void Execute() => _gizmoComponent.ModifyGizmoScale(-0.5f);
    }
}
