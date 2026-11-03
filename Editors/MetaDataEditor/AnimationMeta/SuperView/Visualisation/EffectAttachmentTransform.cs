using Microsoft.Xna.Framework;

namespace Editors.AnimationMeta.SuperView.Visualisation;

internal static class EffectAttachmentTransform
{
    internal static Matrix Create(Quaternion orientation, Vector3 position, float scale, bool scalesWithParent,
        Matrix boneTransform, Matrix parentTransform)
    {
        var pose = CreatePose(orientation, position, boneTransform, parentTransform);
        if (scalesWithParent) scale *= parentTransform.Up.Length();
        return Matrix.CreateScale(scale) * pose;
    }

    internal static Matrix CreatePose(Quaternion orientation, Vector3 position, Matrix boneTransform, Matrix parentTransform)
    {
        var combined = Matrix.CreateFromQuaternion(orientation) * Matrix.CreateTranslation(position)
            * boneTransform * parentTransform;

        // Native EFFECT preserves translation, then rebuilds a proper rotation from Y and Z.
        var z = combined.Backward;
        var x = NormalizeNonZero(Vector3.Cross(combined.Up, z));
        var y = NormalizeNonZero(Vector3.Cross(z, x));
        z = NormalizeNonZero(z);
        var basis = new Matrix(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        var rotation = RotationFromBasis(basis);
        return Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(combined.Translation);
    }

    private static Vector3 NormalizeNonZero(Vector3 value)
    {
        var squared = value.LengthSquared();
        return squared > 0 ? value / MathF.Sqrt(squared) : value;
    }

    private static Quaternion RotationFromBasis(Matrix basis)
    {
        var trace = basis.M11 + basis.M22 + basis.M33 + 1;
        if (trace >= 1)
        {
            var root = MathF.Sqrt(trace);
            var factor = .5f / root;
            return new((basis.M23 - basis.M32) * factor, (basis.M31 - basis.M13) * factor,
                (basis.M12 - basis.M21) * factor, root * .5f);
        }

        var i = basis.M22 > basis.M11 ? 1 : 0;
        if (basis.M33 > Element(basis, i, i)) i = 2;
        var j = (i + 1) % 3;
        var k = (j + 1) % 3;
        var length = MathF.Sqrt(Element(basis, i, i) - Element(basis, j, j) - Element(basis, k, k) + 1);
        if (length == 0) length = .0001f;
        var reciprocal = .5f / length;
        var components = Vector3.Zero;
        SetComponent(ref components, i, length * .5f);
        SetComponent(ref components, j, (Element(basis, i, j) + Element(basis, j, i)) * reciprocal);
        SetComponent(ref components, k, (Element(basis, i, k) + Element(basis, k, i)) * reciprocal);
        return new(components, (Element(basis, j, k) - Element(basis, k, j)) * reciprocal);
    }

    private static float Element(Matrix value, int row, int column) => (row, column) switch
    {
        (0, 0) => value.M11, (0, 1) => value.M12, (0, 2) => value.M13,
        (1, 0) => value.M21, (1, 1) => value.M22, (1, 2) => value.M23,
        (2, 0) => value.M31, (2, 1) => value.M32, _ => value.M33,
    };

    private static void SetComponent(ref Vector3 value, int index, float component)
    {
        if (index == 0) value.X = component;
        else if (index == 1) value.Y = component;
        else value.Z = component;
    }
}
