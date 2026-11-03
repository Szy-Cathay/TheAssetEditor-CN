using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal static class VfxModelInstanceUv
{
    internal static Vector2 FromSeed(float seed)
    {
        var u = seed * 1013f;
        var v = seed * 2293f;
        return new(u - MathF.Floor(u), v - MathF.Floor(v));
    }
}
