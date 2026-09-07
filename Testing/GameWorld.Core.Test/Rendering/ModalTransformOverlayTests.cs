using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class ModalTransformOverlayTests
{
    [Test]
    public void ScaleGuideAndValue_RenderInsideViewportAtDoublePixelDensity()
    {
        var localization = LocalizationManager.Instance ?? new LocalizationManager();
        localization.LoadLanguage();
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var resolver = new Mock<IDeviceResolver>();
        resolver.SetupGet(value => value.Device).Returns(device);
        var mouse = new Mock<IMouseComponent>();
        mouse.Setup(value => value.GetScreenSize()).Returns(new Vector2(256, 128));
        mouse.Setup(value => value.Position()).Returns(new Vector2(200, 64));
        var keyboard = Mock.Of<IKeyboardComponent>();
        var camera = new ArcBallCamera(resolver.Object, keyboard, mouse.Object)
        {
            Yaw = 0,
            Pitch = 0,
            ProjectionMatrixOverride = Matrix.CreateOrthographic(10, 5, 0.1f, 1000)
        };
        camera.Initialize();
        var resources = new ResourceLibrary(Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        using var renderer = new RenderEngineComponent(game, resources, camera, resolver.Object,
            new ApplicationSettingsService(), new SceneRenderParametersStore(), Mock.Of<IEventHub>(),
            new GridComponent(camera, resources, resolver.Object) { ShowGrid = false });
        renderer.Initialize();
        using var gizmo = new Gizmo(camera, mouse.Object, device, renderer);
        var selection = new Mock<ITransformable>();
        selection.SetupGet(value => value.Orientation).Returns(Quaternion.Identity);
        gizmo.Selection.Add(selection.Object);
        gizmo.SetKeyboard(keyboard);
        gizmo.StartModalTransform(GizmoMode.NonUniformScale);
        gizmo.ActiveAxis = GizmoAxis.X;
        mouse.Setup(value => value.Position()).Returns(new Vector2(248, 120));
        gizmo.Update(new GameTime(), true);
        using var target = new RenderTarget2D(device, 512, 256, false, SurfaceFormat.Color, DepthFormat.None);
        device.SetRenderTarget(target);
        try
        {
            renderer.Draw(new GameTime());
            device.Clear(Color.Transparent);
            gizmo.Draw();
        }
        finally
        {
            device.SetRenderTarget(null);
        }
        var pixels = new Color[512 * 256];
        target.GetData(pixels);
        var text = pixels.Select((color, index) => (color, x: index % 512, y: index / 512))
            .Where(pixel => pixel.color.R > 180 && pixel.color.G > 180 && pixel.color.B < 60).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(pixels.Count(color => color.R > 200 && color.G > 200 && color.B > 200), Is.GreaterThan(30), "Dashed pivot guide must reach the render target.");
            Assert.That(text.Length, Is.GreaterThan(50), "The live value and axis must be visible.");
            Assert.That(text.All(pixel => pixel.x >= 8 && pixel.x < 504 && pixel.y >= 8 && pixel.y < 248), Is.True,
                "Both lines must stay inside the viewport at its bottom-right edge.");
        });
    }
}
