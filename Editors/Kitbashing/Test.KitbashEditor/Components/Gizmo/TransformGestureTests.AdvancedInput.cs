using GameWorld.Core.Components.Gizmo;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Selection;

namespace Testing.GameWorld.Core.Components.Gizmo;

public partial class TransformGestureTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void AdvancedSelection_StationaryClickBetweenFramesPaintsOrErases(bool remove)
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        var button = MouseButton.Left;
        context.Keyboard.Setup(k => k.IsKeyDown(Keys.LeftControl)).Returns(remove);
        if (remove)
            ((VertexSelectionState)context.SelectionManager.GetState()).ModifySelection(new[] { 0 }, false);
        var input = CreateSelectionInput(context);
        input.Settings.IsCircleSelection = true;
        var point = new Vector2(400, 400);
        context.Mouse.Setup(m => m.Position()).Returns(point);
        context.Mouse.Setup(m => m.GetPressPosition(button)).Returns(point);
        context.Mouse.Setup(m => m.IsMouseButtonPressed(button)).Returns(true);
        context.Mouse.Setup(m => m.IsMouseButtonReleased(button)).Returns(true);
        input.CaptureSelectionGesture();
        input.Update(new GameTime());
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(remove ? 0 : 1));
        Assert.That(input.IsCircleSelecting, Is.False);
        Assert.That(input.Settings.IsCircleSelection, Is.True);
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        context.CommandExecutor.Undo();
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(remove ? 1 : 0));
    }

    [Test]
    public void BlenderInput_PastedFunctionStartsExpressionInput()
    {
        using var context = CreateBlenderInputContext();
        var baseline = context.Mesh.VertexArray.ToArray();
        ReleaseKey(context, Keys.G);
        context.Keyboard.Setup(k => k.IsKeyDownOrReleased(Keys.LeftControl)).Returns(true);
        context.Keyboard.Setup(k => k.IsKeyPressed(Keys.V)).Returns(true);
        TypeTransformText(context, "sin(pi/2)/2");
        Assert.That(context.Component.Gizmo.IsInNumericInput, Is.True);
        Assert.That(context.Mesh.VertexArray[0].Position.X - baseline[0].Position.X, Is.EqualTo(0.5f).Within(0.00001f));
        context.Keyboard.Setup(k => k.IsKeyDownOrReleased(Keys.LeftControl)).Returns(false);
        context.Keyboard.Setup(k => k.IsKeyPressed(Keys.V)).Returns(false);
        ReleaseKey(context, Keys.Escape);
        Assert.That(context.Mesh.VertexArray, Is.EqualTo(baseline));
    }

    [Test]
    public void BlenderInput_DoubleRTappedBetweenFramesStartsTrackball()
    {
        using var context = CreateBlenderInputContext();
        context.Keyboard.Setup(k => k.GetKeyPressCount(Keys.R)).Returns(2);
        ReleaseKey(context, Keys.R);
        Assert.That(context.Component.Gizmo.IsTrackballRotation, Is.True);
        ReleaseKey(context, Keys.Escape);
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AdvancedSelection_FastCircleStrokeIncludesItsReleasePosition(bool completedBetweenFrames)
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        var input = CreateSelectionInput(context);
        input.StartCircleSelection();
        context.Mouse.Setup(m => m.GetPressPosition(MouseButton.Left)).Returns(new Vector2(400, 400));
        context.Mouse.Setup(m => m.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
        if (!completedBetweenFrames)
        {
            context.Mouse.Setup(m => m.Position()).Returns(new Vector2(400, 400));
            input.Update(new GameTime());
            context.Mouse.Setup(m => m.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
        }
        context.Mouse.Setup(m => m.Position()).Returns(new Vector2(600, 400));
        context.Mouse.Setup(m => m.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
        input.Update(new GameTime());
        Assert.That(((VertexSelectionState)context.SelectionManager.GetState()).SelectedVertices, Is.EqualTo(new[] { 0, 1 }));
        input.FinishCircleSelection();
        context.CommandExecutor.Undo();
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AdvancedSelection_CircleWheelZoomsViewWithoutChangingRadius(bool orthographic)
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        var input = CreateSelectionInput(context);
        input.Settings.IsCircleSelection = true;
        context.Camera.CurrentProjectionType = orthographic
            ? global::GameWorld.Core.Components.Rendering.ProjectionType.Orthographic
            : global::GameWorld.Core.Components.Rendering.ProjectionType.Perspective;
        var zoom = orthographic ? context.Camera.OrthoSize : context.Camera.Zoom;
        context.Mouse.Setup(m => m.DeletaScrollWheel()).Returns(240);
        input.CaptureSelectionGesture();
        context.Camera.Update(new GameTime());
        input.Update(new GameTime());
        Assert.That(input.CircleRadius, Is.EqualTo(25));
        Assert.That(orthographic ? context.Camera.OrthoSize : context.Camera.Zoom, Is.EqualTo(zoom * 1.44f).Within(0.0001f));
        Assert.That(input.IsCircleSelecting, Is.False);
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
    }

    [TestCase(Keys.None)]
    [TestCase(Keys.LeftShift)]
    [TestCase(Keys.LeftControl)]
    public void AdvancedSelection_CircleMiddleNavigationMatchesNormalMouse(Keys modifier)
    {
        using var normal = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        using var circle = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        using var input = CreateSelectionInput(circle);
        input.Settings.IsCircleSelection = true;
        foreach (var context in new[] { normal, circle })
        {
            context.Keyboard.Setup(k => k.IsKeyDown(modifier)).Returns(true);
            context.Mouse.Setup(m => m.Position()).Returns(new Vector2(440, 420));
            context.Mouse.Setup(m => m.GetPressPosition(MouseButton.Middle)).Returns(new Vector2(400, 400));
            context.Mouse.Setup(m => m.IsMouseButtonPressed(MouseButton.Middle)).Returns(true);
            context.Mouse.Setup(m => m.IsMouseButtonDown(MouseButton.Middle)).Returns(true);
        }
        normal.Camera.Update(new GameTime());
        input.CaptureSelectionGesture();
        circle.Camera.Update(new GameTime());
        circle.Component.Update(new GameTime());
        input.Update(new GameTime());
        Assert.Multiple(() =>
        {
            Assert.That(circle.Camera.Yaw, Is.EqualTo(normal.Camera.Yaw));
            Assert.That(circle.Camera.Pitch, Is.EqualTo(normal.Camera.Pitch));
            Assert.That(circle.Camera.LookAt, Is.EqualTo(normal.Camera.LookAt));
            Assert.That(circle.Camera.OrthoSize, Is.EqualTo(normal.Camera.OrthoSize));
            Assert.That(circle.Camera.Zoom, Is.EqualTo(normal.Camera.Zoom));
            Assert.That(circle.Mouse.Object.MouseOwner, Is.SameAs(circle.Camera));
            Assert.That(input.IsCircleSelecting, Is.False);
            Assert.That(input.Settings.IsCircleSelection, Is.True);
            Assert.That(circle.CommandExecutor.CanUndo(), Is.False);
        });
    }

    [TestCase(GeometrySelectionMode.Vertex, 6)]
    [TestCase(GeometrySelectionMode.Edge, 6)]
    [TestCase(GeometrySelectionMode.Face, 2)]
    public void AdvancedSelection_XrayBoxIncludesBackLayerWithoutEditingDocument(GeometrySelectionMode mode, int count)
    {
        using var context = CreateOccludedEditContext(mode);
        var input = CreateSelectionInput(context);
        input.Settings.IsXRay = true;
        var document = context.CommandExecutor.CurrentDocumentStateId;
        ClickOrBox(context, input, new Vector2(380, 380), new Vector2(620, 620));
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(count));
        Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(document));
    }

    [TestCase(false, 1)]
    [TestCase(true, 2)]
    public void AdvancedSelection_CirclePaintEraseAndExitIsOneSelectionUndo(bool xray, int expected)
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        var input = CreateSelectionInput(context);
        input.Settings.IsXRay = xray;
        var document = context.CommandExecutor.CurrentDocumentStateId;
        Assert.That(input.StartCircleSelection(), Is.True);
        context.Mouse.Setup(m => m.Position()).Returns(new Vector2(400, 400));
        context.Mouse.Setup(m => m.IsMouseButtonDown(MouseButton.Left)).Returns(true);
        input.Update(new GameTime());
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(expected));
        context.Keyboard.Setup(k => k.IsKeyDown(Keys.LeftControl)).Returns(true);
        input.Update(new GameTime());
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero);
        context.Keyboard.Setup(k => k.IsKeyDown(Keys.LeftControl)).Returns(false);
        input.Update(new GameTime());
        context.Keyboard.Setup(k => k.IsKeyReleased(Keys.Escape)).Returns(true);
        input.Update(new GameTime());
        Assert.That(input.IsCircleSelecting, Is.False);
        Assert.That(input.Settings.IsCircleSelection, Is.True);
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(expected));
        Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(document));
        context.CommandExecutor.Undo();
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero);
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
        context.CommandExecutor.Redo();
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(expected));
    }

    [Test]
    public void AdvancedSelection_AltClickDoesNotClaimInputOrExpandSelection()
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Edge);
        var input = CreateSelectionInput(context);
        context.Keyboard.Setup(k => k.IsKeyDownOrReleased(Keys.LeftAlt)).Returns(true);
        context.Mouse.Object.MouseOwner = context.Camera;
        for (var click = 0; click < 3; click++)
        {
            context.Mouse.Setup(m => m.Position()).Returns(new Vector2(500, 400));
            context.Mouse.Setup(m => m.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
            context.Mouse.Setup(m => m.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            input.CaptureSelectionGesture();
            Assert.That(context.Mouse.Object.MouseOwner, Is.SameAs(context.Camera));
            input.Update(new GameTime());
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AdvancedSelection_AltZDoesNotChangeXray(bool xray)
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        using var input = CreateSelectionInput(context);
        input.Settings.IsXRay = xray;
        context.Keyboard.Setup(k => k.IsKeyDownOrReleased(Keys.LeftAlt)).Returns(true);
        context.Keyboard.Setup(k => k.IsKeyReleased(Keys.Z)).Returns(true);
        input.CaptureSelectionGesture();
        input.Update(new GameTime());
        Assert.That(input.Settings.IsXRay, Is.EqualTo(xray));
    }

    [Test]
    public void AdvancedSelection_WAndIconToggleTheSameIdleToolWithoutCapturingMouse()
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        using var input = CreateSelectionInput(context);
        context.Keyboard.Setup(k => k.IsKeyPressed(Keys.W)).Returns(true);
        input.CaptureSelectionGesture();
        input.Update(new GameTime());
        Assert.That(input.Settings.IsCircleSelection, Is.True);
        Assert.That(input.IsCircleSelecting, Is.False);
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        context.Mouse.Verify(m => m.BeginContinuousDrag(It.IsAny<bool>()), Times.Never);
        input.Settings.IsCircleSelection = false;
        input.CaptureSelectionGesture();
        Assert.That(input.Settings.IsCircleSelection, Is.True);
        input.CaptureSelectionGesture();
        Assert.That(input.Settings.IsCircleSelection, Is.False);
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
    }

    [TestCase(Keys.LeftAlt)]
    [TestCase(Keys.LeftControl)]
    public void AdvancedSelection_ModifiedWDoesNotActivateCircle(Keys modifier)
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        using var input = CreateSelectionInput(context);
        context.Keyboard.Setup(k => k.IsKeyDownOrReleased(modifier)).Returns(true);
        context.Keyboard.Setup(k => k.IsKeyPressed(Keys.W)).Returns(true);
        input.CaptureSelectionGesture();
        Assert.That(input.Settings.IsCircleSelection, Is.False);
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AdvancedSelection_CDoesNotToggleCircle(bool circle)
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        using var input = CreateSelectionInput(context);
        input.Settings.IsCircleSelection = circle;
        context.Keyboard.Setup(k => k.IsKeyPressed(Keys.C)).Returns(true);
        input.CaptureSelectionGesture();
        input.Update(new GameTime());
        Assert.That(input.Settings.IsCircleSelection, Is.EqualTo(circle));
    }

    [Test]
    public void AdvancedSelection_CircleStrokesReleaseMouseAndUndoIndependently()
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        using var input = CreateSelectionInput(context);
        input.Settings.IsCircleSelection = true;
        var document = context.CommandExecutor.CurrentDocumentStateId;
        for (var stroke = 0; stroke < 2; stroke++)
        {
            var point = new Vector2(400 + 200 * stroke, 400);
            context.Mouse.Setup(m => m.Position()).Returns(point);
            context.Mouse.Setup(m => m.GetPressPosition(MouseButton.Left)).Returns(point);
            context.Mouse.Setup(m => m.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
            context.Mouse.Setup(m => m.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            input.CaptureSelectionGesture();
            input.Update(new GameTime());
            Assert.That(input.IsCircleSelecting, Is.False);
            Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
            Assert.That(input.Settings.IsCircleSelection, Is.True);
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(stroke + 1));
        }
        input.Settings.IsCircleSelection = false;
        Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(document));
        context.CommandExecutor.Undo();
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(1));
        context.CommandExecutor.Undo();
        Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero);
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
    }

    [Test]
    public void AdvancedSelection_CircleCaptureLossKeepsPaintAndReleasesInput()
    {
        using var context = CreateOccludedEditContext(GeometrySelectionMode.Vertex);
        var input = CreateSelectionInput(context);
        input.StartCircleSelection();
        context.Mouse.Setup(m => m.Position()).Returns(new Vector2(400, 400));
        context.Mouse.Setup(m => m.IsMouseButtonDown(MouseButton.Left)).Returns(true);
        input.Update(new GameTime());
        context.Mouse.Raise(m => m.CaptureInterrupted += null);
        Assert.That(input.IsCircleSelecting, Is.False);
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        Assert.That(context.CommandExecutor.CanUndo(), Is.True);
    }

    [Test]
    public void BlenderInput_ExpressionAndTabComponentsPreviewAndUndo()
    {
        using var context = CreateBlenderInputContext();
        var original = context.Mesh.VertexArray.ToArray();
        ModalPreviewReplacement preview = default;
        context.Component.Gizmo.ReplacePreviewFromInitialRequested += value => preview = value;
        ReleaseKey(context, Keys.G);
        TypeTransformText(context, "=(2+3)*4");
        ReleaseKey(context, Keys.Tab);
        TypeTransformText(context, "sin(pi/2)*3");
        ReleaseKey(context, Keys.Tab);
        TypeTransformText(context, "-2");
        Assert.That(preview.VectorValue, Is.EqualTo(new Vector3(20, 3, -2)));
        ReleaseKey(context, Keys.Enter);
        context.CommandExecutor.Undo();
        AssertVerticesNear(context.Mesh.VertexArray, original);
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
    }

    [Test]
    public void BlenderInput_InvalidExpressionCannotCommitAndCanBeCorrected()
    {
        using var context = CreateBlenderInputContext();
        ReleaseKey(context, Keys.G);
        TypeTransformText(context, "=2");
        var valid = context.Mesh.VertexArray.ToArray();
        TypeTransformText(context, "/0");
        ReleaseKey(context, Keys.Enter);
        Assert.That(context.Component.Gizmo.IsInModalTransform, Is.True);
        Assert.That(context.Mesh.VertexArray, Is.EqualTo(valid));
        ReleaseKey(context, Keys.Back);
        TypeTransformText(context, "2");
        ReleaseKey(context, Keys.Enter);
        Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
        Assert.That(context.CommandExecutor.CanUndo(), Is.True);
    }

    [Test]
    public void BlenderInput_TrackballAcceptsTwoNumericAngles()
    {
        using var context = CreateBlenderInputContext();
        SetBlenderTestView(context);
        ModalPreviewReplacement preview = default;
        context.Component.Gizmo.ReplacePreviewFromInitialRequested += value => preview = value;
        ReleaseKey(context, Keys.R);
        ReleaseKey(context, Keys.R);
        TypeTransformText(context, "=90/2");
        ReleaseKey(context, Keys.Tab);
        TypeTransformText(context, "30");
        var axis = new Vector3(MathHelper.PiOver4, MathHelper.Pi / 6, 0);
        AssertMatrixNear(preview.RotationValue, Matrix.CreateFromAxisAngle(Vector3.Normalize(axis), axis.Length()));
        ReleaseKey(context, Keys.Escape);
    }

    private static void TypeTransformText(ComponentContext context, string text)
    {
        context.Keyboard.SetupGet(keyboard => keyboard.TextInput).Returns(text);
        context.Component.Update(new GameTime());
        context.Keyboard.SetupGet(keyboard => keyboard.TextInput).Returns("");
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void BlenderInput_DoubleR_UsesViewTrackballAndRestoresOnCancel(bool orthographic, bool mirrored)
    {
        using var context = CreateBlenderInputContext();
        SetBlenderTestView(context, orthographic: orthographic);
        if (mirrored)
            context.Camera.ProjectionMatrixOverride *= Matrix.CreateScale(-1, 1, 1);
        MoveBlenderPointer(context, new Vector2(500));
        var original = context.Mesh.VertexArray.ToArray();
        ModalPreviewReplacement preview = default;
        context.Component.Gizmo.ReplacePreviewFromInitialRequested += value => preview = value;
        ReleaseKey(context, Keys.R);
        ReleaseKey(context, Keys.R);
        MoveBlenderPointer(context, new Vector2(620, 570));
        var rotationVector = new Vector3(0.7f, mirrored ? -1.2f : 1.2f, 0);
        AssertMatrixNear(preview.RotationValue,
            Matrix.CreateFromAxisAngle(Vector3.Normalize(rotationVector), rotationVector.Length()));
        ReleaseKey(context, Keys.Escape);
        Assert.That(context.Mesh.VertexArray, Is.EqualTo(original));
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
        Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
    }

    [Test]
    public void BlenderInput_DoubleR_ContinuousDisplacementConfirmsOneUndoStep()
    {
        using var context = CreateBlenderInputContext();
        SetBlenderTestView(context);
        MoveBlenderPointer(context, new Vector2(500));
        var original = context.Mesh.VertexArray.ToArray();
        ReleaseKey(context, Keys.R);
        ReleaseKey(context, Keys.R);
        MoveBlenderPointer(context, new Vector2(2600, 700));
        var transformed = context.Mesh.VertexArray.ToArray();
        Assert.That(transformed, Is.Not.EqualTo(original));
        ReleaseKey(context, Keys.Enter);
        Assert.That(context.Mesh.VertexArray, Is.EqualTo(transformed));
        context.CommandExecutor.Undo();
        AssertVerticesNear(context.Mesh.VertexArray, original);
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
        context.CommandExecutor.Redo();
        AssertVerticesNear(context.Mesh.VertexArray, transformed);
    }

    private static void AssertVerticesNear(global::GameWorld.Core.Rendering.VertexPositionNormalTextureCustom[] actual,
        global::GameWorld.Core.Rendering.VertexPositionNormalTextureCustom[] expected)
    {
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (var i = 0; i < actual.Length; i++)
        {
            Assert.That(Vector4.Distance(actual[i].Position, expected[i].Position), Is.LessThan(0.00001), $"position {i}");
            Assert.That(Vector3.Distance(actual[i].Normal, expected[i].Normal), Is.LessThan(0.00001), $"normal {i}");
            Assert.That(Vector3.Distance(actual[i].Tangent, expected[i].Tangent), Is.LessThan(0.00001), $"tangent {i}");
            Assert.That(Vector3.Distance(actual[i].BiNormal, expected[i].BiNormal), Is.LessThan(0.00001), $"binormal {i}");
        }
    }
}
