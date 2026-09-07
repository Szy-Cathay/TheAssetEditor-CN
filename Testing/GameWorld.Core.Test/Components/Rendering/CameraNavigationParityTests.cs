using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Navigation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Components.Rendering;

[NonParallelizable]
public class CameraNavigationParityTests
{
    [TestCase(ViewPresetType.Front, 0, 0, -1)]
    [TestCase(ViewPresetType.Back, 0, 0, 1)]
    [TestCase(ViewPresetType.Right, -1, 0, 0)]
    [TestCase(ViewPresetType.Left, 1, 0, 0)]
    [TestCase(ViewPresetType.Top, 0, -1, 0)]
    [TestCase(ViewPresetType.Bottom, 0, 1, 0)]
    public void AxisViews_AreExactlyPerpendicular(ViewPresetType preset, int x, int y, int z)
    {
        var (camera, _, _) = CreateCamera();
        var transition = new CameraTransition(camera);
        transition.StartTransition(preset);
        transition.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        var direction = Vector3.Normalize(camera.LookAt - camera.Position);
        Assert.That(Vector3.Distance(direction, new Vector3(x, y, z)), Is.LessThan(0.00001));
        Assert.That(float.IsFinite(camera.ViewMatrix.Determinant()), Is.True);
        Assert.That(Math.Abs(camera.ViewMatrix.Determinant()), Is.GreaterThan(0.99));
    }

    [TestCase(false, 0.8f, 0.32f)]
    [TestCase(true, 0.8f, 0.32f)]
    [TestCase(true, 0f, -MathHelper.PiOver2)]
    [TestCase(true, 0f, MathHelper.PiOver2)]
    public void Pan_FollowsPointerInViewPlaneIncludingTopAndBottom(bool orthographic, float yaw, float pitch)
    {
        var (camera, mouse, keyboard) = CreateCamera();
        camera.Yaw = yaw;
        camera.Pitch = pitch;
        camera.SetProjectionTypePreservingScale(orthographic ? ProjectionType.Orthographic : ProjectionType.Perspective);
        camera.OrthoSize = 3;
        var viewport = new Viewport(0, 0, 1000, 1000);
        var point = camera.LookAt;
        var before = viewport.Project(point, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity);
        mouse.Setup(value => value.DeltaPosition()).Returns(new Vector2(-100, -70));
        mouse.Setup(value => value.IsMouseButtonDown(MouseButton.Middle)).Returns(true);
        keyboard.Setup(value => value.IsKeyDown(Keys.RightShift)).Returns(true);
        camera.Update(mouse.Object, keyboard.Object);
        var after = viewport.Project(point, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity);
        Assert.That(after.X - before.X, Is.EqualTo(100).Within(0.2));
        Assert.That(after.Y - before.Y, Is.EqualTo(70).Within(0.2));
    }

    [Test]
    public void ProjectionToggleAndAxisView_PreserveVisibleScaleAfterOrthographicZoom()
    {
        var (camera, _, _) = CreateCamera();
        var viewport = new Viewport(0, 0, 1000, 1000);
        camera.Yaw = camera.Pitch = 0;
        var marker = Vector3.UnitX;
        var perspective = viewport.Project(marker, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity);
        camera.SetProjectionTypePreservingScale(ProjectionType.Orthographic);
        var orthographic = viewport.Project(marker, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity);
        Assert.That(Vector2.Distance(new Vector2(perspective.X, perspective.Y), new Vector2(orthographic.X, orthographic.Y)), Is.LessThan(0.01));
        camera.OrthoSize /= 3;
        var size = camera.OrthoSize;
        var transition = new CameraTransition(camera);
        transition.StartTransition(ViewPresetType.Top);
        transition.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        Assert.That(camera.OrthoSize, Is.EqualTo(size));
        camera.SetProjectionTypePreservingScale(ProjectionType.Perspective);
        Assert.That(camera.PerspectiveViewHeight, Is.EqualTo(size).Within(0.0001));
    }

    [Test]
    public void InterruptedNavigation_DoesNotResumeFromAStillHeldMiddleButton()
    {
        var (camera, mouse, keyboard) = CreateCamera();
        mouse.Setup(value => value.IsMouseButtonDown(MouseButton.Middle)).Returns(true);
        mouse.Setup(value => value.DeltaPosition()).Returns(new Vector2(-20, 0));
        camera.Update(mouse.Object, keyboard.Object);
        var yaw = camera.Yaw;
        mouse.Object.MouseOwner = null;
        mouse.Raise(value => value.CaptureInterrupted += null);
        camera.Update(mouse.Object, keyboard.Object);
        Assert.That(camera.Yaw, Is.EqualTo(yaw));
        Assert.That(mouse.Object.MouseOwner, Is.Null);
    }

