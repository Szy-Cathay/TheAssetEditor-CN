using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal static class VfxModelBirthOrientation
{
    public static Vector3 Apply(int kind, Vector3 sampled, Vector3 minimum, Vector3 maximum, Vector3 reserved)
    {
        if (kind != 1) return sampled;
        var x = minimum.X != maximum.X; var y = minimum.Y != maximum.Y; var z = minimum.Z != maximum.Z;
        if ((x ? 1 : 0) + (y ? 1 : 0) + (z ? 1 : 0) < 2) return sampled;

        // Native rigid birth samples XYZ independently. Reuse already consumed unused channels
        // so unrelated preview randomness stays unchanged; its seed is not the game's RNG seed.
        if (x && y) sampled.Y = minimum.Y + (maximum.Y - minimum.Y) * reserved.Y;
        if (z) sampled.Z = minimum.Z + (maximum.Z - minimum.Z) * (x && y ? reserved.Z : reserved.Y);
        return sampled;
    }
}
