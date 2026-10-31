using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal sealed partial class TotalWarVfxSimulation
{
    internal bool ModelTerrainBirthUnsupported { get; private set; }

    private bool PrepareModelTerrainBirth(EmitterState state, Matrix root, float emitterY, ref Vector3 position)
    {
        if (!state.Definition.ModelTerrainBirth) return false;
        if (state.Parent != null || _groundHeight is not { } ground || !float.IsFinite(ground)
            || !float.IsFinite(emitterY) || !FiniteModelTerrainRoot(root)) return false;
        var right = root.Right.LengthSquared(); var up = root.Up.LengthSquared(); var backward = root.Backward.LengthSquared();
        var determinant = root.Determinant();
        if (!(right > 0) || !(up > 0) || !(backward > 0)
            || !float.IsFinite(right) || !float.IsFinite(up) || !float.IsFinite(backward)
            || !(determinant > 0) || !float.IsFinite(determinant)) return false;
        if (!root.Decompose(out var scale, out var rotation, out var translation)
            || !float.IsFinite(translation.X) || !float.IsFinite(translation.Y) || !float.IsFinite(translation.Z)
            || !float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y) || !float.IsFinite(rotation.Z) || !float.IsFinite(rotation.W)
            || !PositiveUniformScale(scale) || Math.Abs(rotation.X) > .000001f || Math.Abs(rotation.Z) > .000001f
            || !MatrixClose(root, Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation)))
            return false;
        // Native replaces birth world Y after the random spawn area, preserving the raw emitter height.
        // A yaw-only root has an independent Y axis: invert that axis once without perturbing authored X/Z.
        var localY = (ground + emitterY - root.M42) / root.M22;
        if (!float.IsFinite(localY)) return false;
        position.Y = localY;
        return true;
    }

    private static bool FiniteModelTerrainRoot(Matrix root)
        => float.IsFinite(root.M11) && float.IsFinite(root.M12) && float.IsFinite(root.M13) && float.IsFinite(root.M14)
            && float.IsFinite(root.M21) && float.IsFinite(root.M22) && float.IsFinite(root.M23) && float.IsFinite(root.M24)
            && float.IsFinite(root.M31) && float.IsFinite(root.M32) && float.IsFinite(root.M33) && float.IsFinite(root.M34)
            && float.IsFinite(root.M41) && float.IsFinite(root.M42) && float.IsFinite(root.M43) && float.IsFinite(root.M44);
}
