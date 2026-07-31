using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class GridShaderOffscreenTests
{
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
