using System.Reflection;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Navigation;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Moq;

namespace GameWorld.Core.Test.Components.Navigation;

public class NavigationGizmoComponentTests
{
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
