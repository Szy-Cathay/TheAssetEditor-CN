using System;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Commands.Bone
{
    internal enum BoneTransformDeltaKind
    {
        Translation,
        Rotation,
        Scale
    }

    internal readonly struct BoneTransformDelta
    {
        private const float MinimumScaleMagnitude = 0.000001f;

        private BoneTransformDelta(
            BoneTransformDeltaKind kind,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scaleFactor,
            Vector3 pivot)
        {
            Kind = kind;
            Translation = translation;
            Rotation = rotation;
            ScaleFactor = scaleFactor;
            Pivot = pivot;
        }

        public BoneTransformDeltaKind Kind { get; }
        public Vector3 Translation { get; }
        public Quaternion Rotation { get; }
        public Vector3 ScaleFactor { get; }
        public Vector3 Pivot { get; }

        public static bool TryCreateTranslation(
            Vector3 translation,
            Vector3 pivot,
            out BoneTransformDelta delta)
        {
            if (!IsFinite(translation) || !IsFinite(pivot))
            {
                delta = default;
                return false;
            }

            delta = new BoneTransformDelta(
                BoneTransformDeltaKind.Translation,
                translation,
                Quaternion.Identity,
                Vector3.One,
                pivot);
            return true;
        }

        public static bool TryCreateRotation(
            Quaternion rotation,
            Vector3 pivot,
            out BoneTransformDelta delta)
        {
            if (!IsFinite(rotation) ||
                !IsFinite(pivot) ||
                rotation.LengthSquared() < MinimumScaleMagnitude * MinimumScaleMagnitude)
            {
                delta = default;
                return false;
            }

            rotation.Normalize();
            delta = new BoneTransformDelta(
                BoneTransformDeltaKind.Rotation,
                Vector3.Zero,
                rotation,
                Vector3.One,
                pivot);
            return true;
        }

        public static bool TryCreateRotation(
            Matrix rotationMatrix,
            Vector3 pivot,
            out BoneTransformDelta delta)
        {
            if (!BoneTransformMath.TryDecomposeSignedTrs(
                    rotationMatrix,
                    Vector3.One,
                    out var scale,
                    out var rotation,
                    out var translation) ||
                Vector3.DistanceSquared(scale, Vector3.One) > 0.000001f ||
                translation.LengthSquared() > 0.000001f)
            {
                delta = default;
                return false;
            }

            return TryCreateRotation(rotation, pivot, out delta);
        }

        public static bool TryCreateScale(
            Vector3 scaleFactor,
            Vector3 pivot,
            out BoneTransformDelta delta)
        {
            if (!IsFinite(scaleFactor) ||
                !IsFinite(pivot) ||
                Math.Abs(scaleFactor.X) < MinimumScaleMagnitude ||
                Math.Abs(scaleFactor.Y) < MinimumScaleMagnitude ||
                Math.Abs(scaleFactor.Z) < MinimumScaleMagnitude)
            {
                delta = default;
                return false;
            }

            delta = new BoneTransformDelta(
                BoneTransformDeltaKind.Scale,
                Vector3.Zero,
                Quaternion.Identity,
                scaleFactor,
                pivot);
            return true;
        }

        public bool IsNoOp()
        {
            return Kind switch
            {
                BoneTransformDeltaKind.Translation => Translation == Vector3.Zero,
                BoneTransformDeltaKind.Rotation =>
                    Math.Abs(Quaternion.Dot(Rotation, Quaternion.Identity)) >
                    0.999999f,
                BoneTransformDeltaKind.Scale => ScaleFactor == Vector3.One,
                _ => true
            };
        }

        public Matrix CreateWorldMatrix()
        {
            var operation = Kind switch
            {
                BoneTransformDeltaKind.Translation =>
                    Matrix.CreateTranslation(Translation),
                BoneTransformDeltaKind.Rotation =>
                    Matrix.CreateFromQuaternion(Rotation),
                BoneTransformDeltaKind.Scale =>
                    Matrix.CreateScale(ScaleFactor),
                _ => Matrix.Identity
            };

            return Matrix.CreateTranslation(-Pivot) *
                   operation *
                   Matrix.CreateTranslation(Pivot);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.X) &&
                   float.IsFinite(value.Y) &&
                   float.IsFinite(value.Z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.X) &&
                   float.IsFinite(value.Y) &&
                   float.IsFinite(value.Z) &&
                   float.IsFinite(value.W);
        }
    }
}
