using System.Reflection;
using GameWorld.Core.Components.Navigation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

[NonParallelizable]
public class NavigationGizmoRenderingTests
{
    [Test]
    public void ShapeTextures_ContainTransparentAndFeatheredEdgePixels()
    {
        var game = new WpfGameMock();
        using var gizmo = new NavigationGizmo(
            game.GraphicsDevice,
            null!,
            null!,
            null!);

        var circle = GetTexture(gizmo, "_circleTexture");
        var line = GetTexture(gizmo, "_lineTexture");

        Assert.Multiple(() =>
        {
            Assert.That(HasFeatheredEdge(circle), Is.True);
            Assert.That(HasFeatheredEdge(line), Is.True);
        });
    }

    private static Texture2D GetTexture(
        NavigationGizmo gizmo,
        string fieldName)
    {
        var field = typeof(NavigationGizmo).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return (Texture2D)field!.GetValue(gizmo)!;
    }

    private static bool HasFeatheredEdge(Texture2D texture)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        return pixels.Any(pixel =>
            pixel.A > 0 && pixel.A < byte.MaxValue) &&
            pixels.Any(pixel => pixel.A == 0) &&
            pixels.Any(pixel => pixel.A == byte.MaxValue);
    }
}
