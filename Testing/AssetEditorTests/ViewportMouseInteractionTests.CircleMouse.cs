using System.Windows;
using System.Windows.Interop;
using Editors.KitbasherEditor.Components;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;
using Shared.Core.Events;

namespace AssetEditorTests;

public partial class ViewportMouseInteractionTests
{
    [DataTestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    public void NativeCircleMouse_CtrlBrushRemovesAndReleasesViewportForNextAction(double scale)
    {
        WithViewport(scale, (viewport, mouse, _) =>
        {
            ActivateNativeInputViewport(viewport, mouse);
            using var device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef,
                new PresentationParameters
                {
                    BackBufferWidth = 300, BackBufferHeight = 200, IsFullScreen = false,
                    DeviceWindowHandle = new WindowInteropHelper(Window.GetWindow(viewport)).Handle,
                    PresentationInterval = PresentInterval.Immediate
                });
            var resolver = new Mock<IDeviceResolver>();
            resolver.SetupGet(value => value.Device).Returns(device);
            var keyboard = new Mock<IKeyboardComponent>();
            using var camera = new ArcBallCamera(resolver.Object, keyboard.Object, mouse);
            camera.Initialize();
            camera.LookAt = Vector3.Zero;
            camera.Yaw = camera.Pitch = 0;
            camera.ProjectionMatrixOverride = Matrix.CreateOrthographic(15, 10, 0.1f, 1000);
            using var mesh = new MeshObject(new GraphicsCardGeometry(device), string.Empty)
            {
                VertexArray = new[] { new Vector3(60, 60, 0.2f), new Vector3(240, 60, 0.2f), new Vector3(150, 160, 0.2f) }
                    .Select(point => new VertexPositionNormalTextureCustom
                    {
                        Position = new Vector4(camera.InputViewport.Unproject(point, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity), 1),
                        Normal = Vector3.UnitZ, Tangent = Vector3.UnitX, BiNormal = Vector3.UnitY
                    }).ToArray(),
                IndexArray = [0, 1, 2]
            };
            mesh.BuildBoundingBox();
            var events = new Mock<IEventHub>();
            using var selection = new SelectionManager(events.Object);
            selection.SetState(new VertexSelectionState(new DragTestMeshNode { Geometry = mesh }, 0));
            var history = new CommandExecutor(events.Object);
            var services = new Mock<IServiceProvider>();
            services.Setup(provider => provider.GetService(typeof(VertexSelectionCommand)))
                .Returns(() => new VertexSelectionCommand(selection));
            var commands = new CommandFactory(services.Object, history);
            using var gizmo = new KitbashModelGizmoComponent(events.Object, keyboard.Object, mouse, camera,
                history, null!, resolver.Object, commands, selection);
            gizmo.Initialize();
            using var input = new KitbashSelectionInputComponent(mouse, keyboard.Object, camera, selection,
                resolver.Object, commands, null!, null!, gizmo);
            input.Settings.IsCircleSelection = true;
            GetClipCursor(out var initialClip);
            void UpdateSelection()
            {
                mouse.Update(new GameTime());
                input.CaptureSelectionGesture();
                camera.Update(new GameTime());
                gizmo.Update(new GameTime());
                input.Update(new GameTime());
            }
            foreach (var remove in new[] { false, true })
            {
                keyboard.Setup(value => value.IsKeyDownOrReleased(Keys.LeftControl)).Returns(remove);
                mouse.SetCursorPosition(60, 60);
                PumpInput();
                UpdateSelection();
                NativeMouseInputEvent(0, 0, 0x0002);
                try
                {
                    PumpInput();
                    UpdateSelection();
                    Assert.IsTrue(input.IsCircleSelecting);
                    Assert.AreSame(input, mouse.MouseOwner);
                    mouse.SetCursorPosition(240, 60);
                    PumpInput();
                    UpdateSelection();
                }
                finally
                {
                    NativeMouseInputEvent(0, 0, 0x0004);
                    PumpInput();
                    UpdateSelection();
                }
                Assert.AreEqual(remove ? 0 : 2, selection.GetState().SelectionCount());
                Assert.IsFalse(input.IsCircleSelecting);
                Assert.IsTrue(input.Settings.IsCircleSelection);
                Assert.IsNull(mouse.MouseOwner);
                Assert.IsFalse(viewport.IsMouseCaptured);
                GetClipCursor(out var releasedClip);
                Assert.AreEqual(initialClip, releasedClip);
            }
            history.Undo();
            Assert.AreEqual(2, selection.GetState().SelectionCount());
            history.Undo();
            Assert.AreEqual(0, selection.GetState().SelectionCount());
            Assert.IsFalse(history.CanUndo());
            mouse.SetCursorPosition(340, 100);
            Assert.IsTrue(NativePosition(viewport).X > viewport.ActualWidth);
        });
    }
}
