using GameWorld.Core.Components;
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
public class RenderEngineRenderTargetLifecycleTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Draw_ReenablingSelectionOutlineAfterViewportReturnsToPreviousSizeDoesNotReuseDisposedTarget(bool surfaceDetail)
    {
        const int originalSize = 64;
        const int temporaryWidth = 80;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver
            .SetupGet(resolver => resolver.Device)
            .Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(
            Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            Mock.Of<IEventHub>(),
            new GridComponent(
                camera,
                resources,
                deviceResolver.Object)
            {
                ShowGrid = false
            });
        renderEngine.Initialize();
        if (surfaceDetail)
        {
            renderEngine.ShadingMode = ViewportShadingMode.Solid;
            renderEngine.ShadingSettings = new() { CavityStrength = 0.5f, ShadowStrength = 0.6f };
        }

        using var originalTarget = CreateHostTarget(
            device,
            originalSize,
            originalSize);
        using var temporaryTarget = CreateHostTarget(
            device,
            temporaryWidth,
            originalSize);

        try
        {
            DrawFrame(
                renderEngine,
                device,
                originalTarget,
                requestSelectionOutline: true);
            var geometryTarget = GetGeometryTarget();
            DrawFrame(
                renderEngine,
                device,
                temporaryTarget,
                requestSelectionOutline: false);
            if (surfaceDetail)
            {
                Assert.That(geometryTarget!.IsDisposed, Is.True);
                Assert.That(GetGeometryTarget()!.Width, Is.EqualTo(temporaryWidth));
            }
            DrawFrame(
                renderEngine,
                device,
                originalTarget,
                requestSelectionOutline: false);

            Assert.That(
                () => DrawFrame(
                    renderEngine,
                    device,
                    originalTarget,
                    requestSelectionOutline: true),
                Throws.Nothing);
            if (surfaceDetail)
                Assert.That(GetGeometryTarget()!.Width, Is.EqualTo(originalSize));
        }
        finally
        {
            device.SetRenderTarget(null);
            renderEngine.Dispose();
        }

        RenderTarget2D? GetGeometryTarget() => (RenderTarget2D?)typeof(RenderEngineComponent)
            .GetField("_surfaceGeometryTarget", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(renderEngine);
    }

    private static RenderTarget2D CreateHostTarget(
        GraphicsDevice device,
        int width,
        int height)
    {
        return new RenderTarget2D(
            device,
            width,
            height,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
    }

    private static void DrawFrame(
        RenderEngineComponent renderEngine,
        GraphicsDevice device,
        RenderTarget2D hostTarget,
        bool requestSelectionOutline)
    {
        device.SetRenderTarget(hostTarget);
        renderEngine.Update(new GameTime());
        if (requestSelectionOutline)
            renderEngine.RequestSelectionOutline();

        renderEngine.Draw(new GameTime());
    }
}
