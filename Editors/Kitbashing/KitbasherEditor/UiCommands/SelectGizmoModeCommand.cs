using Editors.KitbasherEditor.Core.MenuBarViews;
using Editors.KitbasherEditor.Components;
using GameWorld.Core.Components.Gizmo;
using KitbasherEditor.ViewModels.MenuBarViews;
using Shared.Ui.Common.MenuSystem;
using System.Windows.Input;

namespace Editors.KitbasherEditor.UiCommands
{
    internal class SelectGizmoModeCommand(KitbashSceneComponentSet components, TransformToolViewModel transformToolViewModel) : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Select Gizmo";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;  // Disabled hotkey - use toolbar only

        public void Execute()
        {
            components.SelectionSettings.IsCircleSelection = false;
            components.ModelGizmo.ResetScale();
            transformToolViewModel.SetMode(TransformToolViewModel.TransformMode.None);
            components.ModelGizmo.Disable();
        }
    }

    internal class MoveGizmoModeCommand(KitbashSceneComponentSet components, TransformToolViewModel transformToolViewModel) : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Move Gizmo";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;  // Disabled hotkey - use G for Blender-style modal transform instead

        public void Execute()
        {
            components.ModelGizmo.ResetScale();
            transformToolViewModel.SetMode(TransformToolViewModel.TransformMode.Translate);
            components.ModelGizmo.SetGizmoMode(GizmoMode.Translate);
        }
    }

    internal class RotateGizmoModeCommand(KitbashSceneComponentSet components, TransformToolViewModel transformToolViewModel) : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Rotate Gizmo";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;  // Disabled hotkey - use R for Blender-style modal transform instead

        public void Execute()
        {
            components.ModelGizmo.ResetScale();
            transformToolViewModel.SetMode(TransformToolViewModel.TransformMode.Rotate);
            components.ModelGizmo.SetGizmoMode(GizmoMode.Rotate);
        }
    }

    internal class ScaleGizmoModeCommand(KitbashSceneComponentSet components, TransformToolViewModel transformToolViewModel) : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Scale Gizmo";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;  // Disabled hotkey - use S for Blender-style modal transform instead

        public void Execute()
        {
            components.ModelGizmo.ResetScale();
            transformToolViewModel.SetMode(TransformToolViewModel.TransformMode.Scale);
            components.ModelGizmo.SetGizmoMode(GizmoMode.NonUniformScale);
        }
    }
}
