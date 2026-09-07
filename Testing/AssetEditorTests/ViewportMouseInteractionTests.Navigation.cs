using System.Windows;
using System.Windows.Interop;
using Editors.KitbasherEditor.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Components.Rendering;
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
    [DataRow(0, false, false)]
    [DataRow(1, false, false)]
    [DataRow(2, false, false)]
    [DataRow(0, true, false)]
    [DataRow(1, true, false)]
    [DataRow(2, true, false)]
    [DataRow(0, false, true)]
    [DataRow(1, false, true)]
    [DataRow(2, false, true)]
    [DataRow(0, true, true)]
    [DataRow(1, true, true)]
    [DataRow(2, true, true)]
    public void NativeMiddleNavigation_AcrossViewportEdges_KeepsCameraMotionContinuous(int mode, bool orthographic, bool circle)
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
            var resolver = new Mock<IDeviceResolver>();
            resolver.SetupGet(value => value.Device).Returns(device);
            var keyboard = new Mock<IKeyboardComponent>();
            keyboard.Setup(value => value.IsKeyDown(Keys.RightShift)).Returns(mode == 1);
            keyboard.Setup(value => value.IsKeyDown(Keys.RightControl)).Returns(mode == 2);
            using var camera = new ArcBallCamera(resolver.Object, keyboard.Object, mouse);
            camera.Initialize();
            camera.Yaw = 0.4f;
            camera.Pitch = -0.2f;
            camera.Zoom = 20;
            camera.OrthoSize = 20;
            camera.CurrentProjectionType = orthographic ? ProjectionType.Orthographic : ProjectionType.Perspective;
            var events = new Mock<IEventHub>();
            using var selection = new SelectionManager(events.Object);
            selection.SetState(new EdgeSelectionState());
            using var gizmo = new KitbashModelGizmoComponent(events.Object, keyboard.Object, mouse, camera,
                null!, null!, resolver.Object, null!, selection);
            gizmo.Initialize();
            using var selectionInput = new KitbashSelectionInputComponent(mouse, keyboard.Object, camera, selection,
                resolver.Object, null!, null!, null!, gizmo);
            selectionInput.Settings.IsCircleSelection = circle;
            void UpdateNavigation()
            {
                selectionInput.CaptureSelectionGesture();
                camera.Update(mouse, keyboard.Object);
                gizmo.Update(new GameTime());
                selectionInput.Update(new GameTime());
            }
            mouse.SetCursorPosition(mode == 2 ? 150 : 296, mode == 2 ? 196 : 100);
            PumpInput();
            mouse.Update(new GameTime());
            NativeMouseInputEvent(0, 0, 0x0020);
            PumpInput();
            mouse.Update(new GameTime());
            UpdateNavigation();
            Assert.AreSame(camera, mouse.MouseOwner, "Native middle press must reach the camera through its actual viewport host.");
            Assert.IsTrue(viewport.IsMouseCaptured);
            var wraps = 0;
            try
            {
                for (var step = 0; step < 128; step++)
                {
                    var physical = NativePosition(viewport);
                    var yaw = camera.Yaw;
                    var scale = orthographic ? camera.OrthoSize : camera.Zoom;
                    var point = camera.LookAt;
                    var before = camera.InputViewport.Project(point, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity);
                    var direction = step < 64 ? 2 : -2;
                    NativeMouseInputEvent(mode == 2 ? 0 : direction, mode == 2 ? direction : 0, 0x0001 | 0x2000);
                    var undispatched = NativePosition(viewport);
                    Assert.IsTrue(undispatched.X >= 0 && undispatched.X < 300 && undispatched.Y >= 0 && undispatched.Y < 200,
                        $"Step {step}: cursor {undispatched}, captured={viewport.IsMouseCaptured}, owner={mouse.MouseOwner?.GetType().Name}.");
                    PumpInput(10);
                    mouse.Update(new GameTime());
                    var displacement = -mouse.DeltaPosition();
                    UpdateNavigation();
                    if (Vector2.Distance(physical, NativePosition(viewport)) > 100) wraps++;
                    Assert.IsTrue(displacement.Length() < 32, "Wrapping must not add a viewport-sized movement.");
                    if (mode == 0)
                        Assert.AreEqual(yaw + displacement.X * 0.01f, camera.Yaw, 0.00001f);
                    else if (mode == 1)
                    {
                        var after = camera.InputViewport.Project(point, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity);
                        Assert.AreEqual(displacement.X, after.X - before.X, 0.1f);
                        Assert.AreEqual(displacement.Y, after.Y - before.Y, 0.1f);
                    }
                    else
                        Assert.AreEqual(-displacement.Y * 0.01f,
                            MathF.Log((orthographic ? camera.OrthoSize : camera.Zoom) / scale), 0.00001f);
                }
            }
            finally
            {
                NativeMouseInputEvent(0, 0, 0x0040);
                PumpInput();
                mouse.Update(new GameTime());
                UpdateNavigation();
            }
            Assert.IsTrue(wraps >= 2, $"Expected forward and reverse wrapping, observed {wraps}.");
            Assert.IsNull(mouse.MouseOwner);
            Assert.IsFalse(viewport.IsMouseCaptured);
            Assert.AreEqual(circle, selectionInput.Settings.IsCircleSelection);
            Assert.IsFalse(selectionInput.IsCircleSelecting);
        });
    }
}
