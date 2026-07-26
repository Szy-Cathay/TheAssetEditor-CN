using System.Threading;
using System.Reflection;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Gizmo;
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

        private static ComponentContext CreateComponentContext()
        {
            var eventHub = new TestEventHub();
            var commandExecutor = new CommandExecutor(eventHub);
            var selectionManager = new SelectionManager(eventHub, null, null, null);
            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider
                .Setup(provider => provider.GetService(typeof(TransformVertexCommand)))
                .Returns(() => new TransformVertexCommand(selectionManager));
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

            var mesh = CreateMesh();
            var selectable = new TestTransformableNode { Geometry = mesh };
            var selection = new ObjectSelectionState();
            selection.ModifySelectionSingleObject(selectable, onlyRemove: false);
            selectionManager.SetState(selection);

            return new ComponentContext(component, selectionManager, commandExecutor, eventHub, mesh);
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
            var selectionManager = new SelectionManager(eventHub, null, null, null);
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

        private static MeshObject CreateMesh()
        {
            var mesh = new MeshObject(new TestGraphicsCardGeometry(), string.Empty)
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

        private sealed record ComponentContext(
            GizmoComponent Component,
            SelectionManager SelectionManager,
            CommandExecutor CommandExecutor,
            TestEventHub EventHub,
            MeshObject Mesh) : IDisposable
        {
            public void Dispose() => Component.Dispose();
        }
    }
}
