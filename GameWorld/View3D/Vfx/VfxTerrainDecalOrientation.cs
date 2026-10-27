using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal static class VfxTerrainDecalOrientation
{
    public static Matrix BirthBasis(Vector3 degrees, Vector3 receiverNormal)
    {
        var authored = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.ToRadians(degrees.Z))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.ToRadians(degrees.Y))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.ToRadians(degrees.X));
        // Native caller supplies an independent receiver normal, not the generated spawn direction.
        var normal = receiverNormal;
        if (normal.Y >= .9999f) return Matrix.CreateFromQuaternion(authored);
        // Native 6be290 keeps +X when the supplied normal has zero XZ length, including a zero vector.
        var tangentLength = MathF.Sqrt(normal.X * normal.X + normal.Z * normal.Z);
        var axis = tangentLength > 0 ? new Vector3(normal.Z / tangentLength, 0, -normal.X / tangentLength) : Vector3.UnitX;
        var alignment = Quaternion.CreateFromAxisAngle(axis, MathF.Acos(normal.Y));
        return Matrix.CreateFromQuaternion(alignment * authored);
    }
}
