using Test.TestingUtility.Shared;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Test.Rendering;

public class DefaultFontTests
{
    [Test]
    public void ModalTransformLabels_RenderChineseAndDegreeGlyphs()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var font = game.Content.Load<SpriteFont>("Fonts//DefaultFont");
        var fallback = RenderText(device, font, "?");
        foreach (var character in "世界局部移动旋转缩放°自由表达式未完成或无效圈选：左键添加Ctrl+移除·中键/滚轮操作视图·小键盘+/-调整大小·W切换鼠标".Distinct())
        {
            var pixels = RenderText(device, font, character.ToString());
            Assert.That(pixels.Any(pixel => pixel.A > 0), Is.True, character.ToString());
            Assert.That(pixels.SequenceEqual(fallback), Is.False, character.ToString());
        }
    }

    [Test]
    public void MeasureString_UnsupportedCharacters_UsesFallbackGlyph()
    {
        var game = new WpfGameMock();
        var font = game.Content.Load<Microsoft.Xna.Framework.Graphics.SpriteFont>(
            "Fonts//DefaultFont");

        Assert.Multiple(() =>
        {
            Assert.That(font.DefaultCharacter, Is.EqualTo('?'));
            Assert.That(
                () => font.MeasureString("Command added: 编辑右侧栏属性"),
                Throws.Nothing);
        });
    }

    [Test]
    public void ViewportOverlayFont_DownsampledTextHasSmoothEdgesAtCompactSize()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var font = game.Content.Load<SpriteFont>(
            "Fonts//ViewportOverlayFont");
        var pixels = RenderText(device, font, "帧率：153");
        var visible = pixels
            .Select((pixel, index) => (pixel, index))
            .Where(item => item.pixel.A > 0)
            .ToArray();
        var minY = visible.Min(item => item.index / 160);
        var maxY = visible.Max(item => item.index / 160);
        var partialAlphaPixels = visible.Count(
            item => item.pixel.A < byte.MaxValue);
        var fallbackPixels = RenderText(device, font, "???153");
        var fallbackGlyph = RenderText(device, font, "?");
        var invalidChineseGlyphs = "帧率：物体顶点三角面"
            .Distinct()
            .Where(character =>
            {
                var glyphPixels = RenderText(
                    device,
                    font,
                    character.ToString());
                return !glyphPixels.Any(pixel => pixel.A > 0) ||
                    glyphPixels.SequenceEqual(fallbackGlyph);
            })
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(maxY - minY + 1, Is.InRange(9, 18));
            Assert.That(partialAlphaPixels, Is.GreaterThan(20));
            Assert.That(pixels.SequenceEqual(fallbackPixels), Is.False);
            Assert.That(invalidChineseGlyphs, Is.Empty);
        });
    }

    [Test]
    public void ViewportOverlayFont_ChineseGlyphsAreNotAllTheSameFallbackBox()
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var font = game.Content.Load<SpriteFont>(
            "Fonts//ViewportOverlayFont");
        var glyphPixels = "帧率物体顶点三角面"
            .Select(character => RenderText(
                device,
                font,
                character.ToString()))
            .ToArray();

        var allGlyphsMatch = glyphPixels
            .Skip(1)
            .All(pixels => pixels.SequenceEqual(glyphPixels[0]));

        Assert.That(allGlyphsMatch, Is.False);
    }

    private static Color[] RenderText(
        GraphicsDevice device,
        SpriteFont font,
        string text)
    {
        using var target = new RenderTarget2D(
            device,
            160,
            32,
            false,
            SurfaceFormat.Color,
            DepthFormat.None);
        using var spriteBatch = new SpriteBatch(device);

        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);
        spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.LinearClamp);
        spriteBatch.DrawString(
            font,
            text,
            new Vector2(2, 2),
            Color.White,
            0,
            Vector2.Zero,
            0.5f,
            SpriteEffects.None,
            0);
        spriteBatch.End();
        device.SetRenderTarget(null);

        var pixels = new Color[target.Width * target.Height];
        target.GetData(pixels);
        return pixels;
    }
}