    [Test]
    [Combinatorial]
    public void CancelMiddleNavigation_RestoresStartAndDoesNotRestartWhileHeld(
        [Values(0, 1, 2)] int mode, [Values(false, true)] bool orthographic, [Values(false, true)] bool rightClick)
    {
        var (camera, mouse, keyboard) = CreateCamera();
        camera.SetProjectionTypePreservingScale(orthographic ? ProjectionType.Orthographic : ProjectionType.Perspective);
        camera.AutoPerspectiveOnOrbit = orthographic;
        var start = (camera.Yaw, camera.Pitch, camera.Zoom, camera.OrthoSize, camera.LookAt, camera.CurrentProjectionType);
        mouse.Setup(value => value.IsMouseButtonDown(MouseButton.Middle)).Returns(true);
        keyboard.Setup(value => value.IsKeyDown(Keys.RightShift)).Returns(mode == 1);
        keyboard.Setup(value => value.IsKeyDown(Keys.RightControl)).Returns(mode == 2);
        mouse.Setup(value => value.DeltaPosition()).Returns(new Vector2(-20, 15));
        camera.Update(mouse.Object, keyboard.Object);
        if (rightClick) mouse.Setup(value => value.IsMouseButtonPressed(MouseButton.Right)).Returns(true);
        else keyboard.Setup(value => value.IsKeyPressed(Keys.Escape)).Returns(true);
        camera.Update(mouse.Object, keyboard.Object);
        Assert.That((camera.Yaw, camera.Pitch, camera.Zoom, camera.OrthoSize, camera.LookAt, camera.CurrentProjectionType), Is.EqualTo(start));
        Assert.That(mouse.Object.MouseOwner, Is.Null);
        mouse.Setup(value => value.IsMouseButtonPressed(MouseButton.Right)).Returns(false);
        keyboard.Setup(value => value.IsKeyPressed(Keys.Escape)).Returns(false);
        camera.Update(mouse.Object, keyboard.Object);
        Assert.That((camera.Yaw, camera.Pitch, camera.Zoom, camera.OrthoSize, camera.LookAt, camera.CurrentProjectionType), Is.EqualTo(start));
        Assert.That(mouse.Object.MouseOwner, Is.Null);
    }

    [Test]
    public void Orbit_StartingUpsideDown_UsesTheScreenHorizontalDirection()
    {
        var (camera, mouse, keyboard) = CreateCamera();
        camera.Yaw = 0;
        camera.Pitch = 2;
        mouse.Setup(value => value.IsMouseButtonPressed(MouseButton.Middle)).Returns(true);
        mouse.Setup(value => value.DeltaPosition()).Returns(new Vector2(-10, 0));
        camera.Update(mouse.Object, keyboard.Object);
        Assert.That(camera.Yaw, Is.EqualTo(-0.1f).Within(0.00001));
    }

