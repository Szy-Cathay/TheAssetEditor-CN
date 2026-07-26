using System.Threading;
using System.Reflection;
using GameWorld.Core.Animation;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Bone;
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
        public void BoneCancel_RestoresInitialFrameAndCreatesNoHistory()
        {
            var context = CreateBoneContext();
            var initialFrame = context.Selection.CurrentAnimation.DynamicFrames[0].Clone();
            context.Wrapper.BeginTransform();
            ApplyTwoBoneTranslationPreviews(context.Wrapper);
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
                Assert.That(context.ModifiedEventCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(context.CommandExecutor.CanUndo(), Is.False);
            });
        }

        [Test]
        public void BoneRestoreInitialPreviewState_RestoresFrameAndResetsPreviewDelta()
        {
            var context = CreateBoneContext();
            var initialFrame = context.Selection.CurrentAnimation.DynamicFrames[0].Clone();
            context.Wrapper.BeginTransform();
            ApplyTwoBoneTranslationPreviews(context.Wrapper);
            Assert.That(
                context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                Is.Not.EqualTo(initialFrame.Position));

            context.Wrapper.RestoreInitialPreviewState();
            Assert.That(
                context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                Is.EqualTo(initialFrame.Position));

            context.Wrapper.GizmoTranslateEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
            Assert.That(
                context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                Is.EqualTo(initialFrame.Position));
            context.Wrapper.GizmoTranslateEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
            Assert.That(
                context.Selection.CurrentAnimation.DynamicFrames[0].Position,
                Is.Not.EqualTo(initialFrame.Position));
            context.Wrapper.CancelTransform();
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

            return new ComponentContext(
                component,
                selectionManager,
                commandExecutor,
                eventHub,
                mouse,
                mesh);
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

        private static BoneContext CreateBoneContext()
        {
            var player = new AnimationPlayer();
            var skeletonFile = new AnimationFile
            {
                Header = new AnimationFile.AnimationHeader { SkeletonName = "TestSkeleton" },
                Bones =
                [
                    new AnimationFile.BoneInfo
                    {
                        Name = "root",
                        ParentId = -1
                    }
                ]
            };
            var skeletonFrame = new AnimationFile.Frame();
            skeletonFrame.Transforms.Add(new RmvVector3());
            skeletonFrame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            var skeletonPart = new AnimationFile.AnimationPart();
            skeletonPart.DynamicFrames.Add(skeletonFrame);
            skeletonFile.AnimationParts.Add(skeletonPart);
            var skeleton = new GameSkeleton(skeletonFile, player);

            var clip = new AnimationClip();
            clip.DynamicFrames.Add(new AnimationClip.KeyFrame
            {
                Position = [Vector3.Zero],
                Rotation = [Quaternion.Identity],
                Scale = [Vector3.One]
            });
            clip.PlayTimeInSec = 1;
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
                SelectedBones = [0]
            };
            var modifiedEventCount = 0;
            selection.BoneModifiedEvent += _ => modifiedEventCount++;

            var eventHub = new TestEventHub();
            var commandExecutor = new CommandExecutor(eventHub);
            var selectionManager = new SelectionManager(eventHub, null, null, null);
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
                () => modifiedEventCount);
        }

        private static void ApplyTwoBoneTranslationPreviews(TransformGizmoWrapper wrapper)
        {
            wrapper.GizmoTranslateEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
            wrapper.GizmoTranslateEvent(
                new Vector3(1, 0, 0),
                PivotType.WorldOrigin);
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
            Func<int> GetModifiedEventCount)
        {
            public int ModifiedEventCount => GetModifiedEventCount();
        }

        private sealed record ComponentContext(
            GizmoComponent Component,
            SelectionManager SelectionManager,
            CommandExecutor CommandExecutor,
            TestEventHub EventHub,
            Mock<IMouseComponent> Mouse,
            MeshObject Mesh) : IDisposable
        {
            public void Dispose() => Component.Dispose();
        }
    }
}
