using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class PreviewSurfaceShaderOffscreenTests
{
    [Test]
    public void LineShader_TriangleSurfacePreservesPremultipliedAlpha()
    {
        const int size = 64;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>("Shaders\\LineShader");
        using var target = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        var vertices = new[]
        {
            new VertexPositionColor(
                new Vector3(-0.8f, -0.8f, 0),
                new Color(64, 32, 16, 64)),
            new VertexPositionColor(
                new Vector3(0.8f, -0.8f, 0),
                new Color(64, 32, 16, 64)),
            new VertexPositionColor(
                new Vector3(0, 0.8f, 0),
                new Color(64, 32, 16, 64))
        };

        try
        {
            device.SetRenderTarget(target);
            device.Clear(
                ClearOptions.Target | ClearOptions.DepthBuffer,
                Color.Transparent,
                1,
                0);
            device.BlendState = BlendState.AlphaBlend;
            device.DepthStencilState = DepthStencilState.DepthRead;
            device.RasterizerState = RasterizerState.CullNone;
            effect.Parameters["World"].SetValue(Matrix.Identity);
            effect.Parameters["View"].SetValue(Matrix.Identity);
            effect.Parameters["Projection"].SetValue(Matrix.Identity);
            effect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(
                PrimitiveType.TriangleList,
                vertices,
                0,
                1);
        }
        finally
        {
            device.SetRenderTarget(null);
        }

        var pixels = new Color[size * size];
        target.GetData(pixels);
        var center = pixels[(size / 2) * size + size / 2];

        Assert.Multiple(() =>
        {
            Assert.That(center.A, Is.InRange(60, 68));
            Assert.That(center.R, Is.InRange(60, 68));
            Assert.That(center.G, Is.InRange(28, 36));
            Assert.That(center.B, Is.InRange(12, 20));
        });
    }
}
