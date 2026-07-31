using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class OutlinePostProcessOffscreenTests
{
    [Test]
    public void Outline_UsesSingleFeatheredRingWithoutFillingSelection()
    {
        const int size = 32;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>(
            "Shaders\\OutlinePostProcess");
        using var mask = CreateSquareMask(device, size);
        using var target = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.None);
        var quad = new QuadRenderer(device);

        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        effect.Parameters["ScreenTexture"].SetValue(mask);
        effect.Parameters["InverseResolution"].SetValue(
            new Vector2(1.0f / size, 1.0f / size));
        effect.Techniques["Outline"].Passes[0].Apply();
        quad.RenderQuad(device, -Vector2.One, Vector2.One);
        device.SetRenderTarget(null);

        var pixels = new Color[size * size];
        target.GetData(pixels);
        var row = size / 2;
        var outsideAlpha = pixels[row * size + 8].A;
        var featherPixel = pixels[row * size + 9];
        var featherAlpha = featherPixel.A;
        var selectedAlpha = pixels[row * size + 10].A;

        Assert.Multiple(() =>
        {
            Assert.That(outsideAlpha, Is.EqualTo(0));
            Assert.That(featherAlpha, Is.InRange(245, 255));
            Assert.That(featherPixel.R, Is.LessThanOrEqualTo(featherAlpha + 1));
            Assert.That(selectedAlpha, Is.EqualTo(0));
        });
    }

    [Test]
    public void Outline_SinglePixelSelectionDoesNotExpandBeyondAdjacentPixels()
    {
        const int size = 16;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var effect = game.Content.Load<Effect>(
            "Shaders\\OutlinePostProcess");
        using var mask = new Texture2D(device, size, size);
        var maskPixels = new Color[size * size];
        maskPixels[8 * size + 8] = Color.White;
        mask.SetData(maskPixels);
        using var target = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.None);
        var quad = new QuadRenderer(device);

        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        effect.Parameters["ScreenTexture"].SetValue(mask);
        effect.Parameters["InverseResolution"].SetValue(
            new Vector2(1.0f / size, 1.0f / size));
        effect.Techniques["Outline"].Passes[0].Apply();
        quad.RenderQuad(device, -Vector2.One, Vector2.One);
        device.SetRenderTarget(null);

        var output = new Color[size * size];
        target.GetData(output);
        var outlinedPixels = output.Count(pixel => pixel.A > 0);

        Assert.That(outlinedPixels, Is.LessThanOrEqualTo(8));
    }

    private static Texture2D CreateSquareMask(
        GraphicsDevice device,
        int size)
    {
        var pixels = new Color[size * size];
        for (var y = 10; y <= 21; y++)
        {
            for (var x = 10; x <= 21; x++)
                pixels[y * size + x] = Color.White;
        }

        var texture = new Texture2D(device, size, size);
        texture.SetData(pixels);
        return texture;
    }
}
