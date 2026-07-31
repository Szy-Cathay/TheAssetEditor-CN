using GameWorld.Core.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class EdgeQuadOffscreenTests
{
    [Test]
    public void Draw_SelectedEdge_ProducesVisibleOrangePixels()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>("Shaders\\EdgeQuadShader");
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver.SetupGet(x => x.Device).Returns(device);
        var resourceLibrary = new Mock<IScopedResourceLibrary>();
        resourceLibrary
            .Setup(x => x.GetStaticEffect(ShaderTypes.EdgeQuad))
            .Returns(effect);

        using var renderer = new EdgeQuadInstanceMesh(
            deviceResolver.Object,
            resourceLibrary.Object);
        using var renderTarget = new RenderTarget2D(
            device,
            64,
            64,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);

        renderer.Update(
        [
            new EdgeData
            {
                P0 = new Vector3(-0.75f, 0.0f, 0.0f),
                P1 = new Vector3(0.75f, 0.0f, 0.0f),
                C0 = new Vector3(1.0f, 0.47f, 0.0f),
                C1 = new Vector3(1.0f, 0.47f, 0.0f),
                Width = 1.5f
            }
        ]);

        try
        {
            device.SetRenderTarget(renderTarget);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1.0f, 0);
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullNone;
            renderer.Draw(Matrix.Identity, Matrix.Identity, 64.0f, 64.0f, device);
        }
        finally
        {
            device.SetRenderTarget(null);
        }

        var pixels = new Color[64 * 64];
        renderTarget.GetData(pixels);

        Assert.Multiple(() =>
        {
            Assert.That(effect.Parameters["ViewportHeight"].GetValueSingle(), Is.EqualTo(64.0f));
            Assert.That(effect.Parameters["ViewportWidth"].GetValueSingle(), Is.EqualTo(64.0f));
            Assert.That(
                pixels.Any(pixel =>
                    pixel.A > 0 &&
                    pixel.R > 180 &&
                    pixel.G > 50 &&
                    pixel.B < 30),
                Is.True);
        });
    }

    [Test]
    public void Draw_AntialiasedEdgeUsesPremultipliedCoverage()
    {
        var pixels = RenderEdge(
            new Vector3(-0.75f, -0.35f, 0.0f),
            new Vector3(0.75f, 0.4f, 0.0f),
            1.25f);
        var antialiasedPixels = pixels
            .Where(pixel => pixel.A > 0 && pixel.A < byte.MaxValue)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(antialiasedPixels, Is.Not.Empty);
            Assert.That(
                antialiasedPixels.All(pixel =>
                    pixel.R <= pixel.A + 2 &&
                    pixel.G <= pixel.A + 2 &&
                    pixel.B <= pixel.A + 2),
                Is.True,
                "AlphaBlend requires antialiased RGB to be premultiplied by coverage.");
        });
    }

    [Test]
    public void Draw_SubpixelEdgeFadesInsteadOfBecomingAThickDash()
    {
        var longEdge = RenderEdge(
            new Vector3(-0.75f, 0.0f, 0.0f),
            new Vector3(0.75f, 0.0f, 0.0f),
            1.5f);
        var subpixelEdge = RenderEdge(
            new Vector3(-0.003f, 0.0f, 0.0f),
            new Vector3(0.003f, 0.0f, 0.0f),
            1.5f);
        var longCoverage = longEdge.Sum(pixel => pixel.A);
        var subpixelCoverage = subpixelEdge.Sum(pixel => pixel.A);

        Assert.That(
            subpixelCoverage,
            Is.LessThan(longCoverage * 0.08),
            "Edges shorter than one screen pixel should fade with their projected length.");
    }

    private static Color[] RenderEdge(
        Vector3 start,
        Vector3 end,
        float halfWidth)
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>("Shaders\\EdgeQuadShader");
        var deviceResolver = new Mock<IDeviceResolver>();
        deviceResolver.SetupGet(x => x.Device).Returns(device);
        var resourceLibrary = new Mock<IScopedResourceLibrary>();
        resourceLibrary
            .Setup(x => x.GetStaticEffect(ShaderTypes.EdgeQuad))
            .Returns(effect);
        using var renderer = new EdgeQuadInstanceMesh(
            deviceResolver.Object,
            resourceLibrary.Object);
        using var renderTarget = new RenderTarget2D(
            device,
            64,
            64,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        renderer.Update(
        [
            new EdgeData
            {
                P0 = start,
                P1 = end,
                C0 = new Vector3(1.0f, 0.47f, 0.0f),
                C1 = new Vector3(1.0f, 0.47f, 0.0f),
                Width = halfWidth
            }
        ]);

        try
        {
            device.SetRenderTarget(renderTarget);
            device.Clear(
                ClearOptions.Target | ClearOptions.DepthBuffer,
                Color.Transparent,
                1.0f,
                0);
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullNone;
            renderer.Draw(
                Matrix.Identity,
                Matrix.Identity,
                64.0f,
                64.0f,
                device);
        }
        finally
        {
            device.SetRenderTarget(null);
        }

        var pixels = new Color[64 * 64];
        renderTarget.GetData(pixels);
        return pixels;
    }
}
