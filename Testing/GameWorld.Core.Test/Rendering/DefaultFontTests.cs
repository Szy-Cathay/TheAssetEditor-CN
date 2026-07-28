using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

public class DefaultFontTests
{
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
}
