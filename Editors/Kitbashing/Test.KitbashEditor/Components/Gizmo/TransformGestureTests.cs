using System.Threading;
using System.Reflection;
using GameWorld.Core.Animation;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Gizmo;
using GizmoComponent =
    Editors.KitbasherEditor.Components.KitbashModelGizmoComponent;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Test.TestUtility;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace Testing.GameWorld.Core.Components.Gizmo
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class TransformGestureTests
    {
        [Test]
        public void MouseGizmoStartEvent_CapturesBaselineBeforeFirstPreview()
        {
            using var context = CreateComponentContext();
            var wrapper = (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            var initialVertices = context.Mesh.VertexArray.ToArray();
            context.Component.Gizmo.ActiveAxis = GizmoAxis.X;
            context.Component.Gizmo.ActiveMode = GizmoMode.Translate;

            context.Component.Gizmo.Update(new GameTime(), enableMove: true);
            wrapper.GizmoTranslateEvent(new Vector3(0.25f, 0, 0), PivotType.WorldOrigin);

            Assert.Multiple(() =>
            {
                Assert.That(wrapper.HasBackup, Is.True);
                Assert.That(context.Mesh.DeferBoundingBoxRebuild, Is.True);
                Assert.That(context.Mesh.VertexArray, Is.Not.EqualTo(initialVertices));
            });
        }

        [Test]
        public void EdgeMode_ComponentUpdateRunsStartPreviewStopAndSharedDrawGate()
        {
            using var context = CreateEdgeComponentContext();
            var wrapper = (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            var initialVertices = context.Mesh.VertexArray.ToArray();
            context.Component.SetGizmoMode(GizmoMode.Rotate);
            context.Component.Gizmo.ActiveAxis = GizmoAxis.X;

            context.Component.Update(new GameTime());
            context.Mouse.Setup(component => component.Position()).Returns(new Vector2(80, 10));
            context.Mouse.Setup(component => component.DeltaPosition()).Returns(new Vector2(1, 0));
            context.Mouse.Setup(component => component.LastState()).Returns(
                new MouseState(
                    10,
                    10,
                    0,
                    ButtonState.Pressed,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released));
            context.Component.Update(
                new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

            Assert.Multiple(() =>
            {
                Assert.That(wrapper.HasBackup, Is.True);
                Assert.That(context.Mesh.VertexArray, Is.Not.EqualTo(initialVertices));
                Assert.That(context.Mouse.Object.MouseOwner, Is.SameAs(context.Component));
            });

            context.Mouse
                .Setup(component => component.IsMouseButtonDown(MouseButton.Left))
                .Returns(false);
            context.Mouse.Setup(component => component.State()).Returns(
                new MouseState(
                    80,
                    10,
                    0,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released));
            context.Component.Update(new GameTime());

            var supportedModeMethod = typeof(GizmoComponent).GetMethod(
                "IsSupportedSelectionMode",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.Multiple(() =>
            {
                Assert.That(context.CommandExecutor.CanUndo(), Is.True);
                Assert.That(wrapper.HasBackup, Is.False);
                Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
                Assert.That(supportedModeMethod, Is.Not.Null);
                Assert.That(
                    supportedModeMethod?.Invoke(null, [GeometrySelectionMode.Edge]),
                    Is.True);
            });
        }

        [Test]
        public void ToolbarGizmo_AfterCompletingDrag_AllowsSecondDragWithoutReselecting()
        {
            using var context = CreateComponentContext();
            var wrapper = (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            context.Component.SetGizmoMode(GizmoMode.Translate);
            context.Component.Gizmo.ActiveAxis = GizmoAxis.X;

            context.Component.Update(new GameTime());
            Assert.That(wrapper.HasBackup, Is.True);

            context.Mouse
                .Setup(component => component.IsMouseButtonDown(MouseButton.Left))
                .Returns(false);
            context.Mouse.Setup(component => component.LastState()).Returns(
                new MouseState(
                    10,
                    10,
                    0,
                    ButtonState.Pressed,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released));
            context.Mouse.Setup(component => component.State()).Returns(
                new MouseState(
                    10,
                    10,
                    0,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released));
            context.Component.Update(new GameTime());
            Assert.That(wrapper.HasBackup, Is.False);

            context.Mouse
                .Setup(component => component.IsMouseButtonDown(MouseButton.Left))
                .Returns(true);
            context.Mouse.Setup(component => component.LastState()).Returns(
                new MouseState(
                    10,
                    10,
                    0,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released));
            context.Mouse.Setup(component => component.State()).Returns(
                new MouseState(
                    10,
                    10,
                    0,
                    ButtonState.Pressed,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released));
            context.Component.Gizmo.ActiveAxis = GizmoAxis.X;
            context.Component.Update(new GameTime());

            Assert.Multiple(() =>
            {
                Assert.That(wrapper.HasBackup, Is.True);
                Assert.That(context.Mouse.Object.MouseOwner, Is.SameAs(context.Component));
            });
        }

        [TestCase(Keys.LeftAlt)]
        [TestCase(Keys.RightAlt)]
        public void AltTabRelease_DoesNotToggleEditMode(Keys altKey)
        {
            using var context = CreateComponentContext();
            context.Keyboard
                .Setup(component => component.IsKeyReleased(Keys.Tab))
                .Returns(true);
            context.Keyboard
                .Setup(component => component.IsKeyDownOrReleased(altKey))
                .Returns(true);

            context.Component.Update(new GameTime());

            Assert.That(
                context.SelectionManager.GetState().Mode,
                Is.EqualTo(GeometrySelectionMode.Object));
        }

        [Test]
        public void TabReleaseWithoutAlt_TogglesEditMode()
        {
            using var context = CreateComponentContext();
            context.Keyboard
                .Setup(component => component.IsKeyReleased(Keys.Tab))
                .Returns(true);

            context.Component.Update(new GameTime());

            Assert.That(
                context.SelectionManager.GetState().Mode,
                Is.EqualTo(GeometrySelectionMode.Vertex));
        }

        [Test]
        public void ComponentDispose_DuringVertexPreviewRestoresCpuGpuAndGestureOwnership()
        {
            var context = CreateComponentContext();
            var wrapper = (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            var initialVertices = context.Mesh.VertexArray.ToArray();
            var initialIndices = context.Mesh.IndexArray.ToArray();
            var initialBounds = context.Mesh.BoundingBox;
            context.Component.Gizmo.ActiveAxis = GizmoAxis.X;
            context.Component.Gizmo.ActiveMode = GizmoMode.Translate;
            context.Component.Gizmo.Update(new GameTime(), enableMove: true);
            wrapper.GizmoTranslateEvent(
                new Vector3(0.25f, 0, 0),
                PivotType.WorldOrigin);
            Assert.That(context.Mesh.VertexArray, Is.Not.EqualTo(initialVertices));

            context.Component.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(context.Mesh.VertexArray, Is.EqualTo(initialVertices));
                Assert.That(context.Mesh.IndexArray, Is.EqualTo(initialIndices));
                Assert.That(context.Graphics.UploadedVertexArray, Is.EqualTo(initialVertices));
                Assert.That(context.Graphics.IndexBufferRebuildCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(context.Mesh.BoundingBox, Is.EqualTo(initialBounds));
                Assert.That(context.Mesh.DeferBoundingBoxRebuild, Is.False);
                Assert.That(wrapper.HasBackup, Is.False);
                Assert.That(wrapper.IsTransformActive, Is.False);
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
                Assert.That(context.Component.Gizmo.ActiveAxis, Is.EqualTo(GizmoAxis.None));
                Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
                Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
            });
            AssertNoTransientBackup(wrapper);
            AssertNoActiveCommand(wrapper);
            context.Mouse.Verify(component => component.ClearStates(), Times.Once);
        }

        [Test]
        public void ComponentDispose_DuringBonePreviewRestoresFrameAndRemovesSubscription()
        {
            var initialFrame = new AnimationClip.KeyFrame
            {
                Position = [new Vector3(2, 0, 0)],
                Rotation = [Quaternion.Identity],
                Scale = [Vector3.One]
            };
            var context = CreateBoneComponentContext(initialFrame);
            var selection =
                context.SelectionManager.GetState<BoneSelectionState>();
            var wrapper =
                (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            context.Component.Gizmo.StartModalTransform(GizmoMode.Translate);
            wrapper.GizmoTranslateEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
            Assert.That(
                selection.CurrentAnimation.DynamicFrames[0].Position,
                Is.Not.EqualTo(initialFrame.Position));

            context.Component.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(
                    selection.CurrentAnimation.DynamicFrames[0].Position,
                    Is.EqualTo(initialFrame.Position));
                Assert.That(
                    selection.CurrentAnimation.DynamicFrames[0].Rotation,
                    Is.EqualTo(initialFrame.Rotation));
                Assert.That(
                    selection.CurrentAnimation.DynamicFrames[0].Scale,
                    Is.EqualTo(initialFrame.Scale));
                Assert.That(selection.BoneModificationSubscriberCount, Is.Zero);
                Assert.That(wrapper.IsTransformActive, Is.False);
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
                Assert.That(context.Component.Gizmo.ActiveAxis, Is.EqualTo(GizmoAxis.None));
                Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
                Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
            });
            AssertNoActiveCommand(wrapper);
            context.Mouse.Verify(component => component.ClearStates(), Times.Once);
        }

        [Test]
        public void ComponentDispose_WhenBoneCancelThrowsPreservesFirstErrorAndFinishesCleanup()
        {
            var initialFrame = new AnimationClip.KeyFrame
            {
                Position = [Vector3.Zero],
                Rotation = [Quaternion.Identity],
                Scale = [Vector3.One]
            };
            var context = CreateBoneComponentContext(initialFrame);
            var selection =
                context.SelectionManager.GetState<BoneSelectionState>();
            var wrapper =
                (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            var expectedException = new InvalidOperationException("cancel failed");
            BoneModifiedEvent throwingSubscriber = _ => throw expectedException;
            context.Component.Gizmo.StartModalTransform(GizmoMode.Translate);
            wrapper.GizmoTranslateEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
            selection.BoneModifiedEvent += throwingSubscriber;

            var actualException = Assert.Throws<InvalidOperationException>(
                context.Component.Dispose);

            Assert.Multiple(() =>
            {
                Assert.That(actualException, Is.SameAs(expectedException));
                Assert.That(
                    selection.CurrentAnimation.DynamicFrames[0].Position,
                    Is.EqualTo(initialFrame.Position));
                Assert.That(wrapper.IsTransformActive, Is.False);
                Assert.That(
                    selection.BoneModificationSubscriberCount,
                    Is.EqualTo(1));
                Assert.That(context.Component.Gizmo.Selection, Is.Empty);
                Assert.That(context.Component.Gizmo.ActiveAxis, Is.EqualTo(GizmoAxis.None));
                Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
                Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
            });
            context.Mouse.Verify(component => component.ClearStates(), Times.Once);
            selection.BoneModifiedEvent -= throwingSubscriber;
        }

        [Test]
        public void ComponentDispose_BeforeInitializeIsSafe()
        {
            var eventHub = new TestEventHub();
            var commandExecutor = new CommandExecutor(eventHub);
            var selectionManager = new SelectionManager(eventHub);
            var mouse = new Mock<IMouseComponent>();
            mouse.SetupProperty(component => component.MouseOwner);
            var component = new GizmoComponent(
                eventHub,
                null,
                mouse.Object,
                null,
                commandExecutor,
                null,
                null,
                null,
                selectionManager);

            Assert.DoesNotThrow(component.Dispose);
            mouse.Verify(component => component.ClearStates(), Times.Never);
        }

        [Test]
        public void SelectionChangeDuringLivePreview_CancelsOldGestureAndStopsSameDrag()
        {
            using var context = CreateComponentContext();
            var oldWrapper = (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            var initialVertices = context.Mesh.VertexArray.ToArray();
            var stopEventCount = 0;
            context.Component.Gizmo.StopEvent += () => stopEventCount++;
            context.Component.Gizmo.ActiveAxis = GizmoAxis.X;
            context.Component.Gizmo.ActiveMode = GizmoMode.Translate;
            context.Component.Gizmo.Update(new GameTime(), enableMove: true);
            oldWrapper.GizmoTranslateEvent(new Vector3(0.25f, 0, 0), PivotType.WorldOrigin);
            Assert.That(context.Mesh.VertexArray, Is.Not.EqualTo(initialVertices));

            var replacementMesh = CreateMesh();
            var replacementVertices = replacementMesh.VertexArray.ToArray();
            var replacementNode = new TestTransformableNode { Geometry = replacementMesh };
            var replacementSelection = new ObjectSelectionState();
            replacementSelection.ModifySelectionSingleObject(replacementNode, onlyRemove: false);
            context.SelectionManager.SetState(replacementSelection);
            var replacementWrapper =
                (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();

            context.Component.Gizmo.Update(new GameTime(), enableMove: true);
            context.Mouse
                .Setup(component => component.IsMouseButtonDown(MouseButton.Left))
                .Returns(false);
            context.Mouse.Setup(component => component.LastState()).Returns(
                new MouseState(
                    10,
                    10,
                    0,
                    ButtonState.Pressed,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released));
            context.Mouse.Setup(component => component.State()).Returns(
                new MouseState(
                    10,
                    10,
                    0,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released));
            context.Component.Gizmo.Update(new GameTime(), enableMove: true);

            Assert.Multiple(() =>
            {
                Assert.That(replacementWrapper, Is.Not.SameAs(oldWrapper));
                Assert.That(context.Mesh.VertexArray, Is.EqualTo(initialVertices));
                Assert.That(context.Mesh.DeferBoundingBoxRebuild, Is.False);
                Assert.That(oldWrapper.HasBackup, Is.False);
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
                Assert.That(stopEventCount, Is.Zero);
                Assert.That(context.Component.Gizmo.ActiveAxis, Is.EqualTo(GizmoAxis.None));
                Assert.That(context.Component.Gizmo.IsInModalTransform, Is.False);
                Assert.That(context.Mouse.Object.MouseOwner, Is.Null);
                Assert.That(replacementMesh.VertexArray, Is.EqualTo(replacementVertices));
            });
            AssertNoTransientBackup(oldWrapper);
            AssertNoActiveCommand(oldWrapper);
            context.Mouse.Verify(component => component.ClearStates(), Times.Once);
        }

        [Test]
        public void ModalGizmoStartAndConfirm_CommitsExactlyOneHistoryEntry()
        {
            using var context = CreateComponentContext();
            var wrapper = (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();

            context.Component.Gizmo.StartModalTransform(GizmoMode.Translate);
            wrapper.GizmoTranslateEvent(new Vector3(0.25f, 0, 0), PivotType.WorldOrigin);
            context.Component.Gizmo.ConfirmModalTransform();

            Assert.Multiple(() =>
            {
                Assert.That(context.CommandExecutor.CanUndo(), Is.True);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.EqualTo(1));
                Assert.That(wrapper.HasBackup, Is.False);
                Assert.That(context.Mesh.DeferBoundingBoxRebuild, Is.False);
            });
            AssertNoTransientBackup(wrapper);

            context.CommandExecutor.Undo();
            Assert.That(context.CommandExecutor.CanUndo(), Is.False);
        }

        [Test]
        public void ModalGizmoCancel_RestoresGeometryBoundsAndDisplayStateWithoutHistory()
        {
            using var context = CreateComponentContext();
            var wrapper = (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            var initialVertices = context.Mesh.VertexArray.ToArray();
            var initialIndices = context.Mesh.IndexArray.ToArray();
            var initialBounds = context.Mesh.BoundingBox;
            var initialPosition = wrapper.Position;
            var initialOrientation = wrapper.Orientation;
            var initialScale = wrapper.Scale;

            context.Component.Gizmo.StartModalTransform(GizmoMode.NonUniformScale);
            wrapper.GizmoScaleEvent(new Vector3(-2.2f, 0.25f, 0), PivotType.ObjectCenter);
            context.Component.Gizmo.CancelModalTransform();

            Assert.Multiple(() =>
            {
                Assert.That(context.Mesh.VertexArray, Is.EqualTo(initialVertices));
                Assert.That(context.Mesh.IndexArray, Is.EqualTo(initialIndices));
                Assert.That(context.Mesh.BoundingBox, Is.EqualTo(initialBounds));
                Assert.That(wrapper.Position, Is.EqualTo(initialPosition));
                Assert.That(wrapper.Orientation, Is.EqualTo(initialOrientation));
                Assert.That(wrapper.Scale, Is.EqualTo(initialScale));
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
                Assert.That(context.Mesh.DeferBoundingBoxRebuild, Is.False);
                Assert.That(wrapper.HasBackup, Is.False);
            });
            AssertNoTransientBackup(wrapper);
        }

        [Test]
        public void RestoreInitialPreviewState_RetainsBaselineForReplacementAndCommit()
        {
            var context = CreateDirectContext(CreateMesh());
            var initialVertices = context.Meshes[0].VertexArray.ToArray();

            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(new Vector3(0.3f, 0, 0), PivotType.WorldOrigin);
            context.Wrapper.RestoreInitialPreviewState();

            Assert.Multiple(() =>
            {
                Assert.That(context.Meshes[0].VertexArray, Is.EqualTo(initialVertices));
                Assert.That(context.Wrapper.HasBackup, Is.True);
                Assert.That(context.Meshes[0].DeferBoundingBoxRebuild, Is.True);
            });

            context.Wrapper.GizmoTranslateEvent(new Vector3(0, 0.4f, 0), PivotType.WorldOrigin);
            var replacementPreview = context.Meshes[0].VertexArray.ToArray();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();
            Assert.That(context.Meshes[0].VertexArray, Is.EqualTo(initialVertices));
            context.CommandExecutor.Redo();
            Assert.That(context.Meshes[0].VertexArray, Is.EqualTo(replacementPreview));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestoreInitialPreviewState_UploadsBaselineWhenReplacementIsMissingOrRejected(
            bool attemptRejectedReplacement)
        {
            var mesh = CreateMesh(out var graphics);
            var context = CreateDirectContext(mesh);
            var initialVertices = mesh.VertexArray.ToArray();
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(0.3f, 0, 0),
                PivotType.WorldOrigin);
            Assert.That(graphics.UploadedVertexArray, Is.EqualTo(mesh.VertexArray));
            graphics.ResetRebuildCounts();

            context.Wrapper.RestoreInitialPreviewState();
            if (attemptRejectedReplacement)
            {
                context.Wrapper.GizmoScaleEvent(
                    new Vector3(-1.00001f, 0, 0),
                    PivotType.ObjectCenter);
            }

            Assert.Multiple(() =>
            {
                Assert.That(mesh.VertexArray, Is.EqualTo(initialVertices));
                Assert.That(graphics.UploadedVertexArray, Is.EqualTo(initialVertices));
                Assert.That(graphics.VertexBufferRebuildCount, Is.EqualTo(1));
            });
            context.Wrapper.CancelTransform();
        }

        [Test]
        public void EmptyOrRejectedGesture_DoesNotCreateMutationHistory()
        {
            var context = CreateDirectContext(CreateMesh());

            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoScaleEvent(
                new Vector3(-1.00001f, 0, 0),
                PivotType.ObjectCenter);
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(Vector3.Zero, PivotType.WorldOrigin);
            context.Wrapper.CommitTransform(context.CommandExecutor);

            Assert.Multiple(() =>
            {
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
                Assert.That(context.Wrapper.HasBackup, Is.False);
                Assert.That(context.Meshes[0].DeferBoundingBoxRebuild, Is.False);
            });
            AssertNoTransientBackup(context.Wrapper);
        }

        [Test]
        public void Commit_WhenEventPublicationAndBoundingBoxCleanupThrow_RethrowsPrimaryAndCleansEveryMesh()
        {
            var firstMesh = CreateMesh();
            var secondMesh = CreateMesh();
            var context = CreateDirectContext(firstMesh, secondMesh);
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(new Vector3(0.2f, 0, 0), PivotType.WorldOrigin);
            firstMesh.VertexArray = null;
            context.EventHub.CommandStackChangedException =
                new InvalidOperationException("event publication failed");

            var exception = Assert.Throws<InvalidOperationException>(
                () => context.Wrapper.CommitTransform(context.CommandExecutor));

            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Is.EqualTo("event publication failed"));
                Assert.That(context.CommandExecutor.CanUndo(), Is.True);
                Assert.That(firstMesh.DeferBoundingBoxRebuild, Is.False);
                Assert.That(secondMesh.DeferBoundingBoxRebuild, Is.False);
                Assert.That(context.Wrapper.HasBackup, Is.False);
            });
            AssertNoTransientBackup(context.Wrapper);
        }

        [Test]
        public void Begin_WhenLaterMeshBackupFails_DoesNotLeakPartialBackupOrDeferral()
        {
            var firstMesh = CreateMesh();
            var invalidMesh = CreateMesh();
            invalidMesh.VertexArray = null;
            var context = CreateDirectContext(firstMesh, invalidMesh);

            Assert.Throws<NullReferenceException>(() => context.Wrapper.BeginTransform());

            Assert.Multiple(() =>
            {
                Assert.That(firstMesh.DeferBoundingBoxRebuild, Is.False);
                Assert.That(invalidMesh.DeferBoundingBoxRebuild, Is.False);
                Assert.That(context.Wrapper.HasBackup, Is.False);
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
            });
            AssertNoTransientBackup(context.Wrapper);
        }

        [Test]
        public void ConsecutiveGestures_ResetObjectWindingParity()
        {
            var mesh = CreateMesh();
            var context = CreateDirectContext(mesh);
            var initialIndices = mesh.IndexArray.ToArray();
            var reversedIndices = new ushort[] { 2, 1, 0 };

            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoScaleEvent(new Vector3(-2.2f, 0, 0), PivotType.ObjectCenter);
            context.Wrapper.CommitTransform(context.CommandExecutor);

            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(new Vector3(0.2f, 0, 0), PivotType.WorldOrigin);
            context.Wrapper.CommitTransform(context.CommandExecutor);
            Assert.That(mesh.IndexArray, Is.EqualTo(reversedIndices));

            context.CommandExecutor.Undo();
            Assert.Multiple(() =>
            {
                Assert.That(mesh.IndexArray, Is.EqualTo(reversedIndices));
                Assert.That(context.CommandExecutor.CanUndo(), Is.True);
            });
            context.CommandExecutor.Undo();
            Assert.That(mesh.IndexArray, Is.EqualTo(initialIndices));
        }

        [Test]
        public void Begin_CapturesSelectionBeforePreviewForUndo()
        {
            var mesh = CreateMesh();
            var selectedNode = new TestTransformableNode { Geometry = mesh };
            var initialSelection = new ObjectSelectionState();
            initialSelection.ModifySelectionSingleObject(selectedNode, onlyRemove: false);
            var context = CreateDirectContext(initialSelection, mesh);

            context.Wrapper.BeginTransform();
            context.SelectionManager.SetState(new ObjectSelectionState());
            context.Wrapper.GizmoTranslateEvent(new Vector3(0.2f, 0, 0), PivotType.WorldOrigin);
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.CommandExecutor.Undo();

            Assert.That(
                ((ObjectSelectionState)context.SelectionManager.GetState()).CurrentSelection(),
                Is.EqualTo(new[] { selectedNode }));
        }

        [Test]
        public void BoneBegin_CreatesActiveCommandBeforeFirstPreview()
        {
            var context = CreateBoneContext();
            var initialFrame = context.Selection.CurrentAnimation.DynamicFrames[0].Clone();

            context.Wrapper.BeginTransform();

            Assert.Multiple(() =>
            {
                Assert.That(context.Wrapper.IsTransformActive, Is.True);
                Assert.That(
                    GetActiveCommand(context.Wrapper),
                    Is.InstanceOf<TransformBoneCommand>());
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                    Is.EqualTo(initialFrame.Position));
                Assert.That(context.Wrapper.Scale, Is.EqualTo(Vector3.One));
                Assert.That(context.ModifiedEventCount, Is.Zero);
            });
            context.Wrapper.CancelTransform();
        }

        [TestCase(GizmoMode.Translate)]
        [TestCase(GizmoMode.Rotate)]
        [TestCase(GizmoMode.UniformScale)]
        public void BonePreview_AppliesEveryIncrementalDelta(GizmoMode mode)
        {
            var context = CreateBoneContext();
            context.Wrapper.BeginTransform();

            switch (mode)
            {
                case GizmoMode.Translate:
                    context.Wrapper.GizmoTranslateEvent(
                        new Vector3(0.4f, 0, 0),
                        PivotType.WorldOrigin);
                    context.Wrapper.GizmoTranslateEvent(
                        new Vector3(0.6f, 0, 0),
                        PivotType.WorldOrigin);
                    break;
                case GizmoMode.Rotate:
                    var rotation = Matrix.CreateRotationZ(MathHelper.Pi / 6);
                    context.Wrapper.GizmoRotateEvent(rotation, PivotType.WorldOrigin);
                    context.Wrapper.GizmoRotateEvent(rotation, PivotType.WorldOrigin);
                    break;
                case GizmoMode.UniformScale:
                    context.Wrapper.GizmoScaleEvent(
                        new Vector3(0.25f),
                        PivotType.WorldOrigin);
                    context.Wrapper.GizmoScaleEvent(
                        new Vector3(0.25f),
                        PivotType.WorldOrigin);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }

            var frame = context.Selection.CurrentAnimation.DynamicFrames[0];
            Assert.Multiple(() =>
            {
                Assert.That(
                    frame.Position[0],
                    Is.EqualTo(
                        mode == GizmoMode.Translate
                            ? new Vector3(1, 0, 0)
                            : Vector3.Zero));
                Assert.That(
                    Math.Abs(Quaternion.Dot(
                        frame.Rotation[0],
                        mode == GizmoMode.Rotate
                            ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.Pi / 3)
                            : Quaternion.Identity)),
                    Is.EqualTo(1).Within(0.0001f));
                Assert.That(
                    frame.Scale[0],
                    Is.EqualTo(
                        mode == GizmoMode.UniformScale
                            ? new Vector3(1.5625f)
                            : Vector3.One));
                Assert.That(
                    Vector3.Distance(
                        context.Wrapper.Scale,
                        mode == GizmoMode.UniformScale
                            ? new Vector3(1.5625f)
                            : Vector3.One),
                    Is.LessThan(0.0001f));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(2));
            });
            context.Wrapper.CancelTransform();
        }

        [TestCase(GizmoMode.Rotate)]
        [TestCase(GizmoMode.UniformScale)]
        public void EquivalentBoneGesture_DoesNotCreateMutationHistory(GizmoMode mode)
        {
            var context = CreateBoneContext();
            context.Wrapper.BeginTransform();
            if (mode == GizmoMode.Rotate)
            {
                var halfTurn = Matrix.CreateRotationZ(MathHelper.Pi);
                context.Wrapper.GizmoRotateEvent(halfTurn, PivotType.WorldOrigin);
                context.Wrapper.GizmoRotateEvent(halfTurn, PivotType.WorldOrigin);
            }
            else
            {
                context.Wrapper.GizmoScaleEvent(Vector3.One, PivotType.WorldOrigin);
                context.Wrapper.GizmoScaleEvent(
                    new Vector3(-0.5f),
                    PivotType.WorldOrigin);
            }

            context.Wrapper.CommitTransform(context.CommandExecutor);

            var frame = context.Selection.CurrentAnimation.DynamicFrames[0];
            Assert.Multiple(() =>
            {
                Assert.That(
                    Math.Abs(Quaternion.Dot(frame.Rotation[0], Quaternion.Identity)),
                    Is.EqualTo(1).Within(0.0001f));
                Assert.That(frame.Scale[0], Is.EqualTo(Vector3.One));
                Assert.That(context.Wrapper.Scale, Is.EqualTo(Vector3.One));
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
            });
        }

        [Test]
        public void SignedBoneScale_PreviewUndoRedoPreservesReflectionAndClones()
        {
            var context = CreateBoneContext();
            var initialFrame = context.Selection.CurrentAnimation.DynamicFrames[0].Clone();
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-3, 0, 0),
                PivotType.WorldOrigin);

            var committedFrameReference =
                context.Selection.CurrentAnimation.DynamicFrames[0];
            Assert.Multiple(() =>
            {
                Assert.That(
                    committedFrameReference.Scale[0],
                    Is.EqualTo(new Vector3(-2, 1, 1)));
                Assert.That(context.Wrapper.Scale, Is.EqualTo(new Vector3(-2, 1, 1)));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(1));
            });
            context.Wrapper.CommitTransform(context.CommandExecutor);

            committedFrameReference.Scale[0] = new Vector3(99);
            context.CommandExecutor.Undo();
            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Scale,
                    Is.EqualTo(initialFrame.Scale));
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0],
                    Is.Not.SameAs(committedFrameReference));
                Assert.That(context.Wrapper.Scale, Is.EqualTo(Vector3.One));
            });

            context.CommandExecutor.Redo();
            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Scale[0],
                    Is.EqualTo(new Vector3(-2, 1, 1)));
                Assert.That(context.Wrapper.Scale, Is.EqualTo(new Vector3(-2, 1, 1)));
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0],
                    Is.Not.SameAs(committedFrameReference));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(3));
            });
        }

        [Test]
        public void ExistingSignedBoneScale_InitialDisplayAndPreviewStaySigned()
        {
            var context = CreateBoneContext(
                new AnimationClip.KeyFrame
                {
                    Position = [Vector3.Zero],
                    Rotation = [Quaternion.Identity],
                    Scale = [new Vector3(-2, 1, 1)]
                },
                [-1],
                [0]);

            Assert.That(context.Wrapper.Scale, Is.EqualTo(new Vector3(-2, 1, 1)));
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoScaleEvent(
                new Vector3(0.5f, 0, 0),
                PivotType.ObjectCenter);

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Scale[0],
                    Is.EqualTo(new Vector3(-3, 1, 1)));
                Assert.That(context.Wrapper.Scale, Is.EqualTo(new Vector3(-3, 1, 1)));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(1));
            });
            context.Wrapper.CancelTransform();
        }

        [TestCase(BoneDisplayScenario.IdentityRotation)]
        [TestCase(BoneDisplayScenario.OrientedRotation)]
        [TestCase(BoneDisplayScenario.RotatedWorldScale)]
        [TestCase(BoneDisplayScenario.ReflectedScale)]
        public void AcceptedBonePreviewAndHistory_ResamplesDisplayFromSolvedWorldPose(
            BoneDisplayScenario scenario)
        {
            var initialFrame = scenario switch
            {
                BoneDisplayScenario.IdentityRotation => new AnimationClip.KeyFrame
                {
                    Position = [new Vector3(1, 2, 0)],
                    Rotation = [Quaternion.Identity],
                    Scale = [Vector3.One]
                },
                BoneDisplayScenario.OrientedRotation => new AnimationClip.KeyFrame
                {
                    Position = [new Vector3(2, 0, 0)],
                    Rotation =
                    [
                        Quaternion.CreateFromAxisAngle(
                            Vector3.UnitY,
                            MathHelper.Pi / 5)
                    ],
                    Scale = [new Vector3(2, 3, 4)]
                },
                BoneDisplayScenario.RotatedWorldScale => new AnimationClip.KeyFrame
                {
                    Position = [new Vector3(1, 0, 0)],
                    Rotation =
                    [
                        Quaternion.CreateFromAxisAngle(
                            Vector3.UnitZ,
                            MathHelper.PiOver2)
                    ],
                    Scale = [Vector3.One]
                },
                BoneDisplayScenario.ReflectedScale => new AnimationClip.KeyFrame
                {
                    Position = [new Vector3(1, 0, 0)],
                    Rotation = [Quaternion.Identity],
                    Scale = [new Vector3(-1, 1, 1)]
                },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            var context = CreateBoneContext(initialFrame, [-1], [0]);
            AssertWrapperMatchesSolvedPose(context.Wrapper, context.Selection);
            context.Wrapper.BeginTransform();

            switch (scenario)
            {
                case BoneDisplayScenario.IdentityRotation:
                    context.Wrapper.GizmoRotateEvent(
                        Matrix.CreateRotationZ(MathHelper.PiOver2),
                        PivotType.WorldOrigin);
                    break;
                case BoneDisplayScenario.OrientedRotation:
                    context.Wrapper.GizmoRotateEvent(
                        Matrix.CreateRotationZ(MathHelper.Pi / 4),
                        PivotType.ObjectCenter);
                    break;
                case BoneDisplayScenario.RotatedWorldScale:
                    context.Wrapper.GizmoScaleEvent(
                        new Vector3(1, 0, 0),
                        PivotType.ObjectCenter);
                    break;
                case BoneDisplayScenario.ReflectedScale:
                    context.Wrapper.GizmoScaleEvent(
                        new Vector3(1, 0, 0),
                        PivotType.ObjectCenter);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            AssertWrapperMatchesSolvedPose(context.Wrapper, context.Selection);
            context.Wrapper.CommitTransform(context.CommandExecutor);
            AssertWrapperMatchesSolvedPose(context.Wrapper, context.Selection);

            context.CommandExecutor.Undo();
            AssertWrapperMatchesSolvedPose(context.Wrapper, context.Selection);
            context.CommandExecutor.Redo();
            AssertWrapperMatchesSolvedPose(context.Wrapper, context.Selection);
        }

        [Test]
        public void BoneTransformAcrossModeClone_TwoUndoRedoKeepsCurrentWrapperSynchronized()
        {
            using var context = CreateBoneComponentContext(
                new AnimationClip.KeyFrame
                {
                    Position = [new Vector3(2, 0, 0)],
                    Rotation =
                    [
                        Quaternion.CreateFromAxisAngle(
                            Vector3.UnitY,
                            MathHelper.Pi / 5)
                    ],
                    Scale = [Vector3.One]
                });
            var originalState =
                context.SelectionManager.GetState<BoneSelectionState>();
            var originalWrapper =
                (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            var initialFrame =
                originalState.CurrentAnimation.DynamicFrames[0].Clone();
            originalWrapper.BeginTransform();
            originalWrapper.GizmoRotateEvent(
                Matrix.CreateRotationZ(MathHelper.Pi / 4),
                PivotType.ObjectCenter);
            originalWrapper.CommitTransform(context.CommandExecutor);
            var disposedWrapperOrientation = originalWrapper.Orientation;
            var finalFrame =
                originalState.CurrentAnimation.DynamicFrames[0].Clone();

            var modeCommand =
                new ObjectSelectionModeCommand(context.SelectionManager);
            modeCommand.Configure(context.Node, GeometrySelectionMode.Object);
            context.CommandExecutor.ExecuteCommand(modeCommand);
            context.CommandExecutor.Undo();

            var restoredState =
                context.SelectionManager.GetState<BoneSelectionState>();
            var restoredWrapper =
                (TransformGizmoWrapper)context.Component.Gizmo.Selection.Single();
            Assert.Multiple(() =>
            {
                Assert.That(restoredState, Is.Not.SameAs(originalState));
                Assert.That(restoredWrapper, Is.Not.SameAs(originalWrapper));
            });
            AssertWrapperMatchesSolvedPose(restoredWrapper, restoredState);

            context.CommandExecutor.Undo();
            Assert.Multiple(() =>
            {
                Assert.That(
                    restoredState.CurrentAnimation.DynamicFrames[0].Position,
                    Is.EqualTo(initialFrame.Position));
                Assert.That(
                    restoredState.CurrentAnimation.DynamicFrames[0].Rotation,
                    Is.EqualTo(initialFrame.Rotation));
                Assert.That(
                    restoredState.CurrentAnimation.DynamicFrames[0].Scale,
                    Is.EqualTo(initialFrame.Scale));
            });
            AssertWrapperMatchesSolvedPose(restoredWrapper, restoredState);
            Assert.That(
                Math.Abs(Quaternion.Dot(
                    originalWrapper.Orientation,
                    disposedWrapperOrientation)),
                Is.EqualTo(1).Within(0.0001f));

            context.CommandExecutor.Redo();
            Assert.Multiple(() =>
            {
                Assert.That(
                    restoredState.CurrentAnimation.DynamicFrames[0].Position,
                    Is.EqualTo(finalFrame.Position));
                Assert.That(
                    restoredState.CurrentAnimation.DynamicFrames[0].Scale,
                    Is.EqualTo(finalFrame.Scale));
            });
            AssertWrapperMatchesSolvedPose(restoredWrapper, restoredState);

            var pivotBeforeNextGesture = restoredWrapper.Position;
            restoredWrapper.BeginTransform();
            restoredWrapper.GizmoRotateEvent(
                Matrix.CreateRotationX(MathHelper.Pi / 6),
                PivotType.ObjectCenter);
            AssertWrapperMatchesSolvedPose(restoredWrapper, restoredState);
            Assert.That(
                Vector3.Distance(restoredWrapper.Position, pivotBeforeNextGesture),
                Is.LessThan(0.0001f));
            restoredWrapper.CancelTransform();
        }

        [Test]
        public void AcceptedBonePreview_WhenSubscriberThrows_ResamplesBeforeRethrow()
        {
            var context = CreateBoneContext();
            var expected = new InvalidOperationException("subscriber failed");
            BoneModifiedEvent throwingHandler = _ => throw expected;
            context.Selection.BoneModifiedEvent += throwingHandler;
            context.Wrapper.BeginTransform();

            var actual = Assert.Throws<InvalidOperationException>(() =>
                context.Wrapper.GizmoRotateEvent(
                    Matrix.CreateRotationZ(MathHelper.PiOver2),
                    PivotType.WorldOrigin));

            Assert.That(actual, Is.SameAs(expected));
            AssertWrapperMatchesSolvedPose(context.Wrapper, context.Selection);

            context.Selection.BoneModifiedEvent -= throwingHandler;
            context.Wrapper.CancelTransform();
        }

        [Test]
        public void SelectionCenterRotation_UsesBoneSelectionCenterAsPivot()
        {
            var context = CreateBoneContext(
                new AnimationClip.KeyFrame
                {
                    Position = [new Vector3(2, 0, 0)],
                    Rotation = [Quaternion.Identity],
                    Scale = [Vector3.One]
                },
                [-1],
                [0]);
            var worldBefore = GetBoneWorld(context, 0);
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoRotateEvent(
                Matrix.CreateRotationZ(MathHelper.PiOver2),
                PivotType.SelectionCenter);

            Assert.Multiple(() =>
            {
                AssertMatrixNear(
                    GetBoneWorld(context, 0),
                    worldBefore *
                    Matrix.CreateTranslation(-worldBefore.Translation) *
                    Matrix.CreateRotationZ(MathHelper.PiOver2) *
                    Matrix.CreateTranslation(worldBefore.Translation));
                Assert.That(context.Wrapper.Position, Is.EqualTo(worldBefore.Translation));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(1));
            });
            context.Wrapper.CancelTransform();
        }

        [Test]
        public void ShearedBoneRotation_IsRejectedAtomically()
        {
            var context = CreateBoneContext();
            var initialFrameReference =
                context.Selection.CurrentAnimation.DynamicFrames[0];
            var initialPosition = context.Wrapper.Position;
            var initialOrientation = context.Wrapper.Orientation;
            var shearedRotation = Matrix.Identity;
            shearedRotation.M21 = 0.5f;
            shearedRotation.M22 = MathF.Sqrt(0.75f);
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoRotateEvent(
                shearedRotation,
                PivotType.WorldOrigin);
            context.Wrapper.CommitTransform(context.CommandExecutor);

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0],
                    Is.SameAs(initialFrameReference));
                Assert.That(context.Wrapper.Position, Is.EqualTo(initialPosition));
                Assert.That(context.Wrapper.Orientation, Is.EqualTo(initialOrientation));
                Assert.That(context.ModifiedEventCount, Is.Zero);
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
            });
        }

        [Test]
        public void ZeroBoneScale_IsRejectedBeforeFrameOrDisplayMutation()
        {
            var context = CreateBoneContext();
            var initialFrameReference =
                context.Selection.CurrentAnimation.DynamicFrames[0];
            var initialScale = context.Wrapper.Scale;
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(-1, 0, 0),
                PivotType.WorldOrigin);
            context.Wrapper.CommitTransform(context.CommandExecutor);

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0],
                    Is.SameAs(initialFrameReference));
                Assert.That(context.Wrapper.Scale, Is.EqualTo(initialScale));
                Assert.That(context.ModifiedEventCount, Is.Zero);
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
            });
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void NonFiniteBoneScale_IsRejectedAtomically(float invalidScale)
        {
            var context = CreateBoneContext();
            var initialFrameReference =
                context.Selection.CurrentAnimation.DynamicFrames[0];
            var initialScale = context.Wrapper.Scale;
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(invalidScale, 0, 0),
                PivotType.WorldOrigin);
            context.Wrapper.CommitTransform(context.CommandExecutor);

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0],
                    Is.SameAs(initialFrameReference));
                Assert.That(context.Wrapper.Scale, Is.EqualTo(initialScale));
                Assert.That(context.ModifiedEventCount, Is.Zero);
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
            });
        }

        [Test]
        public void ChildTranslation_UnderRotatedScaledParentMovesInWorldSpace()
        {
            var initialFrame = CreateTwoBoneFrame(
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver2),
                new Vector3(2));
            var context = CreateBoneContext(initialFrame, [-1, 0], [1]);
            var parentBefore = GetBoneWorld(context, 0);
            var childBefore = GetBoneWorld(context, 1);
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoTranslateEvent(
                Vector3.UnitX,
                PivotType.WorldOrigin);

            Assert.Multiple(() =>
            {
                AssertMatrixNear(GetBoneWorld(context, 0), parentBefore);
                AssertMatrixNear(
                    GetBoneWorld(context, 1),
                    childBefore * Matrix.CreateTranslation(Vector3.UnitX));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(1));
            });
            context.Wrapper.CancelTransform();
        }

        [TestCase(GizmoMode.Translate)]
        [TestCase(GizmoMode.Rotate)]
        [TestCase(GizmoMode.UniformScale)]
        [TestCase(GizmoMode.NonUniformScale)]
        public void SelectedHierarchy_AppliesWorldDeltaOnceToParentAndChild(
            GizmoMode mode)
        {
            var context = CreateBoneContext(
                CreateTwoBoneFrame(Quaternion.Identity, Vector3.One),
                [-1, 0],
                [0, 1]);
            var before = new[]
            {
                GetBoneWorld(context, 0),
                GetBoneWorld(context, 1)
            };
            context.Wrapper.BeginTransform();

            Matrix expectedWorldDelta;
            switch (mode)
            {
                case GizmoMode.Translate:
                    expectedWorldDelta = Matrix.CreateTranslation(Vector3.UnitX);
                    context.Wrapper.GizmoTranslateEvent(
                        Vector3.UnitX,
                        PivotType.WorldOrigin);
                    break;
                case GizmoMode.Rotate:
                    expectedWorldDelta = Matrix.CreateRotationZ(MathHelper.PiOver2);
                    context.Wrapper.GizmoRotateEvent(
                        expectedWorldDelta,
                        PivotType.WorldOrigin);
                    break;
                case GizmoMode.UniformScale:
                    expectedWorldDelta = Matrix.CreateScale(2);
                    context.Wrapper.GizmoScaleEvent(
                        Vector3.One,
                        PivotType.WorldOrigin);
                    break;
                case GizmoMode.NonUniformScale:
                    expectedWorldDelta = Matrix.CreateScale(2, 0.5f, 1);
                    context.Wrapper.GizmoScaleEvent(
                        new Vector3(1, -0.5f, 0),
                        PivotType.WorldOrigin);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }

            Assert.Multiple(() =>
            {
                AssertMatrixNear(
                    GetBoneWorld(context, 0),
                    before[0] * expectedWorldDelta);
                AssertMatrixNear(
                    GetBoneWorld(context, 1),
                    before[1] * expectedWorldDelta);
                Assert.That(context.ModifiedEventCount, Is.EqualTo(1));
            });
            context.Wrapper.CancelTransform();
        }

        [Test]
        public void SelectedDescendantThroughUnselectedParent_InheritsWorldDeltaOnce()
        {
            var initialFrame = new AnimationClip.KeyFrame
            {
                Position =
                [
                    new Vector3(1, 0, 0),
                    new Vector3(1, 0, 0),
                    new Vector3(1, 0, 0)
                ],
                Rotation =
                [
                    Quaternion.Identity,
                    Quaternion.Identity,
                    Quaternion.Identity
                ],
                Scale = [Vector3.One, Vector3.One, Vector3.One]
            };
            var context = CreateBoneContext(
                initialFrame,
                [-1, 0, 1],
                [0, 2]);
            var before = new[]
            {
                GetBoneWorld(context, 0),
                GetBoneWorld(context, 1),
                GetBoneWorld(context, 2)
            };
            var worldDelta = Matrix.CreateTranslation(Vector3.UnitX);
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoTranslateEvent(
                Vector3.UnitX,
                PivotType.WorldOrigin);

            Assert.Multiple(() =>
            {
                AssertMatrixNear(GetBoneWorld(context, 0), before[0] * worldDelta);
                AssertMatrixNear(GetBoneWorld(context, 1), before[1] * worldDelta);
                AssertMatrixNear(GetBoneWorld(context, 2), before[2] * worldDelta);
                Assert.That(context.ModifiedEventCount, Is.EqualTo(1));
            });
            context.Wrapper.CancelTransform();
        }

        [Test]
        public void NonUniformWorldScaleThatCreatesShear_IsRejectedAtomically()
        {
            var initialFrame = CreateTwoBoneFrame(
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver4),
                new Vector3(2, 1, 1));
            var context = CreateBoneContext(initialFrame, [-1, 0], [1]);
            var initialFrameReference =
                context.Selection.CurrentAnimation.DynamicFrames[0];
            var initialWrapperScale = context.Wrapper.Scale;
            var childBefore = GetBoneWorld(context, 1);
            context.Wrapper.BeginTransform();

            context.Wrapper.GizmoScaleEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
            context.Wrapper.CommitTransform(context.CommandExecutor);

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0],
                    Is.SameAs(initialFrameReference));
                AssertMatrixNear(GetBoneWorld(context, 1), childBefore);
                Assert.That(context.Wrapper.Scale, Is.EqualTo(initialWrapperScale));
                Assert.That(context.ModifiedEventCount, Is.Zero);
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
            });
        }

        [Test]
        public void BoneCancel_RestoresInitialFrameAndCreatesNoHistory()
        {
            var context = CreateBoneContext();
            var initialFrame = context.Selection.CurrentAnimation.DynamicFrames[0].Clone();
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
            Assert.That(
                context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                Is.Not.EqualTo(initialFrame.Position));

            context.Wrapper.CancelTransform();

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                    Is.EqualTo(initialFrame.Position));
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Rotation,
                    Is.EqualTo(initialFrame.Rotation));
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Scale,
                    Is.EqualTo(initialFrame.Scale));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(2));
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
                Assert.That(context.Wrapper.IsTransformActive, Is.False);
            });
        }

        [Test]
        public void BoneRestoreInitialPreviewState_RestoresAndRepreviewsFromSameBaseline()
        {
            var context = CreateBoneContext();
            var initialFrame = context.Selection.CurrentAnimation.DynamicFrames[0].Clone();
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
            Assert.That(
                context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                Is.Not.EqualTo(initialFrame.Position));

            context.Wrapper.RestoreInitialPreviewState();
            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                    Is.EqualTo(initialFrame.Position));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(2));
            });

            context.Wrapper.GizmoTranslateEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Position[0],
                    Is.EqualTo(new Vector3(1, 0, 0)));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(3));
            });
            context.Wrapper.CancelTransform();
        }

        [Test]
        public void BoneCommit_CapturesDistinctFinalFrameForUndoAndRedo()
        {
            var context = CreateBoneContext();
            var initialFrame = context.Selection.CurrentAnimation.DynamicFrames[0].Clone();
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(0.75f, 0, 0),
                PivotType.WorldOrigin);
            var committedFrameReference =
                context.Selection.CurrentAnimation.DynamicFrames[0];
            var expectedFinalFrame = committedFrameReference.Clone();

            context.Wrapper.CommitTransform(context.CommandExecutor);

            Assert.Multiple(() =>
            {
                Assert.That(context.CommandExecutor.CanUndo(), Is.True);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.EqualTo(1));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(1));
                Assert.That(context.Wrapper.IsTransformActive, Is.False);
            });

            committedFrameReference.Position[0] = new Vector3(99, 0, 0);
            context.CommandExecutor.Undo();
            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                    Is.EqualTo(initialFrame.Position));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(2));
            });

            context.CommandExecutor.Redo();
            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                    Is.EqualTo(expectedFinalFrame.Position));
                Assert.That(
                    context.Selection.CurrentAnimation.DynamicFrames[0],
                    Is.Not.SameAs(committedFrameReference));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(3));
            });
        }

        [Test]
        public void EmptyOrRestoredBoneGesture_DoesNotCreateMutationHistory()
        {
            var context = CreateBoneContext();

            context.Wrapper.BeginTransform();
            context.Wrapper.CommitTransform(context.CommandExecutor);
            context.Wrapper.BeginTransform();
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
            context.Wrapper.RestoreInitialPreviewState();
            context.Wrapper.CommitTransform(context.CommandExecutor);

            Assert.Multiple(() =>
            {
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
                Assert.That(context.EventHub.CommandStackChangedCount, Is.Zero);
                Assert.That(context.Wrapper.IsTransformActive, Is.False);
            });
        }

        private static ComponentContext CreateComponentContext()
        {
            var mesh = CreateMesh(out var graphics);
            var selectable = new TestTransformableNode { Geometry = mesh };
            var selection = new ObjectSelectionState();
            selection.ModifySelectionSingleObject(selectable, onlyRemove: false);
            return CreateComponentContext(selection, mesh, graphics);
        }

        private static ComponentContext CreateEdgeComponentContext()
        {
            var mesh = CreateMesh(out var graphics);
            var selectable = new TestTransformableNode { Geometry = mesh };
            var selection = new EdgeSelectionState
            {
                RenderObject = selectable,
                SelectedEdges = [(0, 1)]
            };
            return CreateComponentContext(selection, mesh, graphics);
        }

        private static ComponentContext CreateComponentContext(
            ISelectionState selection,
            MeshObject mesh,
            TestGraphicsCardGeometry graphics)
        {
            var eventHub = new TestEventHub();
            var commandExecutor = new CommandExecutor(eventHub);
            var selectionManager = new SelectionManager(eventHub);
            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider
                .Setup(provider => provider.GetService(typeof(TransformVertexCommand)))
                .Returns(() => new TransformVertexCommand(selectionManager));
            serviceProvider
                .Setup(provider => provider.GetService(typeof(ObjectSelectionModeCommand)))
                .Returns(() => new ObjectSelectionModeCommand(selectionManager));
            var commandFactory = new CommandFactory(serviceProvider.Object, commandExecutor);

            var mouse = new Mock<IMouseComponent>();
            mouse.SetupProperty(component => component.MouseOwner);
            mouse.Setup(component => component.Position()).Returns(new Vector2(10, 10));
            mouse.Setup(component => component.IsMouseButtonDown(MouseButton.Left)).Returns(true);
            mouse.Setup(component => component.LastState()).Returns(
                new MouseState(
                    10,
                    10,
                    0,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released));
            mouse.Setup(component => component.State()).Returns(
                new MouseState(
                    10,
                    10,
                    0,
                    ButtonState.Pressed,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released));
            var keyboard = new Mock<IKeyboardComponent>();
            var deviceResolver = new Mock<IDeviceResolver>();
            deviceResolver.SetupGet(resolver => resolver.Device).Returns(new WpfGameMock().GraphicsDevice);
            var camera = new ArcBallCamera(deviceResolver.Object, keyboard.Object, mouse.Object);
            camera.Initialize();
            var component = new GizmoComponent(
                eventHub,
                keyboard.Object,
                mouse.Object,
                camera,
                commandExecutor,
                null,
                deviceResolver.Object,
                commandFactory,
                selectionManager);
            component.Initialize();

            selectionManager.SetState(selection);

            return new ComponentContext(
                component,
                selectionManager,
                commandExecutor,
                eventHub,
                mouse,
                keyboard,
                mesh,
                graphics);
        }

        private static DirectContext CreateDirectContext(params MeshObject[] meshes)
        {
            return CreateDirectContext(new ObjectSelectionState(), meshes);
        }

        private static DirectContext CreateDirectContext(
            ISelectionState selection,
            params MeshObject[] meshes)
        {
            var eventHub = new TestEventHub();
            var commandExecutor = new CommandExecutor(eventHub);
            var selectionManager = new SelectionManager(eventHub);
            selectionManager.SetState(selection);
            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider
                .Setup(provider => provider.GetService(typeof(TransformVertexCommand)))
                .Returns(() => new TransformVertexCommand(selectionManager));
            var commandFactory = new CommandFactory(serviceProvider.Object, commandExecutor);
            var wrapper = new TransformGizmoWrapper(commandFactory, meshes.ToList(), selection);
            return new DirectContext(
                wrapper,
                selectionManager,
                commandExecutor,
                eventHub,
                meshes);
        }

        private static BoneContext CreateBoneContext()
        {
            return CreateBoneContext(
                new AnimationClip.KeyFrame
                {
                    Position = [Vector3.Zero],
                    Rotation = [Quaternion.Identity],
                    Scale = [Vector3.One]
                },
                [-1],
                [0]);
        }

        private static BoneContext CreateBoneContext(
            AnimationClip.KeyFrame initialFrame,
            int[] parentIds,
            int[] selectedBones)
        {
            var scene = CreateBoneScene(initialFrame, parentIds, selectedBones);
            var selection = scene.Selection;
            var modifiedEventCount = 0;
            selection.BoneModifiedEvent += _ => modifiedEventCount++;

            var eventHub = new TestEventHub();
            var commandExecutor = new CommandExecutor(eventHub);
            var selectionManager = new SelectionManager(eventHub);
            selectionManager.SetState(selection);
            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider
                .Setup(provider => provider.GetService(typeof(TransformBoneCommand)))
                .Returns(() => new TransformBoneCommand(selectionManager));
            var commandFactory = new CommandFactory(serviceProvider.Object, commandExecutor);
            var wrapper = new TransformGizmoWrapper(
                commandFactory,
                selection.SelectedBones,
                selection);
            return new BoneContext(
                wrapper,
                selection,
                commandExecutor,
                eventHub,
                () => modifiedEventCount);
        }

        private static BoneScene CreateBoneScene(
            AnimationClip.KeyFrame initialFrame,
            int[] parentIds,
            int[] selectedBones)
        {
            var player = new AnimationPlayer();
            var skeletonFile = new AnimationFile
            {
                Header = new AnimationFile.AnimationHeader { SkeletonName = "TestSkeleton" },
                Bones = parentIds
                    .Select((parentId, boneIndex) => new AnimationFile.BoneInfo
                    {
                        Name = $"bone_{boneIndex}",
                        ParentId = parentId
                    })
                    .ToArray()
            };
            var skeletonFrame = new AnimationFile.Frame();
            foreach (var _ in parentIds)
            {
                skeletonFrame.Transforms.Add(new RmvVector3());
                skeletonFrame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }
            var skeletonPart = new AnimationFile.AnimationPart();
            skeletonPart.DynamicFrames.Add(skeletonFrame);
            skeletonFile.AnimationParts.Add(skeletonPart);
            var skeleton = new GameSkeleton(skeletonFile, player);

            var clip = new AnimationClip();
            clip.DynamicFrames.Add(initialFrame.Clone());
            clip.Duration = TimeSpan.FromSeconds(1);
            player.SetAnimation(clip, skeleton);
            player.IsEnabled = true;
            player.Pause();
            player.Refresh();

            var material = new Mock<IRmvMaterial>();
            material.SetupProperty(value => value.ModelName, "TestMesh");
            material.SetupProperty(value => value.PivotPoint, Vector3.Zero);
            var node = new Rmv2MeshNode(CreateMesh(), material.Object, null, player);
            var selection = new BoneSelectionState(node)
            {
                CurrentAnimation = clip,
                Skeleton = skeleton,
                CurrentFrame = 0,
                SelectedBones = selectedBones.ToList()
            };
            return new BoneScene(node, selection);
        }

        private static BoneComponentContext CreateBoneComponentContext(
            AnimationClip.KeyFrame initialFrame)
        {
            var scene = CreateBoneScene(initialFrame, [-1], [0]);
            var eventHub = new TestEventHub();
            var commandExecutor = new CommandExecutor(eventHub);
            var selectionManager = new SelectionManager(eventHub);
            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider
                .Setup(provider => provider.GetService(typeof(TransformBoneCommand)))
                .Returns(() => new TransformBoneCommand(selectionManager));
            var commandFactory = new CommandFactory(
                serviceProvider.Object,
                commandExecutor);

            var mouse = new Mock<IMouseComponent>();
            mouse.SetupProperty(component => component.MouseOwner);
            mouse.Setup(component => component.Position()).Returns(new Vector2(10, 10));
            var keyboard = new Mock<IKeyboardComponent>();
            var deviceResolver = new Mock<IDeviceResolver>();
            deviceResolver
                .SetupGet(resolver => resolver.Device)
                .Returns(new WpfGameMock().GraphicsDevice);
            var camera = new ArcBallCamera(
                deviceResolver.Object,
                keyboard.Object,
                mouse.Object);
            camera.Initialize();
            var component = new GizmoComponent(
                eventHub,
                keyboard.Object,
                mouse.Object,
                camera,
                commandExecutor,
                null,
                deviceResolver.Object,
                commandFactory,
                selectionManager);
            component.Initialize();
            selectionManager.SetState(scene.Selection);

            return new BoneComponentContext(
                component,
                selectionManager,
                commandExecutor,
                eventHub,
                mouse,
                scene.Node);
        }

        private static AnimationClip.KeyFrame CreateTwoBoneFrame(
            Quaternion parentRotation,
            Vector3 parentScale)
        {
            return new AnimationClip.KeyFrame
            {
                Position = [new Vector3(1, 0, 0), new Vector3(1, 0, 0)],
                Rotation = [parentRotation, Quaternion.Identity],
                Scale = [parentScale, Vector3.One]
            };
        }

        private static Matrix GetBoneWorld(BoneContext context, int boneIndex)
        {
            var frame = AnimationSampler.Sample(
                0,
                0,
                context.Selection.Skeleton,
                context.Selection.CurrentAnimation,
                freezeFrame: true);
            return frame.GetSkeletonAnimatedWorld(
                context.Selection.Skeleton,
                boneIndex);
        }

        private static void AssertWrapperMatchesSolvedPose(
            TransformGizmoWrapper wrapper,
            BoneSelectionState selection,
            int boneIndex = 0)
        {
            var sampledFrame = AnimationSampler.Sample(
                selection.CurrentFrame,
                0,
                selection.Skeleton,
                selection.CurrentAnimation,
                freezeFrame: true);
            var world = sampledFrame.GetSkeletonAnimatedWorld(
                selection.Skeleton,
                boneIndex);
            var localScale =
                selection.CurrentAnimation
                    .DynamicFrames[selection.CurrentFrame]
                    .Scale[boneIndex];
            Assert.That(
                BoneTransformMath.TryDecomposeSignedTrs(
                    world,
                    localScale,
                    out var expectedScale,
                    out var expectedOrientation,
                    out var expectedPosition),
                Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Vector3.Distance(wrapper.Position, expectedPosition),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Vector3.Distance(wrapper.Scale, expectedScale),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Math.Abs(Quaternion.Dot(
                        wrapper.Orientation,
                        expectedOrientation)),
                    Is.EqualTo(1).Within(0.0001f));
            });
        }

        private static void AssertMatrixNear(
            Matrix actual,
            Matrix expected,
            float epsilon = 0.0001f)
        {
            var actualValues = new[]
            {
                actual.M11, actual.M12, actual.M13, actual.M14,
                actual.M21, actual.M22, actual.M23, actual.M24,
                actual.M31, actual.M32, actual.M33, actual.M34,
                actual.M41, actual.M42, actual.M43, actual.M44
            };
            var expectedValues = new[]
            {
                expected.M11, expected.M12, expected.M13, expected.M14,
                expected.M21, expected.M22, expected.M23, expected.M24,
                expected.M31, expected.M32, expected.M33, expected.M34,
                expected.M41, expected.M42, expected.M43, expected.M44
            };
            for (var component = 0; component < actualValues.Length; component++)
            {
                Assert.That(
                    actualValues[component],
                    Is.EqualTo(expectedValues[component]).Within(epsilon),
                    $"matrix component {component}");
            }
        }

        private static ICommand GetActiveCommand(TransformGizmoWrapper wrapper)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            return (ICommand)typeof(TransformGizmoWrapper)
                .GetField("_activeCommand", Flags)
                ?.GetValue(wrapper);
        }

        private static void AssertNoTransientBackup(TransformGizmoWrapper wrapper)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.Multiple(() =>
            {
                Assert.That(
                    typeof(TransformGizmoWrapper)
                        .GetField("_backupVertexArrays", Flags)
                        ?.GetValue(wrapper),
                    Is.Null);
                Assert.That(
                    typeof(TransformGizmoWrapper)
                        .GetField("_backupIndexArrays", Flags)
                        ?.GetValue(wrapper),
                    Is.Null);
            });
        }

        private static void AssertNoActiveCommand(TransformGizmoWrapper wrapper)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.That(
                typeof(TransformGizmoWrapper)
                    .GetField("_activeCommand", Flags)
                    ?.GetValue(wrapper),
                Is.Null);
        }

        private static MeshObject CreateMesh()
        {
            return CreateMesh(out _);
        }

        private static MeshObject CreateMesh(out TestGraphicsCardGeometry graphics)
        {
            graphics = new TestGraphicsCardGeometry();
            var mesh = new MeshObject(graphics, string.Empty)
            {
                VertexArray =
                [
                    CreateVertex(new Vector3(1, 0, 0)),
                    CreateVertex(new Vector3(0, 1, 0)),
                    CreateVertex(new Vector3(0, 0, 1))
                ],
                IndexArray = [0, 1, 2]
            };
            mesh.BuildBoundingBox();
            return mesh;
        }

        private static VertexPositionNormalTextureCustom CreateVertex(Vector3 position)
        {
            return new VertexPositionNormalTextureCustom
            {
                Position = new Vector4(position, 1),
                Normal = Vector3.UnitZ,
                Tangent = Vector3.UnitX,
                BiNormal = Vector3.UnitY
            };
        }

        private sealed class TestTransformableNode : SceneNode, ISelectable, ITransformable
        {
            public MeshObject Geometry { get; set; }
            public bool IsSelectable { get; set; } = true;
            public Vector3 Position { get; set; }
            public Vector3 Scale { get; set; } = Vector3.One;
            public Quaternion Orientation { get; set; } = Quaternion.Identity;

            public Vector3 GetObjectCentre() => Geometry.MeshCenter;
            public override ISceneNode CreateCopyInstance() => new TestTransformableNode();
        }

        private sealed class TestEventHub : IEventHub
        {
            private readonly Dictionary<Type, List<Delegate>> _subscribers = new();
            public int CommandStackChangedCount { get; private set; }
            public Exception CommandStackChangedException { get; set; }

            public void PublishGlobalEvent<T>(T e) => Publish(e);

            public void Publish<T>(T e)
            {
                if (e is CommandStackChangedEvent)
                {
                    CommandStackChangedCount++;
                    if (CommandStackChangedException != null)
                        throw CommandStackChangedException;
                }

                if (!_subscribers.TryGetValue(typeof(T), out var subscribers))
                    return;

                foreach (var subscriber in subscribers)
                    ((Action<T>)subscriber)(e);
            }

            public void Register<T>(object owner, Action<T> action)
            {
                if (!_subscribers.TryGetValue(typeof(T), out var subscribers))
                {
                    subscribers = new List<Delegate>();
                    _subscribers.Add(typeof(T), subscribers);
                }

                subscribers.Add(action);
            }

            public void UnRegister(object owner)
            {
            }
        }

        private sealed record DirectContext(
            TransformGizmoWrapper Wrapper,
            SelectionManager SelectionManager,
            CommandExecutor CommandExecutor,
            TestEventHub EventHub,
            IReadOnlyList<MeshObject> Meshes);

        private sealed record BoneContext(
            TransformGizmoWrapper Wrapper,
            BoneSelectionState Selection,
            CommandExecutor CommandExecutor,
            TestEventHub EventHub,
            Func<int> GetModifiedEventCount)
        {
            public int ModifiedEventCount => GetModifiedEventCount();
        }

        private sealed record BoneScene(
            Rmv2MeshNode Node,
            BoneSelectionState Selection);

        private sealed record BoneComponentContext(
            GizmoComponent Component,
            SelectionManager SelectionManager,
            CommandExecutor CommandExecutor,
            TestEventHub EventHub,
            Mock<IMouseComponent> Mouse,
            Rmv2MeshNode Node) : IDisposable
        {
            public void Dispose() => Component.Dispose();
        }

        public enum BoneDisplayScenario
        {
            IdentityRotation,
            OrientedRotation,
            RotatedWorldScale,
            ReflectedScale
        }

        private sealed record ComponentContext(
            GizmoComponent Component,
            SelectionManager SelectionManager,
            CommandExecutor CommandExecutor,
            TestEventHub EventHub,
            Mock<IMouseComponent> Mouse,
            Mock<IKeyboardComponent> Keyboard,
            MeshObject Mesh,
            TestGraphicsCardGeometry Graphics) : IDisposable
        {
            public void Dispose() => Component.Dispose();
        }
    }
}
