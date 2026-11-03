using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

// The preview receiver has no terrain slope, shear or ocean displacement.
internal static class VfxSeaOverlay
{
    internal static bool IsFlatReceiverBasis(Matrix root, Vector3 inheritedScale)
    {
        if (!root.Decompose(out var scale, out _, out _) || !PositiveUniform(scale) || !PositiveUniform(inheritedScale)) return false;
        var determinant = root.Determinant();
        if (!float.IsFinite(determinant) || determinant <= 0) return false;
        var right = root.Right / scale.X;
        var up = root.Up / scale.Y;
        var backward = root.Backward / scale.Z;
        return Vector3.DistanceSquared(up, Vector3.Up) < .00000001f
            && Math.Abs(Vector3.Dot(right, up)) < .00001f
            && Math.Abs(Vector3.Dot(backward, up)) < .00001f
            && Math.Abs(Vector3.Dot(right, backward)) < .00001f
            && root.M14 == 0 && root.M24 == 0 && root.M34 == 0 && root.M44 == 1
            && float.IsFinite(root.M41) && float.IsFinite(root.M42) && float.IsFinite(root.M43);
    }

    private static bool PositiveUniform(Vector3 scale) => float.IsFinite(scale.X) && scale.X > 0
        && Math.Abs(scale.X - scale.Y) < .00001f && Math.Abs(scale.X - scale.Z) < .00001f;
}
