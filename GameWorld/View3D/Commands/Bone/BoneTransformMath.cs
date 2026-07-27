using System;
using System.Linq;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Commands.Bone
{
    internal static class BoneTransformMath
    {
        public static bool TryDecomposeSignedTrs(
            Matrix matrix,
            Vector3 scaleSignHint,
            out Vector3 scale,
            out Quaternion rotation,
            out Vector3 translation)
        {
            scale = default;
            rotation = default;
            translation = default;
            if (!IsFinite(matrix) || !IsFinite(scaleSignHint))
                return false;

            var xAxis = new Vector3(matrix.M11, matrix.M12, matrix.M13);
            var yAxis = new Vector3(matrix.M21, matrix.M22, matrix.M23);
            var zAxis = new Vector3(matrix.M31, matrix.M32, matrix.M33);
            var absoluteScale = new Vector3(
                xAxis.Length(),
                yAxis.Length(),
                zAxis.Length());
            const float minimumScale = 0.000001f;
            if (absoluteScale.X < minimumScale ||
                absoluteScale.Y < minimumScale ||
                absoluteScale.Z < minimumScale)
            {
                return false;
            }

            var scaleSigns = new Vector3(
                Math.Sign(scaleSignHint.X),
                Math.Sign(scaleSignHint.Y),
                Math.Sign(scaleSignHint.Z));
            if (scaleSigns.X == 0 || scaleSigns.Y == 0 || scaleSigns.Z == 0)
                return false;

            var determinant = Vector3.Dot(Vector3.Cross(xAxis, yAxis), zAxis);
            if (!float.IsFinite(determinant) ||
                Math.Abs(determinant) < minimumScale)
            {
                return false;
            }

            var determinantSign = Math.Sign(determinant);
            var hintedSign = Math.Sign(
                scaleSigns.X * scaleSigns.Y * scaleSigns.Z);
            if (determinantSign != hintedSign)
                return false;

            scale = MultiplyComponents(absoluteScale, scaleSigns);
            var rotationMatrix = Matrix.Identity;
            rotationMatrix.M11 = xAxis.X / scale.X;
            rotationMatrix.M12 = xAxis.Y / scale.X;
            rotationMatrix.M13 = xAxis.Z / scale.X;
            rotationMatrix.M21 = yAxis.X / scale.Y;
            rotationMatrix.M22 = yAxis.Y / scale.Y;
            rotationMatrix.M23 = yAxis.Z / scale.Y;
            rotationMatrix.M31 = zAxis.X / scale.Z;
            rotationMatrix.M32 = zAxis.Y / scale.Z;
            rotationMatrix.M33 = zAxis.Z / scale.Z;

            var normalizedX = new Vector3(
                rotationMatrix.M11,
                rotationMatrix.M12,
                rotationMatrix.M13);
            var normalizedY = new Vector3(
                rotationMatrix.M21,
                rotationMatrix.M22,
                rotationMatrix.M23);
            var normalizedZ = new Vector3(
                rotationMatrix.M31,
                rotationMatrix.M32,
                rotationMatrix.M33);
            const float orthogonalityTolerance = 0.0005f;
            // Keyframes store TRS only, so shear must fail closed rather than be projected.
            if (Math.Abs(normalizedX.LengthSquared() - 1) > orthogonalityTolerance ||
                Math.Abs(normalizedY.LengthSquared() - 1) > orthogonalityTolerance ||
                Math.Abs(normalizedZ.LengthSquared() - 1) > orthogonalityTolerance ||
                Math.Abs(Vector3.Dot(normalizedX, normalizedY)) > orthogonalityTolerance ||
                Math.Abs(Vector3.Dot(normalizedX, normalizedZ)) > orthogonalityTolerance ||
                Math.Abs(Vector3.Dot(normalizedY, normalizedZ)) > orthogonalityTolerance)
            {
                return false;
            }

            rotation = Quaternion.CreateFromRotationMatrix(rotationMatrix);
            if (!IsFinite(rotation) || rotation.LengthSquared() < minimumScale)
                return false;
            rotation.Normalize();
            translation = matrix.Translation;

            var recomposed =
                Matrix.CreateScale(scale) *
                Matrix.CreateFromQuaternion(rotation) *
                Matrix.CreateTranslation(translation);
            return MatricesNear(matrix, recomposed, 0.0005f);
        }

        public static bool TryInvert(Matrix matrix, out Matrix inverse)
        {
            inverse = default;
            var determinant = matrix.Determinant();
            if (!float.IsFinite(determinant) ||
                Math.Abs(determinant) < 0.000001f)
            {
                return false;
            }

            inverse = Matrix.Invert(matrix);
            return IsFinite(inverse);
        }

        public static Vector3 MultiplyComponents(Vector3 left, Vector3 right)
        {
            return new Vector3(
                left.X * right.X,
                left.Y * right.Y,
                left.Z * right.Z);
        }

        public static bool MatricesNear(
            Matrix left,
            Matrix right,
            float tolerance)
        {
            var leftValues = GetMatrixValues(left);
            var rightValues = GetMatrixValues(right);
            for (var component = 0; component < leftValues.Length; component++)
            {
                var allowedDifference =
                    tolerance *
                    Math.Max(1, Math.Max(
                        Math.Abs(leftValues[component]),
                        Math.Abs(rightValues[component])));
                if (Math.Abs(leftValues[component] - rightValues[component]) >
                    allowedDifference)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsFinite(Matrix matrix)
        {
            return GetMatrixValues(matrix).All(float.IsFinite);
        }

        public static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.X) &&
                   float.IsFinite(value.Y) &&
                   float.IsFinite(value.Z);
        }

        public static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.X) &&
                   float.IsFinite(value.Y) &&
                   float.IsFinite(value.Z) &&
                   float.IsFinite(value.W);
        }

        private static float[] GetMatrixValues(Matrix matrix)
        {
            return
            [
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44
            ];
        }
    }
}
