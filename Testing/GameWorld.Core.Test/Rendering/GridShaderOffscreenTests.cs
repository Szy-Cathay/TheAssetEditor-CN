using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.PackFiles;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class GridShaderOffscreenTests
{
    [Test]
    public void DefaultCamera_ProjectsReferenceAxesThroughTheRealGridShader()
    {
        const int width = 1156;
        const int height = 1046;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var resolver = Mock.Of<IDeviceResolver>(value => value.Device == device);
        using var camera = new ArcBallCamera(resolver, Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        using var grid = new GridComponent(camera, resources, resolver);
        grid.Initialize();
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24);
        var previousTargets = device.GetRenderTargets();
        var previousViewport = device.Viewport;
        try
        {
            device.SetRenderTarget(target);
            device.Clear(new Color(51, 51, 51));
            var parameters = CommonShaderParameterBuilder.Build(camera, new SceneRenderParametersStore(), height, width);
            var origin = device.Viewport.Project(Vector3.Zero, parameters.Projection, parameters.View, Matrix.Identity);
            var redEnd = device.Viewport.Project(Vector3.UnitX, parameters.Projection, parameters.View, Matrix.Identity);
            var blueEnd = device.Viewport.Project(Vector3.UnitZ, parameters.Projection, parameters.View, Matrix.Identity);
            grid.RenderGrid(device, parameters);
            device.SetRenderTargets(previousTargets);
            var pixels = new Color[width * height];
            target.GetData(pixels);
            var redPixels = new List<Vector2>();
            var bluePixels = new List<Vector2>();
            for (var y = 10; y < height - 10; y++)
            {
                for (var x = 10; x < width - 10; x++)
                {
                    var pixel = pixels[y * width + x];
                    if (pixel.R > pixel.G + 10 && pixel.R > pixel.B + 10)
                        redPixels.Add(new Vector2(x, y));
                    if (pixel.B > pixel.R + 10 && pixel.B > pixel.G + 10)
                        bluePixels.Add(new Vector2(x, y));
                }
            }
            var output = Environment.GetEnvironmentVariable("AECN_CAMERA_OUTPUT");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                using var stream = File.Create(Path.Combine(output, "reference-camera-grid.png"));
                target.SaveAsPng(stream, width, height);
            }
            // Slopes measured from the supplied reference, including the existing mirrored projection.
            Assert.Multiple(() =>
            {
                Assert.That(origin.X, Is.EqualTo(width / 2f).Within(0.01));
                Assert.That(origin.Y, Is.EqualTo(height / 2f).Within(0.01));
                Assert.That((redEnd.Y - origin.Y) / (redEnd.X - origin.X), Is.EqualTo(-0.1981).Within(0.003));
                Assert.That((blueEnd.Y - origin.Y) / (blueEnd.X - origin.X), Is.EqualTo(0.8344).Within(0.003));
                Assert.That(redPixels.Count, Is.GreaterThan(500));
                Assert.That(bluePixels.Count, Is.GreaterThan(500));
                Assert.That(FitSlope(redPixels), Is.EqualTo(-0.1981).Within(0.003));
                Assert.That(FitSlope(bluePixels), Is.EqualTo(0.8344).Within(0.003));
            });
        }
        finally
        {
            device.SetRenderTargets(previousTargets);
            device.Viewport = previousViewport;
        }
    }

    private static double FitSlope(List<Vector2> points)
    {
        var averageX = points.Average(point => (double)point.X);
        var averageY = points.Average(point => (double)point.Y);
        return points.Sum(point => (point.X - averageX) * (point.Y - averageY)) /
               points.Sum(point => (point.X - averageX) * (point.X - averageX));
    }

    [Test]
    public void DistantWhiteGrid_DoesNotFillPixelsBetweenVisibleGridLines()
    {
        const int size = 64;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>("Shaders\\GridShader");
        using var target = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);

        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        effect.Parameters["World"].SetValue(Matrix.Identity);
        effect.Parameters["View"].SetValue(Matrix.CreateLookAt(
            new Vector3(0, 10, 0),
            Vector3.Zero,
            Vector3.Forward));
        effect.Parameters["Projection"].SetValue(
            Matrix.CreateOrthographic(100, 100, 0.1f, 100));
        effect.Parameters["CameraPosition"].SetValue(
            new Vector3(0, 10, 0));
        effect.Parameters["GridColor"].SetValue(Vector3.One);
        effect.Parameters["CameraDistance"].SetValue(100f);
        effect.Parameters["IsOrthographic"].SetValue(1);
        effect.Techniques["Grid"].Passes[0].Apply();
        device.DrawUserPrimitives(
            PrimitiveType.TriangleStrip,
            CreateGroundQuad(50),
            0,
            2);
        device.SetRenderTarget(null);

        var pixels = new Color[size * size];
        target.GetData(pixels);
        var transparentPixels = 0;
        var partialPixelsArePremultiplied = true;
        for (var y = 16; y < 48; y++)
        {
            for (var x = 16; x < 48; x++)
            {
                var pixel = pixels[y * size + x];
                if (pixel.A == 0)
                    transparentPixels++;
                else if (pixel.A < byte.MaxValue && pixel.R > pixel.A + 1)
                    partialPixelsArePremultiplied = false;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(transparentPixels, Is.GreaterThan(256));
            Assert.That(partialPixelsArePremultiplied, Is.True);
        });
    }

    private static VertexPositionTexture[] CreateGroundQuad(float halfSize)
    {
        return
        [
            new VertexPositionTexture(
                new Vector3(-halfSize, 0, halfSize),
                Vector2.Zero),
            new VertexPositionTexture(
                new Vector3(halfSize, 0, halfSize),
                Vector2.Zero),
            new VertexPositionTexture(
                new Vector3(-halfSize, 0, -halfSize),
                Vector2.Zero),
            new VertexPositionTexture(
                new Vector3(halfSize, 0, -halfSize),
                Vector2.Zero)
        ];
    }
}
