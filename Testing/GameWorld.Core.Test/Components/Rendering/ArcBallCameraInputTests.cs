using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;

namespace GameWorld.Core.Test.Components.Rendering
{
    [TestFixture]
    public class ArcBallCameraInputTests
    {
        [Test]
        public void Update_MiddleMouseWhileAnotherComponentOwnsMouse_DoesNotStealOwnership()
        {
            var owner = new TestComponent();
            var mouse = new Mock<IMouseComponent>();
            mouse.SetupProperty(x => x.MouseOwner, owner);
            mouse.Setup(x => x.DeltaPosition()).Returns(Vector2.Zero);
            mouse.Setup(x => x.DeletaScrollWheel()).Returns(0);
            mouse.Setup(x => x.IsMouseButtonDown(MouseButton.Middle)).Returns(true);
            mouse.Setup(x => x.IsMouseOwner(It.IsAny<IGameComponent>()))
                .Returns<IGameComponent>(component => mouse.Object.MouseOwner == component);
            var keyboard = new Mock<IKeyboardComponent>();
            var camera = new ArcBallCamera(null!, keyboard.Object, mouse.Object);

            camera.Update(mouse.Object, keyboard.Object);

            Assert.That(mouse.Object.MouseOwner, Is.SameAs(owner));
            mouse.VerifySet(x => x.MouseOwner = camera, Times.Never);
        }

        [Test]
        public void Update_MiddleMouseOrbitFromAxisOrthographicView_SwitchesToPerspective()
        {
            var (camera, mouse, keyboard) = CreateCameraForMiddleMouse();
            camera.CurrentProjectionType = ProjectionType.Orthographic;
            camera.AutoPerspectiveOnOrbit = true;

            camera.Update(mouse.Object, keyboard.Object);

            Assert.Multiple(() =>
            {
                Assert.That(camera.CurrentProjectionType, Is.EqualTo(ProjectionType.Perspective));
                Assert.That(camera.AutoPerspectiveOnOrbit, Is.False);
            });
        }

        [Test]
        public void Update_MiddleMouseOrbitFromUserOrthographicView_RemainsOrthographic()
        {
            var (camera, mouse, keyboard) = CreateCameraForMiddleMouse();
            camera.CurrentProjectionType = ProjectionType.Orthographic;
            camera.AutoPerspectiveOnOrbit = false;

            camera.Update(mouse.Object, keyboard.Object);

            Assert.That(camera.CurrentProjectionType, Is.EqualTo(ProjectionType.Orthographic));
        }

        [Test]
        public void Update_ShiftMiddleMousePanFromAxisOrthographicView_RemainsOrthographic()
        {
            var (camera, mouse, keyboard) = CreateCameraForMiddleMouse();
            keyboard.Setup(x => x.IsKeyDown(Keys.LeftShift)).Returns(true);
            camera.CurrentProjectionType = ProjectionType.Orthographic;
            camera.AutoPerspectiveOnOrbit = true;

            camera.Update(mouse.Object, keyboard.Object);

            Assert.Multiple(() =>
            {
                Assert.That(camera.CurrentProjectionType, Is.EqualTo(ProjectionType.Orthographic));
                Assert.That(camera.AutoPerspectiveOnOrbit, Is.True);
            });
        }

        private static (ArcBallCamera camera, Mock<IMouseComponent> mouse, Mock<IKeyboardComponent> keyboard)
            CreateCameraForMiddleMouse()
        {
            var mouse = new Mock<IMouseComponent>();
            mouse.SetupProperty(x => x.MouseOwner);
            mouse.Setup(x => x.DeltaPosition()).Returns(new Vector2(5.0f, 3.0f));
            mouse.Setup(x => x.DeletaScrollWheel()).Returns(0);
            mouse.Setup(x => x.IsMouseButtonDown(MouseButton.Middle)).Returns(true);
            mouse.Setup(x => x.IsMouseOwner(It.IsAny<IGameComponent>()))
                .Returns<IGameComponent>(component => mouse.Object.MouseOwner == null ||
                                                     mouse.Object.MouseOwner == component);
            var keyboard = new Mock<IKeyboardComponent>();

            return (new ArcBallCamera(null!, keyboard.Object, mouse.Object), mouse, keyboard);
        }

        sealed class TestComponent : BaseComponent
        {
        }
    }
}
