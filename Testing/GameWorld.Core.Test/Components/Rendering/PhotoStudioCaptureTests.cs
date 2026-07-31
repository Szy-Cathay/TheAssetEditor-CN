using System.Reflection;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Components.Rendering;

[TestFixture]
[NonParallelizable]
public class PhotoStudioCaptureTests
{
    private static readonly Assembly s_gameWorldAssembly =
        typeof(RenderEngineComponent).Assembly;

    [TestCase(320, 180, 1.0f, 320, 180)]
    [TestCase(320, 180, 2.0f, 640, 360)]
    public void TryGetCaptureSize_ProducesExactDimensions(
        int width,
        int height,
        float scale,
        int expectedWidth,
        int expectedHeight)
    {
        var result = TryGetCaptureSize(width, height, scale);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Width, Is.EqualTo(expectedWidth));
            Assert.That(result.Height, Is.EqualTo(expectedHeight));
        });
    }

    [TestCase(0, 180, 1.0f)]
    [TestCase(320, 180, 0.0f)]
    [TestCase(320, 180, 1.5f)]
    [TestCase(10000, 10000, 2.0f)]
    public void TryGetCaptureSize_RejectsInvalidOrUnsupportedSize(
        int width,
        int height,
        float scale)
    {
        Assert.That(
            TryGetCaptureSize(width, height, scale).Success,
            Is.False);
    }

    [Test]
    public void PendingCapture_ConsumesRequestExactlyOnce()
    {
        var settingsType = GetRequiredType(
            "GameWorld.Core.Components.Rendering.SaveRenderImageSettings");
        var pendingType = GetRequiredType(
            "GameWorld.Core.Components.Rendering.PendingRenderCapture");
        var settings = Activator.CreateInstance(
            settingsType,
            "Screenshot",
            true,
            2.0f,
            @"C:\capture")!;
        var pending = Activator.CreateInstance(pendingType)!;
        var request = pendingType.GetMethod("Request");
        var consume = pendingType.GetMethod("Consume");
        Assert.That(request, Is.Not.Null);
        Assert.That(consume, Is.Not.Null);

        request!.Invoke(pending, [settings]);
        var first = consume!.Invoke(pending, null);
        var second = consume.Invoke(pending, null);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(settings));
            Assert.That(second, Is.Null);
        });
    }

    [Test]
    public void PendingCapture_ConsumesConsecutiveRequestsInOrder()
    {
        var settingsType = GetRequiredType(
            "GameWorld.Core.Components.Rendering.SaveRenderImageSettings");
        var pendingType = GetRequiredType(
            "GameWorld.Core.Components.Rendering.PendingRenderCapture");
        var firstSettings = Activator.CreateInstance(
            settingsType,
            "First",
            true,
            2.0f,
            @"C:\capture")!;
        var secondSettings = Activator.CreateInstance(
            settingsType,
            "Second",
            true,
            2.0f,
            @"C:\capture")!;
        var pending = Activator.CreateInstance(pendingType)!;
        var request = pendingType.GetMethod("Request");
        var consume = pendingType.GetMethod("Consume");
        Assert.That(request, Is.Not.Null);
        Assert.That(consume, Is.Not.Null);

        request!.Invoke(pending, [firstSettings]);
        request.Invoke(pending, [secondSettings]);
        var first = consume!.Invoke(pending, null);
        var second = consume.Invoke(pending, null);
        var third = consume.Invoke(pending, null);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(firstSettings));
            Assert.That(second, Is.SameAs(secondSettings));
            Assert.That(third, Is.Null);
        });
    }

    [Test]
    public void GraphicsStateSnapshot_RestoresStateAndRenderTarget()
    {
        using var resources = new GraphicsStateResources();
        var device = resources.Device;
        resources.ApplyExpectedState();

        var snapshotType = GetRequiredType(
            "GameWorld.Core.Components.Rendering.GraphicsDeviceStateSnapshot");
        var capture = snapshotType.GetMethod(
            "Capture",
            BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        var restore = snapshotType.GetMethod(
            "Restore",
            BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        Assert.That(capture, Is.Not.Null);
        Assert.That(restore, Is.Not.Null);
        var snapshot = capture!.Invoke(null, [device]);

        device.SetRenderTarget(resources.OtherTarget);
        device.Viewport = new Viewport(0, 0, 4, 4);
        device.ScissorRectangle = new Rectangle(0, 0, 4, 4);
        device.BlendState = BlendState.Opaque;
        device.BlendFactor = Color.White;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        device.SamplerStates[0] = SamplerState.LinearClamp;
        device.Textures[0] = null;
        device.Indices = null;

        restore!.Invoke(snapshot, [device]);

        Assert.Multiple(() =>
        {
            Assert.That(
                device.GetRenderTargets().Single().RenderTarget,
                Is.SameAs(resources.ExpectedTarget));
            Assert.That(
                device.Viewport,
                Is.EqualTo(resources.ExpectedViewport));
            Assert.That(
                device.ScissorRectangle,
                Is.EqualTo(resources.ExpectedScissor));
            Assert.That(
                device.BlendState,
                Is.SameAs(resources.ExpectedBlend));
            Assert.That(
                device.BlendFactor,
                Is.EqualTo(resources.ExpectedBlendFactor));
            Assert.That(
                device.DepthStencilState,
                Is.SameAs(resources.ExpectedDepth));
            Assert.That(
                device.RasterizerState,
                Is.SameAs(resources.ExpectedRasterizer));
            Assert.That(
                device.SamplerStates[0],
                Is.SameAs(resources.ExpectedSampler));
            Assert.That(
                device.Textures[0],
                Is.SameAs(resources.ExpectedTexture));
            Assert.That(device.Indices, Is.SameAs(resources.ExpectedIndices));
        });
    }

    [Test]
    public void BloomFilter_LoadSupportsAnIsolatedEffectClone()
    {
        var load = typeof(GameWorld.Core.Rendering.BloomFilter)
            .GetMethods()
            .SingleOrDefault(method =>
                method.Name == "Load" &&
                method.GetParameters().Any(parameter =>
                    parameter.Name == "cloneEffect" &&
                    parameter.ParameterType == typeof(bool)));

        Assert.That(load, Is.Not.Null);
    }

    [Test]
    public void PhotoCaptureSurface_KeepsTransparentBackgroundAndScenePixels()
    {
        var graphicsService = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphicsService.GraphicsDevice;
            using var texture = new Texture2D(device, 1, 1);
            using var spriteBatch = new SpriteBatch(device);
            texture.SetData([Color.White]);
            Action<GraphicsDevice> drawScene = _ =>
            {
                spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend);
                spriteBatch.Draw(
                    texture,
                    new Rectangle(6, 6, 4, 4),
                    Color.White);
                spriteBatch.End();
            };
            var surfaceType = GetRequiredType(
                "GameWorld.Core.Components.Rendering.PhotoCaptureSurface");
            var render = surfaceType.GetMethod(
                "Render",
                BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
            Assert.That(render, Is.Not.Null);

            using var target = (RenderTarget2D)render!.Invoke(
                null,
                [device, 16, 16, drawScene])!;
            var pixels = new Color[16 * 16];
            target.GetData(pixels);

            Assert.Multiple(() =>
            {
                Assert.That(pixels[0].A, Is.Zero);
                Assert.That(
                    pixels[8 * 16 + 8].A,
                    Is.EqualTo(255));
                Assert.That(
                    pixels.Count(pixel => pixel.A != 0),
                    Is.EqualTo(16));
            });
        }
        finally
        {
            graphicsService.Release();
        }
    }

    [Test]
    public void PhotoCaptureSceneRenderer_SkipsViewportHelpers()
    {
        var graphicsService = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphicsService.GraphicsDevice;
            var sceneItem = new CountingRenderItem(true);
            var helperItem = new CountingRenderItem(false);
            var rendererType = GetRequiredType(
                "GameWorld.Core.Components.Rendering.PhotoCaptureSceneRenderer");
            var draw = rendererType.GetMethod(
                "Draw",
                BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
            Assert.That(draw, Is.Not.Null);

            draw!.Invoke(
                null,
                [
                    device,
                    default(GameWorld.Core.Rendering.CommonShaderParameters),
                    new IRenderItem[] { sceneItem, helperItem },
                    RenderingTechnique.Normal,
                    RasterizerState.CullNone
                ]);

            Assert.Multiple(() =>
            {
                Assert.That(sceneItem.DrawCount, Is.EqualTo(1));
                Assert.That(helperItem.DrawCount, Is.Zero);
            });
        }
        finally
        {
            graphicsService.Release();
        }
    }

    private static CaptureSizeResult TryGetCaptureSize(
        int width,
        int height,
        float scale)
    {
        var type = GetRequiredType(
            "GameWorld.Core.Components.Rendering.RenderCaptureMath");
        var method = type.GetMethod(
            "TryGetCaptureSize",
            BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        object[] arguments = [width, height, scale, 0, 0];
        var success = (bool)method!.Invoke(null, arguments)!;

        return new CaptureSizeResult(
            success,
            (int)arguments[3]!,
            (int)arguments[4]!);
    }

    private static Type GetRequiredType(string name)
    {
        var type = s_gameWorldAssembly.GetType(name);
        Assert.That(type, Is.Not.Null, $"Missing production type {name}");
        return type!;
    }

    private readonly record struct CaptureSizeResult(
        bool Success,
        int Width,
        int Height);

    private sealed class CountingRenderItem(
        bool includeInPhotoCapture) : IRenderItem
    {
        public bool IncludeInPhotoCapture { get; } =
            includeInPhotoCapture;
        public int DrawCount { get; private set; }

        public void Draw(
            GraphicsDevice device,
            GameWorld.Core.Rendering.CommonShaderParameters parameters,
            RenderingTechnique renderingTechnique)
        {
            DrawCount++;
        }
    }

    private sealed class GraphicsStateResources : IDisposable
    {
        private readonly GraphicsDeviceServiceMock _graphicsService = new();

        public GraphicsDevice Device => _graphicsService.GraphicsDevice;
        public RenderTarget2D ExpectedTarget { get; }
        public RenderTarget2D OtherTarget { get; }
        public Viewport ExpectedViewport { get; } =
            new(1, 1, 6, 6);
        public Rectangle ExpectedScissor { get; } =
            new(1, 2, 5, 4);
        public BlendState ExpectedBlend { get; } =
            new()
            {
                ColorSourceBlend = Blend.SourceAlpha,
                ColorDestinationBlend =
                    Blend.InverseSourceAlpha
            };
        public Color ExpectedBlendFactor { get; } =
            new(10, 20, 30, 40);
        public DepthStencilState ExpectedDepth { get; } =
            new()
            {
                DepthBufferEnable = true,
                StencilEnable = true
            };
        public RasterizerState ExpectedRasterizer { get; } =
            new()
            {
                CullMode = CullMode.CullClockwiseFace,
                ScissorTestEnable = true
            };
        public SamplerState ExpectedSampler { get; } =
            new()
            {
                Filter = TextureFilter.Point,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap
            };
        public Texture2D ExpectedTexture { get; }
        public IndexBuffer ExpectedIndices { get; }

        public GraphicsStateResources()
        {
            ExpectedTarget = new RenderTarget2D(
                Device,
                8,
                8,
                false,
                SurfaceFormat.Color,
                DepthFormat.Depth24);
            OtherTarget = new RenderTarget2D(
                Device,
                4,
                4,
                false,
                SurfaceFormat.Color,
                DepthFormat.Depth24);
            ExpectedTexture = new Texture2D(
                Device,
                1,
                1);
            ExpectedTexture.SetData([Color.Red]);
            ExpectedIndices = new IndexBuffer(
                Device,
                IndexElementSize.SixteenBits,
                3,
                BufferUsage.None);
            ExpectedIndices.SetData<short>([0, 1, 2]);
        }

        public void ApplyExpectedState()
        {
            Device.SetRenderTarget(ExpectedTarget);
            Device.Viewport = ExpectedViewport;
            Device.ScissorRectangle = ExpectedScissor;
            Device.BlendState = ExpectedBlend;
            Device.BlendFactor = ExpectedBlendFactor;
            Device.DepthStencilState = ExpectedDepth;
            Device.RasterizerState = ExpectedRasterizer;
            Device.SamplerStates[0] = ExpectedSampler;
            Device.Textures[0] = ExpectedTexture;
            Device.Indices = ExpectedIndices;
        }

        public void Dispose()
        {
            Device.SetRenderTarget(null);
            Device.Textures[0] = null;
            Device.Indices = null;
            ExpectedIndices.Dispose();
            ExpectedTexture.Dispose();
            OtherTarget.Dispose();
            ExpectedTarget.Dispose();
            ExpectedSampler.Dispose();
            ExpectedRasterizer.Dispose();
            ExpectedDepth.Dispose();
            ExpectedBlend.Dispose();
            _graphicsService.Release();
        }
    }
}
