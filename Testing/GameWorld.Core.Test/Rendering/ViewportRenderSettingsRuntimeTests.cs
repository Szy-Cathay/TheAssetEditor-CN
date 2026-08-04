using System.Reflection;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class ViewportRenderSettingsRuntimeTests
{
    [Test]
    public void GridVisibilityOverride_PreservesCscCameraModeAcrossGlobalChanges()
    {
        var grid = new GridComponent(null!, null!, null!);
        grid.ShowGrid = false;

        grid.SetVisibilityOverride(false);
        grid.ShowGrid = true;

        Assert.That(grid.ShowGrid, Is.False);

        grid.SetVisibilityOverride(null);

        Assert.That(grid.ShowGrid, Is.True);
    }

    [Test]
    public void GlobalViewportChange_UpdatesVisualDefaultsWithoutTouchingFactionPreview()
    {
        var game = new WpfGameMock();
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver.SetupGet(value => value.Device)
            .Returns(game.GraphicsDevice);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(Mock.Of<IPackFileService>());
        resources.Initialize(game.GraphicsDevice, game.Content);
        var eventHub = new RecordingEventHub();
        var grid = new GridComponent(
            camera,
            resources,
            deviceResolver.Object);
        var scene = new SceneRenderParametersStore
        {
            FactionColour0 = new Vector3(0.1f, 0.2f, 0.3f),
            FactionColour1 = new Vector3(0.4f, 0.5f, 0.6f),
            FactionColour2 = new Vector3(0.7f, 0.8f, 0.9f)
        };
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            scene,
            eventHub,
            grid);
        renderEngine.Initialize();

        eventHub.Publish(new ViewportRenderSettingsChangedEvent(
            new ViewportRenderSettings(
                BackgroundColour.Custom,
                "12,34,56",
                true,
                false,
                "64,128,255",
                1.75f,
                45.0f,
                -15.0f,
                120.0f)));

        var background = (Color)typeof(RenderEngineComponent)
            .GetField(
                "_backgroundColour",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(renderEngine)!;
        Assert.Multiple(() =>
        {
            Assert.That(background, Is.EqualTo(new Color(12, 34, 56)));
            Assert.That(renderEngine.BackfaceCulling, Is.True);
            Assert.That(grid.ShowGrid, Is.False);
            Assert.That(grid.GridColur,
                Is.EqualTo(new Color(64, 128, 255).ToVector3()));
            Assert.That(scene.LightIntensityMult, Is.EqualTo(1.75f));
            Assert.That(scene.EnvLightRotationDegrees_Y, Is.EqualTo(45.0f));
            Assert.That(scene.DirLightRotationDegrees_X, Is.EqualTo(-15.0f));
            Assert.That(scene.DirLightRotationDegrees_Y, Is.EqualTo(120.0f));
            Assert.That(scene.FactionColour0,
                Is.EqualTo(new Vector3(0.1f, 0.2f, 0.3f)));
            Assert.That(scene.FactionColour1,
                Is.EqualTo(new Vector3(0.4f, 0.5f, 0.6f)));
            Assert.That(scene.FactionColour2,
                Is.EqualTo(new Vector3(0.7f, 0.8f, 0.9f)));
        });
    }

    [Test]
    public void BackfaceSetting_ChangesOffscreenRasterizedPixels()
    {
        const int size = 64;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver.SetupGet(value => value.Device).Returns(device);
        var camera = new ArcBallCamera(
            deviceResolver.Object,
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        var eventHub = new RecordingEventHub();
        var renderEngine = new RenderEngineComponent(
            game,
            resources,
            camera,
            deviceResolver.Object,
            new ApplicationSettingsService(),
            new SceneRenderParametersStore(),
            eventHub,
            new GridComponent(camera, resources, deviceResolver.Object));
        renderEngine.Initialize();
        using var target = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.None);
        using var effect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            World = Matrix.Identity,
            View = Matrix.Identity,
            Projection = Matrix.Identity
        };
        var vertices = new[]
        {
            new VertexPositionColor(new Vector3(-0.9f, -0.7f, 0), Color.White),
            new VertexPositionColor(new Vector3(-0.1f, -0.7f, 0), Color.White),
            new VertexPositionColor(new Vector3(-0.5f, 0.7f, 0), Color.White),
            new VertexPositionColor(new Vector3(0.1f, -0.7f, 0), Color.White),
            new VertexPositionColor(new Vector3(0.5f, 0.7f, 0), Color.White),
            new VertexPositionColor(new Vector3(0.9f, -0.7f, 0), Color.White)
        };

        var twoSidedPixels = DrawAndCountPixels(
            renderEngine,
            device,
            target,
            effect,
            vertices);
        eventHub.Publish(new ViewportRenderSettingsChangedEvent(
            ViewportRenderSettings.From(new ApplicationSettings()) with
            {
                SimulateGameBackfaces = true
            }));
        var gameLikePixels = DrawAndCountPixels(
            renderEngine,
            device,
            target,
            effect,
            vertices);

        Assert.Multiple(() =>
        {
            Assert.That(twoSidedPixels, Is.GreaterThan(0));
            Assert.That(gameLikePixels, Is.GreaterThan(0));
            Assert.That(
                gameLikePixels,
                Is.LessThan(twoSidedPixels * 0.65),
                "Game-like backface display must visibly remove one of the opposite-winding triangles.");
        });
    }

    private static int DrawAndCountPixels(
        RenderEngineComponent renderEngine,
        GraphicsDevice device,
        RenderTarget2D target,
        BasicEffect effect,
        VertexPositionColor[] vertices)
    {
        var states = (Dictionary<RasterizerStateEnum, RasterizerState>)
            typeof(RenderEngineComponent).GetField(
                "_rasterStates",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(renderEngine)!;
        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);
        device.RasterizerState = states[RasterizerStateEnum.Normal];
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(
                PrimitiveType.TriangleList,
                vertices,
                0,
                2);
        }
        device.SetRenderTarget(null);

        var pixels = new Color[target.Width * target.Height];
        target.GetData(pixels);
        return pixels.Count(pixel => pixel.A != 0);
    }

    private sealed class RecordingEventHub : IEventHub
    {
        private readonly Dictionary<Type, List<(object Owner, Delegate Callback)>>
            _callbacks = [];

        public void PublishGlobalEvent<T>(T e) => Publish(e);

        public void Publish<T>(T e)
        {
            if (!_callbacks.TryGetValue(typeof(T), out var callbacks))
                return;

            foreach (var callback in callbacks.ToList())
                ((Action<T>)callback.Callback)(e);
        }

        public void Register<T>(object owner, Action<T> action)
        {
            if (!_callbacks.TryGetValue(typeof(T), out var callbacks))
            {
                callbacks = [];
                _callbacks.Add(typeof(T), callbacks);
            }

            callbacks.Add((owner, action));
        }

        public void UnRegister(object owner)
        {
            foreach (var callbacks in _callbacks.Values)
                callbacks.RemoveAll(value => value.Owner == owner);
        }
    }
}
