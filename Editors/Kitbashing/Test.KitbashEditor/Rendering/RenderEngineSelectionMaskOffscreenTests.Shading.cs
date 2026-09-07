using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.RenderItems;
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

public partial class RenderEngineSelectionMaskOffscreenTests
{
    [Test]
    public void Wireframe_HidesRearEdgesBehindAnUnfilledFrontSurface()
    {
        const int size = 96;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var resolver = Mock.Of<IDeviceResolver>(value => value.Device == device);
        var camera = new ArcBallCamera(resolver, Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        var events = Mock.Of<IEventHub>();
        using var scoped = new ScopedResourceLibrary(resources, events, Mock.Of<IStandardDialogs>());
        using var renderer = new RenderEngineComponent(game, resources, camera, resolver,
            new ApplicationSettingsService(), new SceneRenderParametersStore(), events,
            new GridComponent(camera, resources, resolver)) { ShadingMode = ViewportShadingMode.Wireframe };
        renderer.Initialize();
        using var front = CreateMesh(device, animated: false);
        using var rear = CreateMesh(device, animated: false);
        var material = new SelectionOutlineCapabilityMaterial(scoped);
        material.GetCapability<AnimationCapability>().AnimationTransforms = [Matrix.Identity];
        var frontItem = new GeometryRenderItem(front, material, Matrix.Identity);
        frontItem.SetSelectionMask(true);
        renderer.AddRenderItem(RenderBuckedId.Normal, frontItem);
        using var target = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        using var mask = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target);
        renderer.Draw(new GameTime());
        device.SetRenderTarget(null);

        var withoutRear = Draw();
        Assert.That(withoutRear.Count(pixel => pixel.A > 0), Is.GreaterThan(0));
        renderer.AddRenderItem(RenderBuckedId.Normal, new GeometryRenderItem(rear, material,
            Matrix.CreateScale(0.5f) * Matrix.CreateTranslation(0, 0, 0.5f)));
        var withRear = Draw();
        Assert.That(withRear, Is.EqualTo(withoutRear),
            "Adding a fully occluded rear mesh must not expose selectable-looking wire edges.");
        renderer.ShadingSettings = renderer.ShadingSettings with { XRay = true };
        Assert.That(Draw(), Is.Not.EqualTo(withoutRear), "X-Ray must reveal that same rear mesh.");

        Color[] Draw()
        {
            device.SetRenderTargets(new RenderTargetBinding(target), new RenderTargetBinding(mask));
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            InvokeRender3DObjects(renderer);
            device.SetRenderTarget(null);
            var pixels = new Color[size * size];
            target.GetData(pixels);
            var maskPixels = new Color[size * size];
            mask.GetData(maskPixels);
            Assert.That(maskPixels.Count(pixel => pixel.R > 0), Is.GreaterThan(1000),
                "The outline mask must contain the filled silhouette, not individual wire edges.");
            return pixels;
        }
    }
}
