using GameWorld.Core.Components.Selection;
using GameWorld.Core.Components.Input;
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
    public void CircleMouse_CtrlBrushRemovesOnlyHitElementsAndUndoesOneStroke(GeometrySelectionMode mode, bool xray)
    {
        using var context = CreateOccludedEditContext(mode);
        using var input = CreateSelectionInput(context);
        SelectCircleTestElements(context);
        var originalCount = context.SelectionManager.GetState().SelectionCount();
        var document = context.CommandExecutor.CurrentDocumentStateId;
        input.Settings.IsCircleSelection = true;
        input.Settings.IsXRay = xray;
        var point = mode switch
        {
            GeometrySelectionMode.Vertex => new Vector2(400, 400),
            GeometrySelectionMode.Edge => new Vector2(500, 400),
            _ => new Vector2(500, 467)
        };
        context.Keyboard.Setup(k => k.IsKeyDown(Keys.RightControl)).Returns(true);
        context.Mouse.Setup(m => m.Position()).Returns(point);
        context.Mouse.Setup(m => m.GetPressPosition(MouseButton.Left)).Returns(point);
        context.Mouse.Setup(m => m.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
        context.Mouse.Setup(m => m.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
        input.CaptureSelectionGesture();
        context.Camera.Update(new GameTime());
        context.Component.Update(new GameTime());
        input.Update(new GameTime());
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(originalCount - (xray ? 2 : 1)));
        Assert.That(input.Settings.IsCircleSelection, Is.True);
        Assert.That(input.IsCircleSelecting, Is.False);
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(document));
        context.CommandExecutor.Undo();
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(originalCount));
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
    }

    [TestCase(GeometrySelectionMode.Vertex, Keys.G)]
    [TestCase(GeometrySelectionMode.Vertex, Keys.R)]
    [TestCase(GeometrySelectionMode.Vertex, Keys.S)]
    [TestCase(GeometrySelectionMode.Edge, Keys.G)]
    [TestCase(GeometrySelectionMode.Edge, Keys.R)]
    [TestCase(GeometrySelectionMode.Edge, Keys.S)]
    [TestCase(GeometrySelectionMode.Face, Keys.G)]
    [TestCase(GeometrySelectionMode.Face, Keys.R)]
    [TestCase(GeometrySelectionMode.Face, Keys.S)]
    public void CircleMouse_TransformsConfirmAndUndoWithoutChangingSelectionShape(GeometrySelectionMode mode, Keys key)
    {
        using var context = CreateOccludedEditContext(mode);
        using var input = CreateSelectionInput(context);
        SelectCircleTestElements(context);
        var baseline = context.Mesh.VertexArray.ToArray();
        var selectionCount = context.SelectionManager.GetState().SelectionCount();
        input.Settings.IsCircleSelection = true;
        input.CaptureSelectionGesture();
        ReleaseKey(context, key);
        input.Update(new GameTime());
        Assert.That(context.Component.Gizmo.IsInModalTransform, Is.True);
        Assert.That(input.IsCircleSelecting, Is.False);
        ReleaseKey(context, Keys.D2);
        Assert.That(context.Mesh.VertexArray, Is.Not.EqualTo(baseline));
        ReleaseKey(context, Keys.Enter);
        input.Update(new GameTime());
        Assert.That(input.Settings.IsCircleSelection, Is.True);
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(selectionCount));
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        context.CommandExecutor.Undo();
        AssertVerticesNear(context.Mesh.VertexArray, baseline);
        Assert.That(input.Settings.IsCircleSelection, Is.True);
    }

    [Test]
    public void CircleMouse_ShiftBrushKeepsExistingSelectionAndAdds()
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        using var input = CreateSelectionInput(context);
        var vertices = (VertexSelectionState)context.SelectionManager.GetState();
        vertices.ModifySelection([0], false);
        input.Settings.IsCircleSelection = true;
        context.Keyboard.Setup(k => k.IsKeyDown(Keys.LeftShift)).Returns(true);
        context.Mouse.Setup(m => m.Position()).Returns(new Vector2(600, 400));
        context.Mouse.Setup(m => m.GetPressPosition(MouseButton.Left)).Returns(new Vector2(400, 400));
        context.Mouse.Setup(m => m.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
        context.Mouse.Setup(m => m.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
        input.Update(new GameTime());
        Assert.That(context.SelectionManager.GetState<VertexSelectionState>().SelectedVertices, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void CircleMouse_NumpadRadiusAdjustmentDoesNotCaptureOrNavigate()
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        using var input = CreateSelectionInput(context);
        input.Settings.IsCircleSelection = true;
        var view = context.Camera.ViewMatrix;
        var projection = context.Camera.ProjectionMatrix;
        context.Keyboard.Setup(k => k.IsKeyPressed(Keys.Add)).Returns(true);
        input.Update(new GameTime());
        Assert.That(input.CircleRadius, Is.EqualTo(29));
        context.Keyboard.Setup(k => k.IsKeyPressed(Keys.Add)).Returns(false);
        context.Keyboard.Setup(k => k.IsKeyPressed(Keys.Subtract)).Returns(true);
        input.Update(new GameTime());
        Assert.That(input.CircleRadius, Is.EqualTo(25));
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        Assert.That(context.Camera.ViewMatrix, Is.EqualTo(view));
        Assert.That(context.Camera.ProjectionMatrix, Is.EqualTo(projection));
    }

    private static void SelectCircleTestElements(ComponentContext context)
    {
        switch (context.SelectionManager.GetState())
        {
            case VertexSelectionState vertices:
                vertices.ModifySelection([0, 1, 2, 3, 4, 5], false);
                break;
            case EdgeSelectionState edges:
                edges.ModifySelection([(0, 1), (1, 2), (0, 2), (3, 4), (4, 5), (3, 5)], false);
                break;
            case FaceSelectionState faces:
                faces.ModifySelection([0, 3], false);
                break;
        }
    }
}
