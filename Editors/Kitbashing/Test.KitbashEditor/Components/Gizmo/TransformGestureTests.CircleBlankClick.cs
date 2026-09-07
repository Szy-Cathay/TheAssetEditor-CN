using Editors.KitbasherEditor.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Selection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;

namespace Testing.GameWorld.Core.Components.Gizmo;

public partial class TransformGestureTests
{
    [TestCase(GeometrySelectionMode.Vertex, false)]
    [TestCase(GeometrySelectionMode.Vertex, true)]
    [TestCase(GeometrySelectionMode.Edge, false)]
    [TestCase(GeometrySelectionMode.Edge, true)]
    [TestCase(GeometrySelectionMode.Face, false)]
    [TestCase(GeometrySelectionMode.Face, true)]
    public void CircleMouse_EmptyClickClearsSelectionAsOneUndoableStep(GeometrySelectionMode mode, bool betweenFrames)
    {
        using var context = CreateOccludedEditContext(mode);
        using var input = CreateSelectionInput(context);
        SelectCircleTestElements(context);
        var originalCount = context.SelectionManager.GetState().SelectionCount();
        var selectedObject = context.SelectionManager.GetState().GetSingleSelectedObject();
        var document = context.CommandExecutor.CurrentDocumentStateId;
        input.Settings.IsCircleSelection = true;

        if (!betweenFrames)
        {
            CirclePointerFrame(context, input, new Vector2(50, 50), pressed: true, down: true);
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(originalCount));
        }
        CirclePointerFrame(context, input, new Vector2(52, 51), pressed: betweenFrames, released: true);
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero);
        Assert.That(context.SelectionManager.GetState().GetSingleSelectedObject(), Is.SameAs(selectedObject));
        Assert.That(context.SelectionManager.GetState().Mode, Is.EqualTo(mode));
        Assert.That(input.Settings.IsCircleSelection, Is.True);
        Assert.That(input.IsCircleSelecting, Is.False);
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(document));

        CirclePointerFrame(context, input, new Vector2(50, 50), pressed: true, released: true);
        context.CommandExecutor.Undo();
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(originalCount));
        Assert.That(context.CommandExecutor.CanUndo(), Is.False, "Clicking empty space again must not add an empty undo step.");
        context.CommandExecutor.Redo();
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero);
        Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(document));
    }

    [TestCase(GeometrySelectionMode.Vertex, Keys.LeftControl)]
    [TestCase(GeometrySelectionMode.Vertex, Keys.RightShift)]
    [TestCase(GeometrySelectionMode.Edge, Keys.LeftControl)]
    [TestCase(GeometrySelectionMode.Edge, Keys.RightShift)]
    [TestCase(GeometrySelectionMode.Face, Keys.LeftControl)]
    [TestCase(GeometrySelectionMode.Face, Keys.RightShift)]
    public void CircleMouse_ModifiedEmptyClickKeepsSelection(GeometrySelectionMode mode, Keys modifier)
    {
        using var context = CreateOccludedEditContext(mode);
        using var input = CreateSelectionInput(context);
        SelectCircleTestElements(context);
        var originalCount = context.SelectionManager.GetState().SelectionCount();
        input.Settings.IsCircleSelection = true;
        context.Keyboard.Setup(k => k.IsKeyDown(modifier)).Returns(true);
        CirclePointerFrame(context, input, new Vector2(50, 50), pressed: true, released: true);
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(originalCount));
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
    }

    [TestCase(GeometrySelectionMode.Vertex, false)]
    [TestCase(GeometrySelectionMode.Vertex, true)]
    [TestCase(GeometrySelectionMode.Edge, false)]
    [TestCase(GeometrySelectionMode.Edge, true)]
    [TestCase(GeometrySelectionMode.Face, false)]
    [TestCase(GeometrySelectionMode.Face, true)]
    public void CircleMouse_EmptyDragKeepsSelectionEvenWhenReturningToStart(GeometrySelectionMode mode, bool returnToStart)
    {
        using var context = CreateOccludedEditContext(mode);
        using var input = CreateSelectionInput(context);
        SelectCircleTestElements(context);
        var originalCount = context.SelectionManager.GetState().SelectionCount();
        input.Settings.IsCircleSelection = true;
        CirclePointerFrame(context, input, new Vector2(50, 50), pressed: true, down: true);
        CirclePointerFrame(context, input, new Vector2(150, 50), down: true);
        CirclePointerFrame(context, input, new Vector2(returnToStart ? 50 : 150, 50), released: true);
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(originalCount));
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
    }

    [TestCase(GeometrySelectionMode.Vertex)]
    [TestCase(GeometrySelectionMode.Edge)]
    [TestCase(GeometrySelectionMode.Face)]
    public void CircleMouse_ClickOnAlreadySelectedElementKeepsSelection(GeometrySelectionMode mode)
    {
        using var context = CreateOccludedEditContext(mode);
        using var input = CreateSelectionInput(context);
        SelectCircleTestElements(context);
        var originalCount = context.SelectionManager.GetState().SelectionCount();
        input.Settings.IsCircleSelection = true;
        var point = mode switch
        {
            GeometrySelectionMode.Vertex => new Vector2(400, 400),
            GeometrySelectionMode.Edge => new Vector2(500, 400),
            _ => new Vector2(500, 467)
        };
        CirclePointerFrame(context, input, point, pressed: true, released: true, pressPosition: point);
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(originalCount));
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CircleMouse_InterruptedEmptyClickKeepsSelection(bool captureLost)
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        using var input = CreateSelectionInput(context);
        SelectCircleTestElements(context);
        input.Settings.IsCircleSelection = true;
        CirclePointerFrame(context, input, new Vector2(50, 50), pressed: true, down: true);
        if (captureLost)
            context.Mouse.Raise(m => m.CaptureInterrupted += null);
        else
        {
            context.Keyboard.Setup(k => k.IsKeyReleased(Keys.Escape)).Returns(true);
            CirclePointerFrame(context, input, new Vector2(50, 50), down: true);
        }
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(6));
        Assert.That(input.Settings.IsCircleSelection, Is.True);
        Assert.That(input.IsCircleSelecting, Is.False);
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
    }

    private static void CirclePointerFrame(ComponentContext context, KitbashSelectionInputComponent input, Vector2 position,
        bool pressed = false, bool down = false, bool released = false, Vector2? pressPosition = null)
    {
        context.Mouse.Setup(m => m.Position()).Returns(position);
        context.Mouse.Setup(m => m.GetPressPosition(MouseButton.Left)).Returns(pressPosition ?? new Vector2(50, 50));
        context.Mouse.Setup(m => m.IsMouseButtonPressed(MouseButton.Left)).Returns(pressed);
        context.Mouse.Setup(m => m.IsMouseButtonDown(MouseButton.Left)).Returns(down);
        context.Mouse.Setup(m => m.IsMouseButtonReleased(MouseButton.Left)).Returns(released);
        input.CaptureSelectionGesture();
        context.Camera.Update(new GameTime());
        context.Component.Update(new GameTime());
        input.Update(new GameTime());
    }
}
