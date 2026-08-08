using Editors.KitbasherEditor.Core.MenuBarViews;
using Editors.KitbasherEditor.Components;
using GameWorld.Core.Components.Gizmo;
using KitbasherEditor.ViewModels.MenuBarViews;
using Shared.Ui.Common.MenuSystem;
using System.Windows.Input;

namespace Editors.KitbasherEditor.UiCommands
{
    internal class SelectGizmoModeCommand(KitbashModelGizmoComponent gizmoComponent, TransformToolViewModel transformToolViewModel) : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Select Gizmo";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;  // Disabled hotkey - use toolbar only

        public void Execute()
        {
            gizmoComponent.ResetScale();
            transformToolViewModel.SetMode(TransformToolViewModel.TransformMode.None);
            gizmoComponent.Disable();
        }
    }

    internal class MoveGizmoModeCommand(KitbashModelGizmoComponent gizmoComponent, TransformToolViewModel transformToolViewModel) : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Move Gizmo";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;  // Disabled hotkey - use G for Blender-style modal transform instead

        public void Execute()
        {
            gizmoComponent.ResetScale();
            transformToolViewModel.SetMode(TransformToolViewModel.TransformMode.Translate);
            gizmoComponent.SetGizmoMode(GizmoMode.Translate);
        }
    }

    internal class RotateGizmoModeCommand(KitbashModelGizmoComponent gizmoComponent, TransformToolViewModel transformToolViewModel) : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Rotate Gizmo";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;  // Disabled hotkey - use R for Blender-style modal transform instead

        public void Execute()
        {
            gizmoComponent.ResetScale();
            transformToolViewModel.SetMode(TransformToolViewModel.TransformMode.Rotate);
            gizmoComponent.SetGizmoMode(GizmoMode.Rotate);
        }
    }

    internal class ScaleGizmoModeCommand(KitbashModelGizmoComponent gizmoComponent, TransformToolViewModel transformToolViewModel) : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Scale Gizmo";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;  // Disabled hotkey - use S for Blender-style modal transform instead

        public void Execute()
        {
            gizmoComponent.ResetScale();
            transformToolViewModel.SetMode(TransformToolViewModel.TransformMode.Scale);
            gizmoComponent.SetGizmoMode(GizmoMode.NonUniformScale);
        }
    }
}
