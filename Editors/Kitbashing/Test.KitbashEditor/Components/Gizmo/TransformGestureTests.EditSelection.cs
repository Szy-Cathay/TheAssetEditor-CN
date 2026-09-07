using System.Reflection;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace Testing.GameWorld.Core.Components.Gizmo
{
    public partial class TransformGestureTests
    {
        [TestCase(GeometrySelectionMode.Vertex)]
        [TestCase(GeometrySelectionMode.Edge)]
        [TestCase(GeometrySelectionMode.Face)]
        public void EditSelection_BoxExcludesHiddenGeometryAndMovesOnlyVisibleSelection(GeometrySelectionMode mode)
        {
            using var context = CreateOccludedEditContext(mode);
            var input = CreateSelectionInput(context);
            var original = context.Mesh.VertexArray.ToArray();
            var documentState = context.CommandExecutor.CurrentDocumentStateId;
            ClickOrBox(context, input, new Vector2(380, 380), new Vector2(620, 620));
            var selection = context.SelectionManager.GetState();
            Assert.That(selection.SelectionCount(), Is.EqualTo(mode == GeometrySelectionMode.Face ? 1 : 3));
            Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(documentState));
            var expectedPivot = Enumerable.Range(0, 3).Select(context.Mesh.GetVertexById).Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex) / 3;
            Assert.That(Vector3.Distance(context.Component.Gizmo.Selection.Single().Position, expectedPivot), Is.LessThan(0.0001));

            ReleaseKey(context, Keys.G);
            ReleaseKey(context, Keys.Y);
            ReleaseKey(context, Keys.D1);
            for (var index = 0; index < original.Length; index++)
            {
                var expected = original[index].Position + (index < 3 ? new Vector4(0, 1, 0, 0) : Vector4.Zero);
                Assert.That(Vector4.Distance(context.Mesh.VertexArray[index].Position, expected), Is.LessThan(0.0001));
            }
            ReleaseKey(context, Keys.Enter);
            context.CommandExecutor.Undo();
            Assert.That(context.Mesh.VertexArray, Is.EqualTo(original));
            Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(documentState));
        }

        [TestCase(GeometrySelectionMode.Vertex, 400, 400)]
        [TestCase(GeometrySelectionMode.Edge, 500, 400)]
        [TestCase(GeometrySelectionMode.Face, 500, 480)]
        public void EditSelection_ShiftClickTogglesSelectedElementAndUndoRestoresIt(GeometrySelectionMode mode, int x, int y)
        {
            using var context = CreateOccludedEditContext(mode);
            var input = CreateSelectionInput(context);
            var point = new Vector2(x, y);
            ClickOrBox(context, input, point, point);
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(1));
            var documentState = context.CommandExecutor.CurrentDocumentStateId;
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.LeftShift)).Returns(true);
            ClickOrBox(context, input, point, point);
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero);
            Assert.That(context.Component.Gizmo.Selection, Is.Empty);
            context.CommandExecutor.Undo();
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(1));
            Assert.That(context.Component.Gizmo.Selection, Has.Count.EqualTo(1));
            Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(documentState));
        }

        [Test, Combinatorial]
        public void EditSelection_GizmoHitMatchesDisplayedAxisAndPlane(
            [Values(GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z, GizmoAxis.XY, GizmoAxis.XZ, GizmoAxis.YZ)] GizmoAxis axis,
            [Values(false, true)] bool orthographic,
            [Values(TransformSpace.World, TransformSpace.Local)] TransformSpace space)
        {
            using var context = CreateBlenderInputContext();
            SetBlenderTestView(context, orthographic: orthographic);
            context.Camera.Yaw = MathHelper.ToRadians(35);
            context.Camera.Pitch = MathHelper.ToRadians(-20);
            context.Component.Gizmo.Selection.Single().Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f);
            context.Component.SetGizmoMode(GizmoMode.Translate);
            context.Component.Gizmo.GizmoDisplaySpace = space;
            context.Component.Update(new GameTime());
            var gizmoWorld = (Matrix)typeof(global::GameWorld.Core.Components.Gizmo.Gizmo)
                .GetField("_gizmoWorld", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context.Component.Gizmo);
            var point = axis switch
            {
                GizmoAxis.X => new Vector3(2, 0, 0),
                GizmoAxis.Y => new Vector3(0, 2, 0),
                GizmoAxis.Z => new Vector3(0, 0, 2),
                GizmoAxis.XY => new Vector3(0.65f, 0.65f, 0),
                GizmoAxis.XZ => new Vector3(0.65f, 0, 0.65f),
                _ => new Vector3(0, 0.65f, 0.65f)
            };
            var screen = context.Camera.InputViewport.Project(point, context.Camera.ProjectionMatrix, context.Camera.ViewMatrix, gizmoWorld);
            context.Component.Gizmo.SelectAxis(new Vector2(screen.X, screen.Y));
            Assert.That(context.Component.Gizmo.ActiveAxis, Is.EqualTo(axis));
            var original = context.Mesh.VertexArray.ToArray();
            var localDelta = axis switch
            {
                GizmoAxis.X => new Vector3(2, 0, 0),
                GizmoAxis.Y => new Vector3(0, 2, 0),
                GizmoAxis.Z => new Vector3(0, 0, 2),
                GizmoAxis.XY => new Vector3(2, 2, 0),
                GizmoAxis.XZ => new Vector3(2, 0, 2),
                _ => new Vector3(0, 2, 2)
            };
            var expectedDelta = space == TransformSpace.Local
                ? Vector3.Transform(localDelta, context.Component.Gizmo.Selection.Single().Orientation)
                : localDelta;
            context.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(screen.X, screen.Y));
            context.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
            context.Component.Update(new GameTime());
            context.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            Assert.That(context.Component.Gizmo.IsInModalTransform, Is.True);
            Assert.That(context.Component.Gizmo.ActiveAxis, Is.EqualTo(axis));
            ReleaseKey(context, Keys.D2);
            for (var i = 0; i < original.Length; i++)
                Assert.That(Vector4.Distance(context.Mesh.VertexArray[i].Position, original[i].Position + new Vector4(expectedDelta, 0)), Is.LessThan(0.0001));
            context.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            context.Component.Update(new GameTime());
            Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
            context.CommandExecutor.Undo();
            for (var i = 0; i < original.Length; i++)
                Assert.That(Vector4.Distance(context.Mesh.VertexArray[i].Position, original[i].Position), Is.LessThan(0.00001));
        }

        [Test, Combinatorial]
        public void EditSelection_SelectionCenterPivotMatchesGizmoDuringRotationAndScale(
            [Values(GeometrySelectionMode.Vertex, GeometrySelectionMode.Edge, GeometrySelectionMode.Face)] GeometrySelectionMode mode,
            [Values(Keys.R, Keys.S)] Keys key)
        {
            using var context = CreateOccludedEditContext(mode);
            var input = CreateSelectionInput(context);
            ClickOrBox(context, input, new Vector2(380, 380), new Vector2(620, 620));
            var original = context.Mesh.VertexArray.ToArray();
            var pivot = context.Component.Gizmo.Selection.Single().Position;
            context.Component.Gizmo.ActivePivot = PivotType.SelectionCenter;
            ReleaseKey(context, key);
            if (key == Keys.R)
            {
                ReleaseKey(context, Keys.Z);
                ReleaseKey(context, Keys.D9);
                ReleaseKey(context, Keys.D0);
            }
            else
                ReleaseKey(context, Keys.D2);
            var transform = key == Keys.R ? Matrix.CreateRotationZ(MathHelper.PiOver2) : Matrix.CreateScale(2);
            for (var i = 0; i < original.Length; i++)
            {
                var initial = new Vector3(original[i].Position.X, original[i].Position.Y, original[i].Position.Z);
                var expected = i < 3 ? pivot + Vector3.Transform(initial - pivot, transform) : initial;
                Assert.That(Vector4.Distance(context.Mesh.VertexArray[i].Position, new Vector4(expected, 1)), Is.LessThan(0.001));
            }
            ReleaseKey(context, Keys.Enter);
            context.CommandExecutor.Undo();
            for (var i = 0; i < original.Length; i++)
                Assert.That(Vector4.Distance(context.Mesh.VertexArray[i].Position, original[i].Position), Is.LessThan(0.00001));
        }

        [TestCase(GizmoAxis.XY)]
        [TestCase(GizmoAxis.XZ)]
        [TestCase(GizmoAxis.YZ)]
        public void EditSelection_SharedLegacyPlaneDragKeepsConstraint(GizmoAxis axis)
        {
            using var context = CreateBlenderInputContext();
            SetBlenderTestView(context);
            context.Camera.Yaw = MathHelper.ToRadians(35);
            context.Camera.Pitch = MathHelper.ToRadians(-20);
            context.Component.SetGizmoMode(GizmoMode.Translate);
            context.Component.Gizmo.Update(new GameTime(), true);
            var original = context.Mesh.VertexArray.ToArray();
            context.Component.Gizmo.ActiveAxis = axis;
            context.Mouse.Setup(mouse => mouse.IsMouseButtonDown(MouseButton.Left)).Returns(true);
            context.Component.Gizmo.Update(new GameTime(), true);
            context.Mouse.Setup(mouse => mouse.LastState()).Returns(new MouseState(500, 500, 0,
                ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
            context.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(520, 460));
            context.Component.Gizmo.Update(new GameTime(), true);
            var delta = context.Mesh.VertexArray[0].Position - original[0].Position;
            Assert.That(delta.Length(), Is.GreaterThan(0.001));
            Assert.That(axis == GizmoAxis.XY ? delta.Z : axis == GizmoAxis.XZ ? delta.Y : delta.X, Is.EqualTo(0).Within(0.00001));
        }

        private static ComponentContext CreateOccludedEditContext(GeometrySelectionMode mode)
        {
            var context = CreateBlenderInputContext();
            var viewport = SetBlenderTestView(context);
            Vector3[] screenPositions =
            [
                new(400, 400, 0.2f), new(600, 400, 0.2f), new(500, 600, 0.2f),
                new(400, 400, 0.8f), new(600, 400, 0.8f), new(500, 600, 0.8f)
            ];
            context.Mesh.VertexArray = screenPositions.Select(position => CreateVertex(viewport.Unproject(
                position, context.Camera.ProjectionMatrix, context.Camera.ViewMatrix, Matrix.Identity))).ToArray();
            context.Mesh.IndexArray = [0, 1, 2, 3, 4, 5];
            context.Mesh.BuildBoundingBox();
            var node = new Rmv2MeshNode(context.Mesh, new Mock<IRmvMaterial>().Object, null, null);
            ISelectionState selection = mode switch
            {
                GeometrySelectionMode.Vertex => new VertexSelectionState(node, 0),
                GeometrySelectionMode.Edge => new EdgeSelectionState { RenderObject = node },
                _ => new FaceSelectionState { RenderObject = node }
            };
            context.SelectionManager.SetState(selection);
            return context;
        }

        private static void ClickOrBox(ComponentContext context, Editors.KitbasherEditor.Components.KitbashSelectionInputComponent input, Vector2 start, Vector2 end)
        {
            context.Mouse.Setup(mouse => mouse.IsMouseOwner(input)).Returns(true);
            context.Mouse.Setup(mouse => mouse.GetPressPosition(MouseButton.Left)).Returns(start);
            context.Mouse.Setup(mouse => mouse.Position()).Returns(end);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            input.Update(new GameTime());
            context.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(false);
        }
    }
}