    [Test]
    public void AxisTransition_FromUpsideDown_UsesTheShortestPitchPath()
    {
        var (camera, _, _) = CreateCamera();
        camera.Pitch = 3;
        var transition = new CameraTransition(camera);
        transition.StartTransition(ViewPresetType.Top);
        transition.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(125)));
        Assert.That(camera.Pitch, Is.LessThan(-2.3f));
        transition.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        Assert.That(camera.Pitch, Is.EqualTo(-MathHelper.PiOver2));
    }

    [Test]
    public void Orbit_CanPassBothPolesAndReturnWithoutLosingMotion()
    {
        var (camera, mouse, keyboard) = CreateCamera();
        mouse.Setup(value => value.IsMouseButtonDown(MouseButton.Middle)).Returns(true);
        foreach (var sign in new[] { -1, 1 })
        {
            camera.Pitch = sign * (MathHelper.PiOver2 - 0.05f);
            var start = camera.ViewMatrix;
            mouse.Setup(value => value.DeltaPosition()).Returns(new Vector2(0, sign * 20));
            camera.Update(mouse.Object, keyboard.Object);
            Assert.That(Math.Abs(camera.Pitch), Is.GreaterThan(MathHelper.PiOver2 + 0.1f));
            Assert.That(ViewPresets.DetectViewPreset(camera.Yaw, camera.Pitch), Is.Null);
            Assert.That(float.IsFinite(camera.ViewMatrix.Determinant()), Is.True);
            mouse.Setup(value => value.DeltaPosition()).Returns(new Vector2(0, -sign * 20));
            camera.Update(mouse.Object, keyboard.Object);
            Assert.That(Math.Abs(camera.ViewMatrix.M22 - start.M22), Is.LessThan(0.00001));
        }
    }

    [Test]
    public void FocusDuringAxisTransition_IsNotUndoneByTheNextAnimationFrame()
    {
        var (camera, _, _) = CreateCamera();
        var transition = new CameraTransition(camera);
        transition.StartTransition(ViewPresetType.Top);
        transition.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(25)));
        camera.FrameBounds(new BoundingBox(new Vector3(1, 2, 3), new Vector3(2, 3, 4)));
        var focus = camera.LookAt;
        var zoom = camera.Zoom;
        transition.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(100)));
        Assert.That(transition.IsTransitioning, Is.False);
        Assert.That(camera.LookAt, Is.EqualTo(focus));
        Assert.That(camera.Zoom, Is.EqualTo(zoom));
    }

    [Test]
    public void PerspectivePreset_AfterOrthographicZoom_PreservesVisibleScale()
    {
        var (camera, _, _) = CreateCamera();
        camera.SetProjectionTypePreservingScale(ProjectionType.Orthographic);
        camera.OrthoSize = 2;
        var transition = new CameraTransition(camera);
        transition.StartTransition(ViewPresetType.Perspective);
        transition.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        Assert.That(camera.PerspectiveViewHeight, Is.EqualTo(2).Within(0.00001));
    }

    [Test]
    public void WheelZoom_IsReversibleAndDoesNotChangeCameraDuringTransform()
    {
        var (camera, mouse, keyboard) = CreateCamera();
        var original = camera.Zoom;
        mouse.Setup(value => value.DeletaScrollWheel()).Returns(120);
        camera.Update(mouse.Object, keyboard.Object);
        mouse.Setup(value => value.DeletaScrollWheel()).Returns(-120);
        camera.Update(mouse.Object, keyboard.Object);
        Assert.That(camera.Zoom, Is.EqualTo(original).Within(0.0001));
        mouse.Setup(value => value.IsMouseOwner(camera)).Returns(false);
        mouse.Object.MouseOwner = Mock.Of<IGameComponent>();
        camera.Update(mouse.Object, keyboard.Object);
        Assert.That(camera.Zoom, Is.EqualTo(original).Within(0.0001));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PickingAndBoxSelection_UseLogicalViewportAndActualProjection(bool orthographic)
    {
        var (camera, _, _) = CreateCamera();
        camera.Yaw = camera.Pitch = 0;
        camera.SetProjectionTypePreservingScale(orthographic ? ProjectionType.Orthographic : ProjectionType.Perspective);
        var point = new Vector3(1, 0.5f, 0);
        var projected = camera.InputViewport.Project(point, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity);
        var mouse = new Vector2(projected.X, projected.Y);
        var ray = camera.CreateCameraRay(mouse);
        var distance = Vector3.Cross(point - ray.Position, ray.Direction).Length();
        Assert.That(distance, Is.LessThan(0.001));
        var selection = camera.UnprojectRectangle(new Rectangle((int)mouse.X - 10, (int)mouse.Y - 10, 20, 20));
        Assert.That(selection.Contains(point), Is.EqualTo(ContainmentType.Contains));
        Assert.That(selection.Contains(Vector3.Zero), Is.EqualTo(ContainmentType.Disjoint));
    }

    private static (ArcBallCamera, Mock<IMouseComponent>, Mock<IKeyboardComponent>) CreateCamera()
    {
        var game = new WpfGameMock();
        var resolver = new Mock<IDeviceResolver>();
        resolver.SetupGet(value => value.Device).Returns(game.GraphicsDevice);
        var mouse = new Mock<IMouseComponent>();
        mouse.SetupProperty(value => value.MouseOwner);
        mouse.Setup(value => value.IsMouseOwner(It.IsAny<IGameComponent>())).Returns(true);
        mouse.Setup(value => value.GetScreenSize()).Returns(new Vector2(1000));
        var keyboard = new Mock<IKeyboardComponent>();
        var camera = new ArcBallCamera(resolver.Object, keyboard.Object, mouse.Object);
        camera.Initialize();
        return (camera, mouse, keyboard);
    }
}
