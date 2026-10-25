using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal static class VfxDecalVolume
{
    public static VfxPreviewModel Create(Vector2 pivot)
    {
        var vertices = new EffekseerVertex[8];
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = new()
            {
                Position = new((index & 1) - pivot.X, ((index >> 1) & 1) - pivot.Y, ((index >> 2) & 1) - .5f),
                Normal = Vector3.Forward, Tangent = Vector3.Right, Binormal = Vector3.Up,
            };
        return new(vertices, [0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6,
            0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7,
            0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5]);
    }
}
