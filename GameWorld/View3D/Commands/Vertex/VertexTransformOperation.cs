using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace GameWorld.Core.Commands.Vertex
{
    internal enum VertexTransformOperationMode
    {
        Translate,
        Rotate,
        Scale
    }

    internal readonly record struct VertexTransformOperation(
        VertexTransformOperationMode Mode,
        Matrix Transform,
        Vector3 PivotPoint);

    internal readonly record struct VertexTransformMeshResult(
        MeshObject Geometry,
        int FirstModifiedVertex,
        int LastModifiedVertex)
    {
        public bool HasModifiedVertices => LastModifiedVertex >= FirstModifiedVertex;
    }

    internal static class VertexTransformOperationApplier
    {
        internal const float MinimumScaleAxisSafetyMagnitude = 0.001f;
        internal const float MaximumScaleAxisSafetyConditionNumber = 1000.0f;
        internal const float MaximumScalePositionRoundTripError = 0.00001f;

        public static bool TryApply(
            IReadOnlyList<MeshObject> geometryList,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformOperation operation,
            bool inverse,
            out IReadOnlyList<VertexTransformMeshResult> results,
            bool validateScalePositionRoundTrip)
        {
            if (!IsStructurallyValid(
                    geometryList,
                    selectionState,
                    affectedVertexIndices,
                    falloffWeights,
                    operation) ||
                (validateScalePositionRoundTrip &&
                 operation.Mode == VertexTransformOperationMode.Scale &&
                 !AreAffectedScalePositionsReversible(
                     geometryList,
                     selectionState,
                     affectedVertexIndices,
                     falloffWeights,
                     operation)))
            {
                results = Array.Empty<VertexTransformMeshResult>();
                return false;
            }

            var appliedResults = new List<VertexTransformMeshResult>(geometryList.Count);
            foreach (var geometry in geometryList)
            {
                var firstModifiedVertex = int.MaxValue;
                var lastModifiedVertex = -1;
                ApplyToMesh(
                    geometry,
                    selectionState,
                    affectedVertexIndices,
                    falloffWeights,
                    operation,
                    inverse,
                    ref firstModifiedVertex,
                    ref lastModifiedVertex);
                appliedResults.Add(new VertexTransformMeshResult(
                    geometry,
                    firstModifiedVertex,
                    lastModifiedVertex));
            }

            results = appliedResults;
            return true;
        }

        public static bool AreStructurallyValid(
            IReadOnlyList<MeshObject> geometryList,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            IReadOnlyList<VertexTransformOperation> operations)
        {
            foreach (var operation in operations)
            {
                if (!IsStructurallyValid(
                    geometryList,
                    selectionState,
                    affectedVertexIndices,
                    falloffWeights,
                    operation))
                {
                    return false;
                }
            }

            return true;
        }

        static bool IsStructurallyValid(
            IReadOnlyList<MeshObject> geometryList,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformOperation operation)
        {
            if (!Enum.IsDefined(operation.Mode) ||
                !IsFinite(operation.PivotPoint) ||
                (operation.Mode == VertexTransformOperationMode.Scale &&
                 !PassesScaleAxisSafetyGuards(operation.Transform)) ||
                !IsInvertible(operation.Transform) ||
                !TryCreateReplayMatrix(operation.Transform, operation.PivotPoint, false, out _) ||
                !TryCreateReplayMatrix(operation.Transform, operation.PivotPoint, true, out _))
            {
                return false;
            }

            if (selectionState.Mode == GeometrySelectionMode.Vertex)
            {
                if (selectionState is not VertexSelectionState vertexSelectionState)
                {
                    return false;
                }

                foreach (var geometry in geometryList)
                {
                    if (vertexSelectionState.VertexWeights.Count > geometry.VertexCount())
                        return false;

                    for (var vertexIndex = 0; vertexIndex < vertexSelectionState.VertexWeights.Count; vertexIndex++)
                    {
                        var weight = vertexSelectionState.VertexWeights[vertexIndex];
                        if (!IsValidWeightedTransform(
                            weight,
                            operation,
                            operation.PivotPoint))
                        {
                            return false;
                        }
                    }
                }
            }
            else if (affectedVertexIndices != null &&
                     falloffWeights != null &&
                     falloffWeights.Count > 0)
            {
                foreach (var geometry in geometryList)
                {
                    for (var vertexIndex = 0; vertexIndex < geometry.VertexCount(); vertexIndex++)
                    {
                        if (!falloffWeights.TryGetValue(vertexIndex, out var weight))
                            continue;

                        if (!IsValidWeightedTransform(
                            weight,
                            operation,
                            operation.PivotPoint))
                        {
                            return false;
                        }
                    }
                }
            }
            else if (affectedVertexIndices != null)
            {
                foreach (var geometry in geometryList)
                {
                    foreach (var vertexIndex in affectedVertexIndices)
                    {
                        if (vertexIndex < 0 || vertexIndex >= geometry.VertexCount())
                            return false;
                    }
                }
            }

            return true;
        }

        static bool AreAffectedScalePositionsReversible(
            IReadOnlyList<MeshObject> geometryList,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformOperation operation)
        {
            // This second, read-only affected-vertex pass keeps rejection atomic.
            foreach (var geometry in geometryList)
            {
                if (selectionState.Mode == GeometrySelectionMode.Vertex)
                {
                    var vertexSelectionState = (VertexSelectionState)selectionState;
                    for (var vertexIndex = 0; vertexIndex < vertexSelectionState.VertexWeights.Count; vertexIndex++)
                    {
                        var weight = vertexSelectionState.VertexWeights[vertexIndex];
                        if (weight == 0)
                            continue;

                        if (!IsScalePositionReversible(
                            geometry.VertexArray[vertexIndex].Position,
                            CreateWeightedTransform(operation, weight),
                            operation.PivotPoint))
                        {
                            return false;
                        }
                    }
                }
                else if (affectedVertexIndices != null &&
                         falloffWeights != null &&
                         falloffWeights.Count > 0)
                {
                    for (var vertexIndex = 0; vertexIndex < geometry.VertexCount(); vertexIndex++)
                    {
                        if (!falloffWeights.TryGetValue(vertexIndex, out var weight) || weight == 0)
                            continue;

                        if (!IsScalePositionReversible(
                            geometry.VertexArray[vertexIndex].Position,
                            CreateWeightedTransform(operation, weight),
                            operation.PivotPoint))
                        {
                            return false;
                        }
                    }
                }
                else if (affectedVertexIndices != null)
                {
                    foreach (var vertexIndex in affectedVertexIndices)
                    {
                        if (!IsScalePositionReversible(
                            geometry.VertexArray[vertexIndex].Position,
                            operation.Transform,
                            operation.PivotPoint))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    for (var vertexIndex = 0; vertexIndex < geometry.VertexCount(); vertexIndex++)
                    {
                        if (!IsScalePositionReversible(
                            geometry.VertexArray[vertexIndex].Position,
                            operation.Transform,
                            operation.PivotPoint))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        static bool IsScalePositionReversible(
            Vector4 position,
            Matrix transform,
            Vector3 pivotPoint)
        {
            if (!TryCreateReplayMatrix(transform, pivotPoint, inverse: false, out var forward) ||
                !TryCreateReplayMatrix(transform, pivotPoint, inverse: true, out var reverse) ||
                !TryTransformPosition(position, forward, out var transformed) ||
                !TryTransformPosition(transformed, reverse, out var roundTrip))
            {
                return false;
            }

            return
                MathF.Abs(roundTrip.X - position.X) <= MaximumScalePositionRoundTripError &&
                MathF.Abs(roundTrip.Y - position.Y) <= MaximumScalePositionRoundTripError &&
                MathF.Abs(roundTrip.Z - position.Z) <= MaximumScalePositionRoundTripError;
        }

        static bool TryTransformPosition(
            Vector4 position,
            Matrix transform,
            out Vector4 transformed)
        {
            transformed = Vector4.Transform(position, transform);
            transformed.X /= transformed.W;
            transformed.Y /= transformed.W;
            transformed.Z /= transformed.W;
            transformed.W = 1;
            return IsFinite(transformed);
        }

        static bool IsValidWeightedTransform(
            float weight,
            VertexTransformOperation operation,
            Vector3 pivotPoint)
        {
            if (!float.IsFinite(weight))
                return false;
            if (weight == 0)
                return true;

            return TryCreateWeightedTransform(operation, weight, out var weightedTransform) &&
                   (operation.Mode != VertexTransformOperationMode.Scale ||
                    PassesScaleAxisSafetyGuards(weightedTransform)) &&
                   IsInvertible(weightedTransform) &&
                   TryCreateReplayMatrix(weightedTransform, pivotPoint, false, out _) &&
                   TryCreateReplayMatrix(weightedTransform, pivotPoint, true, out _);
        }

        static void ApplyToMesh(
            MeshObject geometry,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformOperation operation,
            bool inverse,
            ref int firstModifiedVertex,
            ref int lastModifiedVertex)
        {
            if (selectionState.Mode == GeometrySelectionMode.Vertex)
            {
                var vertexSelectionState = (VertexSelectionState)selectionState;
                for (var vertexIndex = 0; vertexIndex < vertexSelectionState.VertexWeights.Count; vertexIndex++)
                {
                    var weight = vertexSelectionState.VertexWeights[vertexIndex];
                    if (weight == 0)
                        continue;

                    ApplyVertex(
                        geometry,
                        vertexIndex,
                        CreateWeightedTransform(operation, weight),
                        operation,
                        inverse);
                    IncludeVertex(vertexIndex, ref firstModifiedVertex, ref lastModifiedVertex);
                }
            }
            else if (affectedVertexIndices != null &&
                     falloffWeights != null &&
                     falloffWeights.Count > 0)
            {
                for (var vertexIndex = 0; vertexIndex < geometry.VertexCount(); vertexIndex++)
                {
                    if (!falloffWeights.TryGetValue(vertexIndex, out var weight) || weight == 0)
                        continue;

                    ApplyVertex(
                        geometry,
                        vertexIndex,
                        CreateWeightedTransform(operation, weight),
                        operation,
                        inverse);
                    IncludeVertex(vertexIndex, ref firstModifiedVertex, ref lastModifiedVertex);
                }
            }
            else if (affectedVertexIndices != null)
            {
                foreach (var vertexIndex in affectedVertexIndices)
                {
                    ApplyVertex(
                        geometry,
                        vertexIndex,
                        operation.Transform,
                        operation,
                        inverse);
                    IncludeVertex(vertexIndex, ref firstModifiedVertex, ref lastModifiedVertex);
                }
            }
            else
            {
                for (var vertexIndex = 0; vertexIndex < geometry.VertexCount(); vertexIndex++)
                {
                    ApplyVertex(
                        geometry,
                        vertexIndex,
                        operation.Transform,
                        operation,
                        inverse);
                    IncludeVertex(vertexIndex, ref firstModifiedVertex, ref lastModifiedVertex);
                }
            }
        }

        static void ApplyVertex(
            MeshObject geometry,
            int vertexIndex,
            Matrix transform,
            VertexTransformOperation operation,
            bool inverse)
        {
            TryCreateReplayMatrix(transform, operation.PivotPoint, inverse, out var replayMatrix);
            if (operation.Mode == VertexTransformOperationMode.Translate)
            {
                geometry.TransformVertexTranslation(vertexIndex, replayMatrix);
                return;
            }

            var normalMatrix = Matrix.Transpose(Matrix.Invert(replayMatrix));
            if (operation.Mode == VertexTransformOperationMode.Rotate)
                geometry.TransformVertexRotation(vertexIndex, replayMatrix, normalMatrix);
            else
                TransformScaleVertexPreservingBasisMagnitude(
                    geometry,
                    vertexIndex,
                    replayMatrix,
                    normalMatrix);
        }

        static void TransformScaleVertexPreservingBasisMagnitude(
            MeshObject geometry,
            int vertexIndex,
            Matrix transform,
            Matrix normalMatrix)
        {
            var normalLength = geometry.VertexArray[vertexIndex].Normal.Length();
            var tangentLength = geometry.VertexArray[vertexIndex].Tangent.Length();
            var binormalLength = geometry.VertexArray[vertexIndex].BiNormal.Length();

            geometry.TransformVertex(vertexIndex, transform, normalMatrix);

            geometry.VertexArray[vertexIndex].Normal =
                RestoreMagnitude(geometry.VertexArray[vertexIndex].Normal, normalLength);
            geometry.VertexArray[vertexIndex].Tangent =
                RestoreMagnitude(geometry.VertexArray[vertexIndex].Tangent, tangentLength);
            geometry.VertexArray[vertexIndex].BiNormal =
                RestoreMagnitude(geometry.VertexArray[vertexIndex].BiNormal, binormalLength);
        }

        static Vector3 RestoreMagnitude(Vector3 direction, float magnitude)
        {
            return magnitude == 0 ? Vector3.Zero : direction * magnitude;
        }

        static Matrix CreateWeightedTransform(VertexTransformOperation operation, float weight)
        {
            TryCreateWeightedTransform(operation, weight, out var weightedTransform);
            return weightedTransform;
        }

        static bool TryCreateWeightedTransform(
            VertexTransformOperation operation,
            float weight,
            out Matrix weightedTransform)
        {
            switch (operation.Mode)
            {
                case VertexTransformOperationMode.Translate:
                    weightedTransform = Matrix.CreateTranslation(operation.Transform.Translation * weight);
                    return IsFinite(weightedTransform);
                case VertexTransformOperationMode.Rotate:
                    if (!operation.Transform.Decompose(out _, out var rotation, out _)
                        || !IsFinite(rotation))
                    {
                        weightedTransform = default;
                        return false;
                    }

                    weightedTransform = Matrix.CreateFromQuaternion(
                        Quaternion.Slerp(Quaternion.Identity, rotation, weight));
                    return IsFinite(weightedTransform);
                case VertexTransformOperationMode.Scale:
                    var scale = new Vector3(
                        operation.Transform.M11,
                        operation.Transform.M22,
                        operation.Transform.M33);
                    weightedTransform = Matrix.CreateScale(
                        Vector3.Lerp(Vector3.One, scale, weight));
                    return IsFinite(weightedTransform);
                default:
                    weightedTransform = default;
                    return false;
            }
        }

        static bool TryCreateReplayMatrix(
            Matrix transform,
            Vector3 pivotPoint,
            bool inverse,
            out Matrix replayMatrix)
        {
            var transformToApply = inverse ? Matrix.Invert(transform) : transform;
            replayMatrix =
                Matrix.CreateTranslation(-pivotPoint) *
                transformToApply *
                Matrix.CreateTranslation(pivotPoint);
            return IsInvertible(replayMatrix);
        }

        static bool IsInvertible(Matrix transform)
        {
            if (!IsFinite(transform))
                return false;

            var determinant = transform.Determinant();
            if (!float.IsFinite(determinant) ||
                determinant == 0)
            {
                return false;
            }

            return IsFinite(Matrix.Invert(transform));
        }

        static bool PassesScaleAxisSafetyGuards(Matrix transform)
        {
            var x = MathF.Abs(transform.M11);
            var y = MathF.Abs(transform.M22);
            var z = MathF.Abs(transform.M33);
            var minimum = MathF.Min(x, MathF.Min(y, z));
            var maximum = MathF.Max(x, MathF.Max(y, z));

            // Cheap early rejection before the coordinate-aware pass; this is not
            // sufficient to establish reversibility by itself.
            return minimum >= MinimumScaleAxisSafetyMagnitude &&
                   maximum / minimum <= MaximumScaleAxisSafetyConditionNumber;
        }

        static void IncludeVertex(
            int vertexIndex,
            ref int firstModifiedVertex,
            ref int lastModifiedVertex)
        {
            firstModifiedVertex = Math.Min(firstModifiedVertex, vertexIndex);
            lastModifiedVertex = Math.Max(lastModifiedVertex, vertexIndex);
        }

        static bool IsFinite(Matrix value)
        {
            return
                float.IsFinite(value.M11) &&
                float.IsFinite(value.M12) &&
                float.IsFinite(value.M13) &&
                float.IsFinite(value.M14) &&
                float.IsFinite(value.M21) &&
                float.IsFinite(value.M22) &&
                float.IsFinite(value.M23) &&
                float.IsFinite(value.M24) &&
                float.IsFinite(value.M31) &&
                float.IsFinite(value.M32) &&
                float.IsFinite(value.M33) &&
                float.IsFinite(value.M34) &&
                float.IsFinite(value.M41) &&
                float.IsFinite(value.M42) &&
                float.IsFinite(value.M43) &&
                float.IsFinite(value.M44);
        }

        static bool IsFinite(Vector3 value)
        {
            return
                float.IsFinite(value.X) &&
                float.IsFinite(value.Y) &&
                float.IsFinite(value.Z);
        }

        static bool IsFinite(Vector4 value)
        {
            return
                float.IsFinite(value.X) &&
                float.IsFinite(value.Y) &&
                float.IsFinite(value.Z) &&
                float.IsFinite(value.W);
        }

        static bool IsFinite(Quaternion value)
        {
            return
                float.IsFinite(value.X) &&
                float.IsFinite(value.Y) &&
                float.IsFinite(value.Z) &&
                float.IsFinite(value.W);
        }
    }
}
