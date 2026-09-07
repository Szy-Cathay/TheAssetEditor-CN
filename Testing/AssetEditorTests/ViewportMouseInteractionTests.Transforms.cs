using System.Windows;
using System.Windows.Interop;
using Editors.KitbasherEditor.Components;
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
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;

namespace AssetEditorTests;

public partial class ViewportMouseInteractionTests
{
    [DataTestMethod]
    [DataRow(GizmoMode.Translate, GizmoAxis.None, 0f)]
    [DataRow(GizmoMode.Translate, GizmoAxis.X, 88f)]
    [DataRow(GizmoMode.Translate, GizmoAxis.Z, 2f)]
    [DataRow(GizmoMode.UniformScale, GizmoAxis.None, 0f)]
    [DataRow(GizmoMode.Rotate, GizmoAxis.None, 0f)]
    public void NativeRepeatedWraps_KeepKitbashMeshPreviewContinuousAndUndoable(GizmoMode mode, GizmoAxis axis, float yaw)
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            ActivateNativeInputViewport(viewport, mouse);
            using var device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                new PresentationParameters
                {
                    BackBufferWidth = 300, BackBufferHeight = 200, IsFullScreen = false,
                    DeviceWindowHandle = new WindowInteropHelper(Window.GetWindow(viewport)).Handle,
                    PresentationInterval = PresentInterval.Immediate
                });
            using var mesh = new MeshObject(new GraphicsCardGeometry(device), string.Empty)
            {
                VertexArray = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }.Select(position =>
                    new VertexPositionNormalTextureCustom
                    {
                        Position = new Vector4(position, 1), Normal = Vector3.UnitZ,
                        Tangent = Vector3.UnitX, BiNormal = Vector3.UnitY
                    }).ToArray(),
                IndexArray = [0, 1, 2]
            };
            mesh.BuildBoundingBox();
            var baseline = mesh.VertexArray.ToArray();
            var events = new Mock<IEventHub>();
            Action<SelectionChangedEvent>? selectionChanged = null;
            events.Setup(hub => hub.Register(It.IsAny<object>(), It.IsAny<Action<SelectionChangedEvent>>()))
                .Callback<object, Action<SelectionChangedEvent>>((_, handler) => selectionChanged = handler);
            events.Setup(hub => hub.Publish(It.IsAny<SelectionChangedEvent>()))
                .Callback<SelectionChangedEvent>(change => selectionChanged?.Invoke(change));
            using var selection = new SelectionManager(events.Object);
            var history = new CommandExecutor(events.Object);
            var services = new Mock<IServiceProvider>();
            services.Setup(provider => provider.GetService(typeof(TransformVertexCommand)))
                .Returns(() => new TransformVertexCommand(selection));
            var commands = new CommandFactory(services.Object, history);
            var keyboard = new Mock<IKeyboardComponent>();
            var resolver = new Mock<IDeviceResolver>();
            resolver.SetupGet(value => value.Device).Returns(device);
            using var camera = new ArcBallCamera(resolver.Object, keyboard.Object, mouse)
            {
                LookAt = mesh.MeshCenter, Yaw = MathHelper.ToRadians(yaw), Pitch = 0,
                ProjectionMatrixOverride = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1.5f, 0.1f, 1000)
            };
            camera.Initialize();
            using var component = new KitbashModelGizmoComponent(events.Object, keyboard.Object, mouse, camera,
                history, null!, resolver.Object, commands, selection);
            component.Initialize();
            var state = new ObjectSelectionState();
            state.ModifySelectionSingleObject(new DragTestMeshNode { Geometry = mesh }, onlyRemove: false);
            selection.SetState(state);

            mouse.SetCursorPosition(200, 100);
            PumpInput();
            mouse.Update(new GameTime());
            component.Gizmo.StartModalTransform(mode, mouse.Position(), confirmOnMouseRelease: mode != GizmoMode.Rotate);
            if (mode == GizmoMode.Rotate)
                component.Gizmo.ToggleTrackballRotation();
            component.Gizmo.ActiveAxis = axis;
            if (mode != GizmoMode.Rotate)
                NativeMouseInputEvent(0, 0, 0x0002);
            var wraps = 0;
            try
            {
                for (var step = 0; step < 320; step++)
                {
                    var physicalBefore = NativePosition(viewport);
                    var vertexBefore = mesh.VertexArray[0].Position;
                    NativeMouseInputEvent(step < 240 ? 4 : -4, 0, 0x0001 | 0x2000);
                    var undispatched = NativePosition(viewport);
                    Assert.IsTrue(undispatched.X >= 0 && undispatched.X < viewport.ActualWidth);
                    PumpInput(10);
                    mouse.Update(new GameTime());
                    component.Update(new GameTime());
                    Assert.IsTrue(component.Gizmo.IsInModalTransform, "The native left drag must stay active.");
                    if (Vector2.Distance(physicalBefore, NativePosition(viewport)) > 100) wraps++;
                    foreach (var vertex in mesh.VertexArray)
                        Assert.IsTrue(float.IsFinite(vertex.Position.X) && float.IsFinite(vertex.Position.Y) && float.IsFinite(vertex.Position.Z));
                    if (axis != GizmoAxis.None)
                        Assert.IsTrue(Vector4.Distance(mesh.VertexArray[0].Position, baseline[0].Position) < 0.001f,
                            $"Step {step}: horizontal motion moved the mesh along a view-aligned axis.");
                    else
                        Assert.IsTrue(Vector4.Distance(mesh.VertexArray[0].Position, vertexBefore) < 1,
                            $"Step {step}: a small native motion caused a large mesh jump.");
                }
            }
            finally
            {
                if (mode != GizmoMode.Rotate)
                    NativeMouseInputEvent(0, 0, 0x0004);
                PumpInput();
            }
            mouse.Update(new GameTime());
            if (mode == GizmoMode.Rotate)
                keyboard.Setup(input => input.IsKeyReleased(Microsoft.Xna.Framework.Input.Keys.Enter)).Returns(true);
            component.Update(new GameTime());
            Assert.IsTrue(wraps >= 2, $"Expected repeated native wrapping, observed {wraps}.");
            Assert.IsFalse(component.Gizmo.IsInModalTransform);
            Assert.IsNull(mouse.MouseOwner);
            if (axis == GizmoAxis.None)
            {
                Assert.IsTrue(history.CanUndo(), "The continuous preview must commit one undoable transform.");
                history.Undo();
            }
            for (var index = 0; index < baseline.Length; index++)
                Assert.IsTrue(Vector4.Distance(mesh.VertexArray[index].Position, baseline[index].Position) < 0.001f);
        });
    }

    private sealed class DragTestMeshNode : SceneNode, ISelectable, ITransformable
    {
        public MeshObject Geometry { get; set; } = null!;
        public bool IsSelectable { get; set; } = true;
        public Vector3 Position { get; set; }
        public Vector3 Scale { get; set; } = Vector3.One;
        public Quaternion Orientation { get; set; } = Quaternion.Identity;
        public Vector3 GetObjectCentre() => Geometry.MeshCenter;
        public override ISceneNode CreateCopyInstance() => new DragTestMeshNode();
    }
}
