using Editors.CscEditor.Services;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Test.CscEditor;

public class CscGizmoHistoryTests
{
    [TestCase("SetTarget")]
    [TestCase("Disable")]
    [TestCase("Dispose")]
    public void Public_interruptions_finish_active_drag_once_and_release_mouse(
        string interruption)
    {
        var mouse = new TestMouse();
        var gizmo = new CscGizmoComponent(mouse);
        var starts = 0;
        var completions = 0;
        gizmo.DragStarted += () => starts++;
        gizmo.DragCompleted += () => completions++;
        gizmo.BeginDragInteraction();

        switch (interruption)
        {
            case "SetTarget":
                gizmo.SetTarget(null);
                break;
            case "Disable":
                gizmo.Disable();
                break;
            case "Dispose":
                gizmo.Dispose();
                break;
        }
        gizmo.CompleteDragInteraction();

        Assert.Multiple(() =>
        {
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(mouse.MouseOwner, Is.Null);
            Assert.That(mouse.ClearCount, Is.EqualTo(1));
            Assert.That(gizmo.IsDragActive, Is.False);
        });
    }

    [Test]
    public void Lost_mouse_ownership_finishes_drag_before_next_update()
    {
        var mouse = new TestMouse();
        var gizmo = new CscGizmoComponent(mouse);
        var completions = 0;
        gizmo.DragCompleted += () => completions++;
        gizmo.BeginDragInteraction();
        mouse.MouseOwner = null;

        gizmo.Update(new GameTime());

        Assert.Multiple(() =>
        {
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(gizmo.IsDragActive, Is.False);
            Assert.That(mouse.ClearCount, Is.EqualTo(1));
        });
    }

    private sealed class TestMouse : IMouseComponent
    {
        public int ClearCount { get; private set; }
        public IGameComponent MouseOwner { get; set; } = null!;
        public bool Enabled { get; set; } = true;
        public int UpdateOrder { get; set; }
        public event EventHandler<EventArgs>? EnabledChanged;
        public event EventHandler<EventArgs>? UpdateOrderChanged;

        public void ClearStates() => ClearCount++;
        public int DeletaScrollWheel() => 0;
        public Vector2 DeltaPosition() => Vector2.Zero;
        public Vector2 Position() => Vector2.Zero;
        public Vector2? GetPressPosition(MouseButton button) => null;
        public Vector2? CapturedCursorPosition => null;
        public event Action CaptureInterrupted { add { } remove { } }
        public void BeginContinuousDrag(bool wrap = true) { }
        public void EndContinuousDrag() { }
        public bool IsMouseButtonDown(MouseButton button) => false;
        public bool IsMouseButtonPressed(MouseButton button) => false;
        public bool IsMouseButtonReleased(MouseButton button) => false;
        public bool IsMouseOwner(IGameComponent component) =>
            MouseOwner == null || MouseOwner == component;
        public MouseState State() => new();
        public MouseState LastState() => new();
        public void Update(GameTime t)
        {
        }

        public void SetCursorPosition(int x, int y)
        {
        }

        public Vector2 GetScreenSize() => Vector2.Zero;
        public void SetModalCursor(ModalCursorType cursorType)
        {
        }

        public void ResetCursor()
        {
        }

        public void Initialize()
        {
        }
    }
}
