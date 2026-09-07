using Editors.KitbasherEditor.Components;
using GameWorld.Core.Commands;
using GameWorld.Core.Animation;
using GameWorld.Core.Commands.Edge;
using GameWorld.Core.Commands.Face;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace Testing.GameWorld.Core.Components.Gizmo
{
    public partial class TransformGestureTests
    {
        [Test]
        public void BlenderInput_ReleasingStartKeyAfterConfirmationDoesNotRestartGesture()
        {
            using var context = CreateBlenderInputContext();
            context.Keyboard.Setup(keyboard => keyboard.IsKeyPressed(Keys.S)).Returns(true);
            context.Component.Update(new GameTime());
            context.Keyboard.Setup(keyboard => keyboard.IsKeyPressed(Keys.S)).Returns(false);
            ReleaseKey(context, Keys.D2);
            ReleaseKey(context, Keys.Enter);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(Keys.S)).Returns(true);
            context.Component.Update(new GameTime());
            Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
        }

        [TestCase(Keys.G)]
        [TestCase(Keys.R)]
        [TestCase(Keys.S)]
        public void BlenderInput_MovementBeforeFirstFrameUsesKeyPressOrigin(Keys mode)
        {
            using var immediate = CreateBlenderInputContext();
            using var delayed = CreateBlenderInputContext();
            SetBlenderTestView(immediate);
            SetBlenderTestView(delayed);
            var start = new Vector2(700, 500);
            var end = new Vector2(600, 700);
            MoveBlenderPointer(immediate, start);
            ReleaseKey(immediate, mode);
            MoveBlenderPointer(immediate, end);
            delayed.Keyboard.Setup(keyboard => keyboard.GetKeyPressPosition(mode)).Returns(start);
            delayed.Mouse.Setup(mouse => mouse.Position()).Returns(end);
            ReleaseKey(delayed, mode);
            Assert.That(delayed.Mesh.VertexArray, Is.EqualTo(immediate.Mesh.VertexArray));
        }

        [TestCase(false, 1000, 800)]
        [TestCase(false, 1600, 900)]
        [TestCase(true, 1000, 800)]
        [TestCase(true, 1600, 900)]
        public void BlenderInput_GrabFollowsPointerInBothProjections(bool orthographic, int width, int height)
        {
            using var context = CreateBlenderInputContext();
            var viewport = SetBlenderTestView(context, width, height, orthographic);
            var pivot = context.Component.Gizmo.Selection.Single().GetObjectCentre();
            ModalPreviewReplacement preview = default;
            context.Component.Gizmo.ReplacePreviewFromInitialRequested += value => preview = value;
            ReleaseKey(context, Keys.G);
            MoveBlenderPointer(context, new Vector2(width / 2 + 137, height / 2 - 61));
            var projected = viewport.Project(pivot + preview.VectorValue, context.Camera.ProjectionMatrix, context.Camera.ViewMatrix, Matrix.Identity);
            Assert.Multiple(() =>
            {
                Assert.That(projected.X, Is.EqualTo(width / 2 + 137).Within(0.01));
                Assert.That(projected.Y, Is.EqualTo(height / 2 - 61).Within(0.01));
            });
        }

        [TestCase(GizmoAxis.X, 88f, 0f)]
        [TestCase(GizmoAxis.Z, 2f, 0f)]
        [TestCase(GizmoAxis.Y, 0f, 88f)]
        public void BlenderInput_ViewAlignedAxis_HorizontalWrapDoesNotMoveMeshIntoDepth(
            GizmoAxis axis, float yaw, float pitch)
        {
            using var context = CreateBlenderInputContext();
            SetBlenderTestView(context, orthographic: false);
            context.Camera.Yaw = MathHelper.ToRadians(yaw);
            context.Camera.Pitch = MathHelper.ToRadians(pitch);
            var original = context.Mesh.VertexArray.ToArray();
            ReleaseKey(context, Keys.G);
            context.Component.Gizmo.ActiveAxis = axis;
            foreach (var x in new[] { 510, 997, 1000, 1497, 2497, 500 })
            {
                MoveBlenderPointer(context, new Vector2(x, 500));
                for (var index = 0; index < original.Length; index++)
                    Assert.That(Vector4.Distance(context.Mesh.VertexArray[index].Position, original[index].Position),
                        Is.LessThan(0.001f), $"Horizontal input {x} moved a view-aligned axis into depth.");
            }
        }

        [TestCase(GizmoAxis.X, 88f, 0f)]
        [TestCase(GizmoAxis.Z, 2f, 0f)]
        [TestCase(GizmoAxis.Y, 0f, 88f)]
        public void BlenderInput_ViewAlignedAxis_VerticalDepthControlSurvivesHorizontalWrap(
            GizmoAxis axis, float yaw, float pitch)
        {
            using var context = CreateBlenderInputContext();
            SetBlenderTestView(context, orthographic: false);
            context.Camera.Yaw = MathHelper.ToRadians(yaw);
            context.Camera.Pitch = MathHelper.ToRadians(pitch);
            var original = context.Mesh.VertexArray.ToArray();
            ReleaseKey(context, Keys.G);
            context.Component.Gizmo.ActiveAxis = axis;
            var direction = axis == GizmoAxis.X ? Vector3.UnitX : axis == GizmoAxis.Y ? Vector3.UnitY : Vector3.UnitZ;
            foreach (var y in new[] { 20, -20 })
            {
                var depth = y * context.Camera.PerspectiveViewHeight / 1000;
                var translation = direction * (4 * depth * MathF.Abs(depth));
                foreach (var x in new[] { 520, 997, 1000, 1497 })
                {
                    MoveBlenderPointer(context, new Vector2(x, 500 + y));
                    for (var index = 0; index < original.Length; index++)
                        Assert.That(Vector4.Distance(context.Mesh.VertexArray[index].Position,
                            original[index].Position + new Vector4(translation, 0)), Is.LessThan(0.001f));
                }
            }
        }

        [TestCase(GizmoAxis.X)]
        [TestCase(GizmoAxis.Y)]
        [TestCase(GizmoAxis.Z)]
        [TestCase(GizmoAxis.XY)]
        [TestCase(GizmoAxis.XZ)]
        [TestCase(GizmoAxis.YZ)]
        public void BlenderInput_ConstrainedTranslation_SameDisplacementIgnoresGrabOffset(GizmoAxis axis)
        {
            using var centered = CreateBlenderInputContext();
            using var offset = CreateBlenderInputContext();
            foreach (var context in new[] { centered, offset })
            {
                SetBlenderTestView(context, orthographic: false);
                context.Camera.Yaw = 0.8f;
                context.Camera.Pitch = 0.32f;
            }
            MoveBlenderPointer(offset, new Vector2(650, 400));
            ReleaseKey(centered, Keys.G);
            ReleaseKey(offset, Keys.G);
            centered.Component.Gizmo.ActiveAxis = axis;
            offset.Component.Gizmo.ActiveAxis = axis;
            MoveBlenderPointer(centered, new Vector2(580, 530));
            MoveBlenderPointer(offset, new Vector2(730, 430));
            for (var index = 0; index < centered.Mesh.VertexArray.Length; index++)
                Assert.That(Vector4.Distance(centered.Mesh.VertexArray[index].Position, offset.Mesh.VertexArray[index].Position),
                    Is.LessThan(0.001f), "The same drag must not depend on the grabbed part of the handle.");
        }

        [TestCase(2f)]
        [TestCase(0.5f)]
        [TestCase(-1f)]
        public void BlenderInput_ScaleUsesSignedDistanceFromPivot(float factor)
        {
            using var context = CreateBlenderInputContext();
            SetBlenderTestView(context);
            MoveBlenderPointer(context, new Vector2(700, 500));
            var original = context.Mesh.VertexArray.ToArray();
            var pivot = context.Component.Gizmo.Selection.Single().GetObjectCentre();
            ReleaseKey(context, Keys.S);
            MoveBlenderPointer(context, new Vector2(500 + 200 * factor, 500));
            for (var index = 0; index < original.Length; index++)
            {
                var position = original[index].Position;
                var expected = pivot + (new Vector3(position.X, position.Y, position.Z) - pivot) * factor;
                Assert.That(Vector4.Distance(context.Mesh.VertexArray[index].Position, new Vector4(expected, position.W)), Is.LessThan(0.0001));
            }
            ReleaseKey(context, Keys.Escape);
            Assert.That(context.Mesh.VertexArray, Is.EqualTo(original));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BlenderInput_RotationFollowsCircularPointerPathAcrossMultipleTurns(bool mirroredProjection)
        {
            using var context = CreateBlenderInputContext();
            var viewport = SetBlenderTestView(context);
            if (mirroredProjection)
                context.Camera.ProjectionMatrixOverride *= Matrix.CreateScale(-1, 1, 1);
            MoveBlenderPointer(context, new Vector2(700, 500));
            var original = context.Mesh.VertexArray[0].Position;
            var projectedOriginal = viewport.Project(new Vector3(original.X, original.Y, original.Z),
                context.Camera.ProjectionMatrix, context.Camera.ViewMatrix, Matrix.Identity);
            var originalDirection = Vector2.Normalize(new Vector2(projectedOriginal.X - 500, projectedOriginal.Y - 500));
            ReleaseKey(context, Keys.R);
            for (var step = 1; step <= 10; step++)
            {
                var angle = step * MathHelper.PiOver2;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                MoveBlenderPointer(context, new Vector2(500, 500) + direction * 200);
                var position = context.Mesh.VertexArray[0].Position;
                var projected = viewport.Project(new Vector3(position.X, position.Y, position.Z), context.Camera.ProjectionMatrix, context.Camera.ViewMatrix, Matrix.Identity);
                var screenDirection = Vector2.Normalize(new Vector2(projected.X - 500, projected.Y - 500));
                var expectedDirection = Vector2.TransformNormal(originalDirection, Matrix.CreateRotationZ(angle));
                Assert.That(Vector2.Distance(screenDirection, expectedDirection), Is.LessThan(0.0001), $"Quarter turn {step}");
            }
        }

        [Test]
        public void BlenderInput_RotationPrecisionUsesAngleWithoutJumpingWhenShiftChanges()
        {
            using var context = CreateBlenderInputContext();
            SetBlenderTestView(context);
            MoveBlenderPointer(context, new Vector2(700, 500));
            ModalPreviewReplacement preview = default;
            context.Component.Gizmo.ReplacePreviewFromInitialRequested += value => preview = value;
            ReleaseKey(context, Keys.R);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.LeftShift)).Returns(true);
            MoveBlenderPointer(context, new Vector2(500, 700));
            AssertMatrixNear(preview.RotationValue, Matrix.CreateRotationZ(MathHelper.ToRadians(-3)));
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.LeftShift)).Returns(false);
            context.Component.Update(new GameTime());
            AssertMatrixNear(preview.RotationValue, Matrix.CreateRotationZ(MathHelper.ToRadians(-3)));
            MoveBlenderPointer(context, new Vector2(300, 500));
            AssertMatrixNear(preview.RotationValue, Matrix.CreateRotationZ(MathHelper.ToRadians(-93)));
        }

        [Test]
        public void BlenderInput_ScaleWrapKeepsContinuousDistanceAndPrecision()
        {
            using var context = CreateBlenderInputContext();
            SetBlenderTestView(context);
            MoveBlenderPointer(context, new Vector2(700, 500));
            ModalPreviewReplacement preview = default;
            context.Component.Gizmo.ReplacePreviewFromInitialRequested += value => preview = value;
            ReleaseKey(context, Keys.S);
            MoveBlenderPointer(context, new Vector2(990, 500));
            Assert.That(preview.VectorValue.X + 1, Is.EqualTo(2.45f).Within(0.0001));
            context.Mouse.Verify(mouse => mouse.BeginContinuousDrag(true), Times.Once);
            context.Mouse.SetupGet(mouse => mouse.CapturedCursorPosition).Returns(new Vector2(3, 500));
            MoveBlenderPointer(context, new Vector2(990, 500));
            Assert.That(preview.VectorValue.X + 1, Is.EqualTo(2.45f).Within(0.0001));
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.RightShift)).Returns(true);
            MoveBlenderPointer(context, new Vector2(1090, 500));
            Assert.That(preview.VectorValue.X + 1, Is.EqualTo(2.5f).Within(0.0001));
        }

        private static Viewport SetBlenderTestView(ComponentContext context, int width = 1000, int height = 1000, bool orthographic = true)
        {
            context.Camera.LookAt = context.Component.Gizmo.Selection.Single().GetObjectCentre();
            context.Camera.Yaw = 0;
            context.Camera.Pitch = 0;
            context.Camera.ProjectionMatrixOverride = orthographic
                ? Matrix.CreateOrthographic(10f * width / height, 10, 0.1f, 1000)
                : Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, (float)width / height, 0.1f, 1000);
            context.Mouse.Setup(mouse => mouse.GetScreenSize()).Returns(new Vector2(width, height));
            context.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(width / 2, height / 2));
            return new Viewport(0, 0, width, height);
        }

        [Test]
        public void BlenderInput_PositiveZMatchesWorldCoordinatesAndRotationKeepsLocalBasis()
        {
            using var context = CreateBlenderInputContext();
            var wrapper = (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            var original = context.Mesh.VertexArray.ToArray();
            ModalPreviewReplacement preview = default;
            context.Component.Gizmo.ReplacePreviewFromInitialRequested += value => preview = value;
            ReleaseKey(context, Keys.G);
            ReleaseKey(context, Keys.Z);
            ReleaseKey(context, Keys.D2);
            Assert.That(preview.VectorValue, Is.EqualTo(new Vector3(0, 0, 2)));
            ReleaseKey(context, Keys.Escape);
            wrapper.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.PiOver4);
            var basis = Matrix.CreateFromQuaternion(wrapper.Orientation);
            ReleaseKey(context, Keys.R);
            ReleaseKey(context, Keys.Z);
            ReleaseKey(context, Keys.D9);
            ReleaseKey(context, Keys.D0);
            AssertMatrixNear(Matrix.CreateFromQuaternion(wrapper.Orientation), basis * Matrix.CreateRotationZ(MathHelper.PiOver2));
            ReleaseKey(context, Keys.Escape);
            Assert.That(context.Mesh.VertexArray, Is.EqualTo(original));
            AssertMatrixNear(Matrix.CreateFromQuaternion(wrapper.Orientation), basis);
        }

        [Test]
        public void BlenderInput_CaptureLossCancelsPreviewAndDoesNotCreateHistory()
        {
            using var context = CreateBlenderInputContext();
            var original = context.Mesh.VertexArray.ToArray();
            ReleaseKey(context, Keys.G);
            ReleaseKey(context, Keys.D2);
            context.Mouse.Raise(mouse => mouse.CaptureInterrupted += null);
            Assert.That(context.Mesh.VertexArray, Is.EqualTo(original));
            Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
            Assert.That(context.CommandExecutor.CanUndo(), Is.False);
        }

        [TestCase(GizmoMode.Translate, false)]
        [TestCase(GizmoMode.Rotate, false)]
        [TestCase(GizmoMode.NonUniformScale, false)]
        [TestCase(GizmoMode.Translate, true)]
        [TestCase(GizmoMode.Rotate, true)]
        [TestCase(GizmoMode.NonUniformScale, true)]
        public void BlenderInput_ToolbarPointerUsesSamePreviewAndCommitsOnRelease(GizmoMode mode, bool circle)
        {
            using var context = circle ? CreateOccludedEditContext(GeometrySelectionMode.Vertex) : CreateBlenderInputContext();
            if (circle)
                context.SelectionManager.GetState<VertexSelectionState>().ModifySelection([0, 1, 2], false);
            using var input = CreateSelectionInput(context);
            input.Settings.IsCircleSelection = circle;
            SetBlenderTestView(context);
            context.Component.SetGizmoMode(mode);
            context.Component.Update(new GameTime());
            var sphere = (BoundingSphere)typeof(global::GameWorld.Core.Components.Gizmo.Gizmo)
                .GetProperty("XSphere", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(context.Component.Gizmo)!;
            var projected = context.Camera.InputViewport.Project(sphere.Center, context.Camera.ProjectionMatrix, context.Camera.ViewMatrix, Matrix.Identity);
            context.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(projected.X, projected.Y));
            context.Component.Gizmo.ActiveAxis = GizmoAxis.None;
            context.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
            input.CaptureSelectionGesture();
            context.Component.Update(new GameTime());
            input.Update(new GameTime());
            Assert.That(context.Component.Gizmo.IsInModalTransform, Is.True);
            Assert.That(input.IsCircleSelecting, Is.False);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            ReleaseKey(context, Keys.D2);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            context.Component.Update(new GameTime());
            input.Update(new GameTime());
            Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
            Assert.That(input.Settings.IsCircleSelection, Is.EqualTo(circle));
            Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
            Assert.That(context.CommandExecutor.CanUndo(), Is.True);
        }

        private static void MoveBlenderPointer(ComponentContext context, Vector2 position)
        {
            context.Mouse.Setup(mouse => mouse.Position()).Returns(position);
            context.Component.Update(new GameTime());
        }

        [TestCase(Keys.G)]
        [TestCase(Keys.R)]
        [TestCase(Keys.S)]
        public void BlenderInput_NumericPreviewMatchesCommitAndUndo(Keys mode)
        {
            using var context = CreateBlenderInputContext();
            var original = context.Mesh.VertexArray.ToArray();
            ReleaseKey(context, mode);
            ReleaseKey(context, Keys.X);
            ReleaseKey(context, Keys.D2);
            var preview = context.Mesh.VertexArray.ToArray();
            Assert.That(preview, Is.Not.EqualTo(original), "The number must preview before Enter.");

            ReleaseKey(context, Keys.Enter);
            Assert.That(context.Mesh.VertexArray, Is.EqualTo(preview));
            Assert.That(context.Component.Gizmo.IsInNumericInput, Is.False);
            context.CommandExecutor.Undo();
            for (var index = 0; index < original.Length; index++)
            {
                Assert.That(Vector4.Distance(context.Mesh.VertexArray[index].Position, original[index].Position), Is.LessThan(0.00001));
                Assert.That(Vector3.Distance(context.Mesh.VertexArray[index].Normal, original[index].Normal), Is.LessThan(0.00001));
            }
            context.CommandExecutor.Redo();
            for (var index = 0; index < preview.Length; index++)
                Assert.That(Vector4.Distance(context.Mesh.VertexArray[index].Position, preview[index].Position), Is.LessThan(0.00001));
        }

        [TestCase(Keys.Escape)]
        [TestCase(Keys.Enter)]
        public void BlenderInput_NextGestureDoesNotReuseNumber(Keys finish)
        {
            using var context = CreateBlenderInputContext();
            ReleaseKey(context, Keys.S);
            ReleaseKey(context, Keys.D2);
            ReleaseKey(context, finish);
            var baseline = context.Mesh.VertexArray.ToArray();
            ReleaseKey(context, Keys.S);
            Assert.Multiple(() =>
            {
                Assert.That(context.Component.Gizmo.IsInNumericInput, Is.False);
                Assert.That(context.Mesh.VertexArray, Is.EqualTo(baseline));
            });
        }

        [TestCase(Keys.G)]
        [TestCase(Keys.R)]
        [TestCase(Keys.S)]
        public void BlenderInput_RepeatedAxisCyclesWorldLocalFree(Keys mode)
        {
            using var context = CreateBlenderInputContext();
            var wrapper = (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            wrapper.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver2);
            ReleaseKey(context, mode);
            ReleaseKey(context, Keys.X);
            Assert.That(Vector3.Distance(context.Component.Gizmo.AxisMatrix.Right, Vector3.UnitX), Is.LessThan(0.0001));
            ReleaseKey(context, Keys.X);
            Assert.Multiple(() =>
            {
                Assert.That(context.Component.Gizmo.ActiveAxis, Is.EqualTo(GizmoAxis.X));
                Assert.That(Vector3.Distance(context.Component.Gizmo.AxisMatrix.Right, Vector3.UnitY), Is.LessThan(0.0001));
            });
            ReleaseKey(context, Keys.X);
            Assert.That(context.Component.Gizmo.ActiveAxis, Is.EqualTo(GizmoAxis.None));
        }

        [Test]
        public void BlenderInput_LocalScaleUsesFrozenOrientation()
        {
            using var context = CreateBlenderInputContext();
            var original = context.Mesh.VertexArray.Select(vertex => vertex.Position).ToArray();
            var wrapper = (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            var pivot = wrapper.GetObjectCentre();
            wrapper.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver2);
            ReleaseKey(context, Keys.S);
            ReleaseKey(context, Keys.X);
            ReleaseKey(context, Keys.X);
            ReleaseKey(context, Keys.D2);
            var expected = original.Select(position => pivot + (new Vector3(position.X, position.Y, position.Z) - pivot) * new Vector3(1, 2, 1)).ToArray();
            for (var index = 0; index < expected.Length; index++)
            {
                var position = context.Mesh.VertexArray[index].Position;
                Assert.That(Vector3.Distance(new Vector3(position.X, position.Y, position.Z), expected[index]), Is.LessThan(0.0001));
            }
            ReleaseKey(context, Keys.Enter);
            context.CommandExecutor.Undo();
            Assert.That(context.Mesh.VertexArray.Select(vertex => vertex.Position), Is.EqualTo(original));
        }

        [TestCase(Keys.R, 13, 0)]
        [TestCase(Keys.S, 0, 3)]
        public void BlenderInput_CtrlSnapsSmallMouseMovementToBaseline(Keys mode, int x, int y)
        {
            using var context = CreateBlenderInputContext();
            var original = context.Mesh.VertexArray.ToArray();
            ReleaseKey(context, mode);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.LeftControl)).Returns(true);
            context.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(400 + x, 400 + y));
            context.Component.Update(new GameTime());
            Assert.That(context.Mesh.VertexArray, Is.EqualTo(original));
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.LeftControl)).Returns(false);
            context.Component.Update(new GameTime());
            Assert.That(context.Mesh.VertexArray, Is.Not.EqualTo(original));
        }

        [Test]
        public void BlenderInput_CtrlDoesNotRoundTypedTranslation()
        {
            using var context = CreateBlenderInputContext();
            var original = context.Mesh.VertexArray[0].Position;
            ReleaseKey(context, Keys.G);
            ReleaseKey(context, Keys.X);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.RightControl)).Returns(true);
            ReleaseKey(context, Keys.D1);
            ReleaseKey(context, Keys.Decimal);
            ReleaseKey(context, Keys.D2);
            Assert.That(context.Mesh.VertexArray[0].Position.X, Is.EqualTo(original.X + 1.2f).Within(0.0001));
        }

        [TestCase(Keys.G)]
        [TestCase(Keys.R)]
        [TestCase(Keys.S)]
        public void BlenderInput_ControlShortcutDoesNotStartTransform(Keys key)
        {
            using var context = CreateBlenderInputContext();
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.RightControl)).Returns(true);
            ReleaseKey(context, key);
            Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
        }

        private static ComponentContext CreateBlenderInputContext()
        {
            var context = CreateComponentContext();
            context.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(400, 400));
            context.Mouse.Setup(mouse => mouse.GetScreenSize()).Returns(new Vector2(1000, 1000));
            context.Mouse.Setup(mouse => mouse.IsMouseButtonDown(MouseButton.Left)).Returns(false);
            context.Mouse.Setup(mouse => mouse.State()).Returns(default(MouseState));
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDownOrReleased(It.IsAny<Keys>()))
                .Returns((Keys key) => context.Keyboard.Object.IsKeyDown(key) || context.Keyboard.Object.IsKeyReleased(key));
            return context;
        }

        [TestCase(GeometrySelectionMode.Vertex, 3)]
        [TestCase(GeometrySelectionMode.Edge, 3)]
        [TestCase(GeometrySelectionMode.Face, 1)]
        public void BlenderInput_EditSelectionAllDeselectInvertAndUndo(GeometrySelectionMode mode, int count)
        {
            using var context = CreateBlenderInputContext();
            var selected = context.SelectionManager.GetState().GetSingleSelectedObject();
            var changeMode = new ObjectSelectionModeCommand(context.SelectionManager);
            changeMode.Configure(selected, mode);
            changeMode.Execute();
            var input = CreateSelectionInput(context);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(Keys.A)).Returns(true);
            input.Update(new GameTime());
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(count));
            input.Update(new GameTime());
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(count), "Repeated A must keep everything selected.");
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.RightAlt)).Returns(true);
            input.Update(new GameTime());
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(Keys.A)).Returns(false);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.RightAlt)).Returns(false);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.RightControl)).Returns(true);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(Keys.I)).Returns(true);
            input.Update(new GameTime());
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(count));
            context.CommandExecutor.Undo();
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero);
            context.CommandExecutor.Undo();
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(count));
            context.CommandExecutor.Undo();
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero, "Repeated A must not add a history entry.");
        }

        [TestCase(2, 0)]
        [TestCase(3, 1)]
        public void BlenderInput_EdgesToFacesRequiresAllBoundaryEdges(int edgeCount, int faceCount)
        {
            using var context = CreateEdgeComponentContext();
            var edges = (EdgeSelectionState)context.SelectionManager.GetState();
            edges.ModifySelection(new[] { (0, 1), (1, 2), (0, 2) }.Take(edgeCount), false);
            var command = new ObjectSelectionModeCommand(context.SelectionManager);
            command.Configure(edges.RenderObject, GeometrySelectionMode.Face);
            context.CommandExecutor.ExecuteCommand(command);
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(faceCount));
            context.CommandExecutor.Undo();
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(edgeCount));
        }

        [Test]
        public void BlenderInput_SelectionShortcutCannotInterruptModalTransform()
        {
            using var context = CreateBlenderInputContext();
            ReleaseKey(context, Keys.G);
            var input = CreateSelectionInput(context);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(Keys.A)).Returns(true);
            input.Update(new GameTime());
            Assert.That(context.Component.Gizmo.IsInModalTransform, Is.True);
        }

        [Test]
        public void BlenderInput_ObjectAllRespectsHiddenAndLockedHierarchy()
        {
            using var context = CreateBlenderInputContext();
            using var scene = new SceneManager(null, null, context.EventHub);
            var first = context.SelectionManager.GetState().GetSingleSelectedObject();
            var second = new TestTransformableNode { Geometry = context.Mesh };
            scene.RootNode.Children.Add(first);
            scene.RootNode.Children.Add(second);
            scene.RootNode.Children.Add(new TestTransformableNode { Geometry = context.Mesh, IsSelectable = false });
            var hidden = new GroupNode("hidden") { IsVisible = false };
            hidden.Children.Add(new TestTransformableNode { Geometry = context.Mesh });
            scene.RootNode.Children.Add(hidden);
            var locked = new GroupNode("locked") { IsLockable = true, IsSelectable = false };
            locked.Children.Add(new TestTransformableNode { Geometry = context.Mesh });
            scene.RootNode.Children.Add(locked);
            var input = CreateSelectionInput(context, scene);
            var documentState = context.CommandExecutor.CurrentDocumentStateId;
            context.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(Keys.A)).Returns(true);
            input.Update(new GameTime());
            input.Update(new GameTime());
            Assert.That(context.SelectionManager.GetState().SelectedObjects(), Is.EquivalentTo(new[] { first, second }));
            Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(documentState));
            context.CommandExecutor.Undo();
            Assert.That(context.SelectionManager.GetState().SelectedObjects(), Is.EquivalentTo(new[] { first }));
            context.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(Keys.A)).Returns(false);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.LeftControl)).Returns(true);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(Keys.I)).Returns(true);
            input.Update(new GameTime());
            Assert.That(context.SelectionManager.GetState().SelectedObjects(), Is.EquivalentTo(new[] { second }));
        }

        [TestCase(Keys.Escape, false)]
        [TestCase(Keys.Escape, true)]
        [TestCase(Keys.Enter, false)]
        [TestCase(Keys.Enter, true)]
        public void BlenderInput_FirstSelectionAfterTransformIsNotSwallowed(Keys finish, bool box)
        {
            using var context = CreateBlenderInputContext();
            using var scene = new SceneManager(null, null, context.EventHub);
            var input = CreateSelectionInput(context, scene);
            context.Mouse.Setup(mouse => mouse.IsMouseOwner(input)).Returns(() =>
                context.Mouse.Object.MouseOwner == null || context.Mouse.Object.MouseOwner == input);
            ReleaseKey(context, Keys.G);
            ReleaseKey(context, finish);
            input.Update(new GameTime());

            context.Mouse.Setup(mouse => mouse.GetPressPosition(MouseButton.Left)).Returns(new Vector2(10, 10));
            context.Mouse.Setup(mouse => mouse.Position()).Returns(box ? new Vector2(200, 200) : new Vector2(10, 10));
            context.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            input.Update(new GameTime());

            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.Zero,
                "The first fresh click or box after Esc/Enter must select normally, including a complete gesture between frames.");
            Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        }

        [Test]
        public void BlenderInput_ConfirmClickReleaseDoesNotSelect()
        {
            using var context = CreateBlenderInputContext();
            using var scene = new SceneManager(null, null, context.EventHub);
            var input = CreateSelectionInput(context, scene);
            context.Mouse.Setup(mouse => mouse.IsMouseOwner(input)).Returns(true);
            ReleaseKey(context, Keys.G);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonDown(MouseButton.Left)).Returns(true);
            context.Component.Update(new GameTime());
            input.Update(new GameTime());
            context.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonDown(MouseButton.Left)).Returns(false);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            input.Update(new GameTime());
            Assert.That(context.SelectionManager.GetState().SelectionCount(), Is.EqualTo(1));
            Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
        }

        [TestCase(Keys.G)]
        [TestCase(Keys.R)]
        [TestCase(Keys.S)]
        public void BlenderInput_MousePathIsIndependentOfFrameCountAndShiftToggle(Keys mode)
        {
            using var coarse = CreateBlenderInputContext();
            using var fine = CreateBlenderInputContext();
            ReleaseKey(coarse, mode);
            ReleaseKey(fine, mode);
            coarse.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(500, 500));
            coarse.Component.Update(new GameTime());
            for (var step = 1; step <= 10; step++)
            {
                fine.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(400 + step * 10, 400 + step * 10));
                fine.Component.Update(new GameTime());
            }
            Assert.That(fine.Mesh.VertexArray, Is.EqualTo(coarse.Mesh.VertexArray));
            var preview = coarse.Mesh.VertexArray.ToArray();
            coarse.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.LeftShift)).Returns(true);
            coarse.Component.Update(new GameTime());
            Assert.That(coarse.Mesh.VertexArray, Is.EqualTo(preview), "Holding Shift without movement must not jump.");
            coarse.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.LeftShift)).Returns(false);
            coarse.Component.Update(new GameTime());
            Assert.That(coarse.Mesh.VertexArray, Is.EqualTo(preview));
        }

        [TestCase(Keys.G)]
        [TestCase(Keys.R)]
        [TestCase(Keys.S)]
        public void BlenderInput_RightClickCancelRestoresAndReleasesOwnership(Keys mode)
        {
            using var context = CreateBlenderInputContext();
            var original = context.Mesh.VertexArray.ToArray();
            ReleaseKey(context, mode);
            ReleaseKey(context, Keys.D2);
            context.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Right)).Returns(true);
            context.Component.Update(new GameTime());
            Assert.Multiple(() =>
            {
                Assert.That(context.Mesh.VertexArray, Is.EqualTo(original));
                Assert.That(context.Component.Gizmo.IsInNumericInput, Is.False);
                Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
            });
        }

        [Test]
        public void BlenderInput_CtrlShiftUsesFineRotationAndScaleSteps()
        {
            using var context = CreateBlenderInputContext();
            SetBlenderTestView(context);
            MoveBlenderPointer(context, new Vector2(700, 500));
            ModalPreviewReplacement preview = default;
            context.Component.Gizmo.ReplacePreviewFromInitialRequested += value => preview = value;
            ReleaseKey(context, Keys.R);
            ReleaseKey(context, Keys.X);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.RightShift)).Returns(true);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.RightControl)).Returns(true);
            MoveBlenderPointer(context, new Vector2(500, 500) + new Vector2(MathF.Cos(MathHelper.Pi / 6), MathF.Sin(MathHelper.Pi / 6)) * 200);
            AssertMatrixNear(preview.RotationValue, Matrix.CreateRotationX(MathHelper.ToRadians(1)));
            ReleaseKey(context, Keys.Escape);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.RightControl)).Returns(false);
            MoveBlenderPointer(context, new Vector2(700, 500));
            ReleaseKey(context, Keys.S);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyDown(Keys.RightControl)).Returns(true);
            MoveBlenderPointer(context, new Vector2(720, 500));
            Assert.That(preview.VectorValue.X, Is.EqualTo(0.01).Within(0.00001));
        }

        [TestCase(2f)]
        [TestCase(-2f)]
        public void BlenderInput_OrientedBoneScalePreservesWorldPoseAndUndo(float factor)
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver4);
            var context = CreateBoneContext(new AnimationClip.KeyFrame
            {
                Position = [new Vector3(2, 0, 0)],
                Rotation = [rotation],
                Scale = [Vector3.One]
            }, [-1], [0]);
            using var wrapper = context.Wrapper;
            var original = GetBoneWorld(context, 0);
            wrapper.BeginTransform();
            wrapper.ReplaceInitialPreview(ModalPreviewReplacement.Scale(new Vector3(factor - 1, 0, 0),
                PivotType.ObjectCenter, Matrix.CreateFromQuaternion(rotation)));
            var expected = Matrix.CreateScale(factor, 1, 1) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(2, 0, 0);
            AssertMatrixNear(GetBoneWorld(context, 0), expected);
            wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            AssertMatrixNear(GetBoneWorld(context, 0), original);
            context.CommandExecutor.Redo();
            AssertMatrixNear(GetBoneWorld(context, 0), expected);
        }

        private static KitbashSelectionInputComponent CreateSelectionInput(ComponentContext context, SceneManager scene = null)
        {
            var services = new Mock<IServiceProvider>();
            services.Setup(provider => provider.GetService(typeof(VertexSelectionCommand))).Returns(() => new VertexSelectionCommand(context.SelectionManager));
            services.Setup(provider => provider.GetService(typeof(EdgeSelectionCommand))).Returns(() => new EdgeSelectionCommand(context.SelectionManager));
            services.Setup(provider => provider.GetService(typeof(FaceSelectionCommand))).Returns(() => new FaceSelectionCommand(context.SelectionManager));
            services.Setup(provider => provider.GetService(typeof(ObjectSelectionCommand))).Returns(() => new ObjectSelectionCommand(context.SelectionManager));
            return new KitbashSelectionInputComponent(context.Mouse.Object, context.Keyboard.Object, context.Camera,
                context.SelectionManager, null, new CommandFactory(services.Object, context.CommandExecutor), scene, null, context.Component);
        }

        private static void ReleaseKey(ComponentContext context, Keys key)
        {
            context.Keyboard.Setup(keyboard => keyboard.IsKeyPressed(key)).Returns(true);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(key)).Returns(true);
            context.Component.Update(new GameTime());
            context.Keyboard.Setup(keyboard => keyboard.IsKeyPressed(key)).Returns(false);
            context.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(key)).Returns(false);
            context.Component.Update(new GameTime());
        }
    }
}
