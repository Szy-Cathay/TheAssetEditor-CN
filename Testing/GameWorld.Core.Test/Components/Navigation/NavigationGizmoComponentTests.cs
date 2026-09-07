using System.Reflection;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Navigation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Components.Navigation;

[NonParallelizable]
public class NavigationGizmoComponentTests
{
    [Test]
    [Combinatorial]
    public void Update_AlignedAxisClick_ReversesViewAndReturns(
        [Values(ViewPresetType.Front, ViewPresetType.Back, ViewPresetType.Right,
            ViewPresetType.Left, ViewPresetType.Top, ViewPresetType.Bottom)] ViewPresetType initialView,
        [Values(1f, 1.5f)] float renderScale,
        [Values] bool orthographic)
    {
        var game = new WpfGameMock();
        var mouse = new Mock<IMouseComponent>();
        mouse.SetupProperty(value => value.MouseOwner);
        var viewport = game.GraphicsDevice.Viewport;
        mouse.Setup(value => value.GetScreenSize()).Returns(new Vector2(viewport.Width, viewport.Height) / renderScale);
        var center = new Vector2(viewport.Width - 55, 55) / renderScale;
        mouse.Setup(value => value.Position()).Returns(center);
        mouse.Setup(value => value.GetPressPosition(MouseButton.Left)).Returns(center);
        var keyboard = new Mock<IKeyboardComponent>();
        using var camera = new ArcBallCamera(new DeviceResolver(game), keyboard.Object, mouse.Object);
        camera.Initialize();
        (camera.Yaw, camera.Pitch) = ViewPresets.GetViewAngles(initialView);
        camera.LookAt = new Vector3(2, 3, 4);
        camera.Zoom = 7;
        camera.OrthoSize = 2;
        camera.CurrentProjectionType = orthographic ? ProjectionType.Orthographic : ProjectionType.Perspective;
        var viewHeight = orthographic ? camera.OrthoSize : camera.PerspectiveViewHeight;
        using var component = new NavigationGizmoComponent(camera, keyboard.Object, mouse.Object,
            null!, new DeviceResolver(game), null!);
        component.Initialize();
        var oppositeView = initialView switch
        {
            ViewPresetType.Front => ViewPresetType.Back,
            ViewPresetType.Back => ViewPresetType.Front,
            ViewPresetType.Right => ViewPresetType.Left,
            ViewPresetType.Left => ViewPresetType.Right,
            ViewPresetType.Top => ViewPresetType.Bottom,
            _ => ViewPresetType.Top
        };
        var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16));

        for (var click = 0; click < 4; click++)
        {
            // Use the real hit test, event subscription and camera transition.
            mouse.Setup(value => value.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
            component.Update(frame);
            Assert.That(mouse.Object.MouseOwner, Is.SameAs(component));
            mouse.Setup(value => value.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            mouse.Setup(value => value.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            component.Update(frame);
            mouse.Setup(value => value.IsMouseButtonReleased(MouseButton.Left)).Returns(false);
            component.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));

            var expectedView = click % 2 == 0 ? oppositeView : initialView;
            Assert.Multiple(() =>
            {
                Assert.That(ViewPresets.DetectViewPreset(camera.Yaw, camera.Pitch, 0.00001f), Is.EqualTo(expectedView), $"Click {click + 1}");
                Assert.That(component.CurrentView, Is.EqualTo(expectedView));
                Assert.That(camera.CurrentProjectionType, Is.EqualTo(ProjectionType.Orthographic));
                Assert.That(camera.LookAt, Is.EqualTo(new Vector3(2, 3, 4)));
                Assert.That(camera.OrthoSize, Is.EqualTo(viewHeight).Within(0.00001f));
                Assert.That(mouse.Object.MouseOwner, Is.Null);
            });
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Update_ManualNavigationDuringTransition_DoesNotOverwriteCamera(bool wheel)
    {
        var mouse = new Mock<IMouseComponent>();
        mouse.SetupProperty(component => component.MouseOwner);
        var keyboard = new Mock<IKeyboardComponent>();
        var camera = new ArcBallCamera(null!, keyboard.Object, mouse.Object);
        var component = new NavigationGizmoComponent(camera, keyboard.Object, mouse.Object, null!, null!, null!);
        var transition = new CameraTransition(camera);
        SetPrivateField(component, "_cameraTransition", transition);
        transition.StartTransition(ViewPresetType.Front);
        transition.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(20)));
        if (wheel) mouse.Setup(value => value.DeletaScrollWheel()).Returns(120);
        else mouse.Object.MouseOwner = camera;
        camera.Yaw = 0.55f;
        camera.LookAt = new Vector3(1, 2, 3);
        camera.Zoom = 7;

        component.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16)));

        Assert.That(transition.IsTransitioning, Is.False);
        Assert.That(camera.Yaw, Is.EqualTo(0.55f));
        Assert.That(camera.Zoom, Is.EqualTo(7));
        Assert.That(camera.LookAt, Is.EqualTo(new Vector3(1, 2, 3)));
    }

    [Test]
    public void Update_RapidAxisShortcuts_UsesLastRequestedView()
    {
        var mouse = new Mock<IMouseComponent>();
        mouse.SetupProperty(component => component.MouseOwner);
        var keyboard = new Mock<IKeyboardComponent>();
        var camera = new ArcBallCamera(null!, keyboard.Object, mouse.Object);
        var component = new NavigationGizmoComponent(camera, keyboard.Object, mouse.Object, null!, null!, null!);
        var transition = new CameraTransition(camera);
        SetPrivateField(component, "_cameraTransition", transition);
        transition.StartTransition(ViewPresetType.Front);
        keyboard.Setup(value => value.IsKeyReleased(Keys.NumPad3)).Returns(true);

        component.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16)));
        keyboard.Setup(value => value.IsKeyReleased(Keys.NumPad3)).Returns(false);
        component.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16)));
        transition.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));

        Assert.That(camera.Yaw, Is.EqualTo(MathHelper.PiOver2).Within(0.00001));
        Assert.That(component.IsInOrthoView, Is.True);
        Assert.That(component.CurrentView, Is.EqualTo(ViewPresetType.Right));
    }

    [Test]
    public void Update_LeftReleasedDuringCameraTransition_ReleasesMouseOnNextFrame()
    {
        var mouse = new Mock<IMouseComponent>();
        mouse.SetupProperty(component => component.MouseOwner);
        mouse.SetupSequence(component => component.IsMouseButtonReleased(MouseButton.Left))
            .Returns(true)
            .Returns(false);
        var keyboard = new Mock<IKeyboardComponent>();
        var camera = new ArcBallCamera(null!, keyboard.Object, mouse.Object);
        var component = new NavigationGizmoComponent(
            camera,
            keyboard.Object,
            mouse.Object,
            null!,
            null!,
            null!);
        var transition = new CameraTransition(camera);
        transition.StartTransition(ViewPresetType.Front);
        SetPrivateField(component, "_cameraTransition", transition);
        SetPrivateField(component, "_ownsMouseClick", true);
        mouse.Object.MouseOwner = component;
        var frame = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16.0));

        component.Update(frame);
        component.Update(frame);

        Assert.That(mouse.Object.MouseOwner, Is.Null);
        mouse.Verify(component => component.ClearStates(), Times.Once);
    }

    private static void SetPrivateField<T>(NavigationGizmoComponent component, string name, T value)
    {
        typeof(NavigationGizmoComponent)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);
    }
}
