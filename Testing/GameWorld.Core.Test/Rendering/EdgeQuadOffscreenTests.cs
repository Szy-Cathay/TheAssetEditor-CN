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
}
