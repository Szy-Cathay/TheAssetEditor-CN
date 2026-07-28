using GameWorld.Core.Animation;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    internal readonly record struct VertexTransformMatrixPair(
        Matrix Forward,
        Matrix Reverse);

    internal readonly record struct VertexPoseMapping(
        Matrix VertexToWorld,
        Matrix WorldToVertex,
        bool IsValid);

    internal sealed class VertexTransformReplayPlan
    {
        public VertexTransformMatrixPair RawMatrices { get; }
        public IReadOnlyDictionary<float, VertexTransformMatrixPair> WeightedMatrices { get; }
        public bool ContainsScale { get; }
        public bool TransformsBasis { get; }
        public int OperationCount { get; }
        public IReadOnlyDictionary<MeshObject, MeshPoseSnapshot> PoseSnapshots { get; }
        public IReadOnlyDictionary<MeshObject, VertexPoseMapping[]> PoseMappings { get; }
        public bool HasInvalidPoseMappings { get; }

        public VertexTransformReplayPlan(
            VertexTransformMatrixPair rawMatrices,
            IReadOnlyDictionary<float, VertexTransformMatrixPair> weightedMatrices,
            bool containsScale,
            bool transformsBasis,
            int operationCount,
            IReadOnlyDictionary<MeshObject, MeshPoseSnapshot>? poseSnapshots = null,
            IReadOnlyDictionary<MeshObject, VertexPoseMapping[]>? poseMappings = null,
            bool hasInvalidPoseMappings = false)
        {
            RawMatrices = rawMatrices;
            WeightedMatrices = weightedMatrices;
            ContainsScale = containsScale;
            TransformsBasis = transformsBasis;
            OperationCount = operationCount;
            PoseSnapshots = poseSnapshots ??
                new Dictionary<MeshObject, MeshPoseSnapshot>();
            PoseMappings = poseMappings ??
                new Dictionary<MeshObject, VertexPoseMapping[]>();
            HasInvalidPoseMappings = hasInvalidPoseMappings;
        }
    }

    internal static class VertexTransformOperationApplier
    {
        internal const float MinimumScaleAxisSafetyMagnitude = 0.001f;
        internal const float MaximumScaleAxisSafetyConditionNumber = 1000.0f;
        internal const float MaximumScalePositionRoundTripError = 0.00001f;
        internal const float MaximumPositionRoundTripError = 0.0001f;
        const int ParallelPoseMappingVertexThreshold = 4096;

        public static VertexTransformReplayPlan CreateEmptyReplayPlan(
            ISelectionState selectionState,
            IReadOnlyDictionary<int, float>? falloffWeights,
            IReadOnlyDictionary<MeshObject, MeshPoseSnapshot>? poseSnapshots = null)
        {
            var identity = new VertexTransformMatrixPair(
                Matrix.Identity,
                Matrix.Identity);
            var weightedMatrices = new Dictionary<float, VertexTransformMatrixPair>();
            if (selectionState is VertexSelectionState vertexSelectionState)
            {
                foreach (var weight in vertexSelectionState.VertexWeights)
                {
                    if (weight != 0)
                        weightedMatrices.TryAdd(weight, identity);
                }
            }
            else if (falloffWeights != null && falloffWeights.Count > 0)
            {
                foreach (var weight in falloffWeights.Values)
                {
                    if (weight != 0)
                        weightedMatrices.TryAdd(weight, identity);
                }
            }

            var poseMappings = CreatePoseMappings(
                poseSnapshots,
                out var hasInvalidPoseMappings);
            return new VertexTransformReplayPlan(
                identity,
                weightedMatrices,
                containsScale: false,
                transformsBasis: false,
                operationCount: 0,
                poseSnapshots,
                poseMappings,
                hasInvalidPoseMappings);
        }

        public static bool TryAppendOperation(
            IReadOnlyList<MeshObject> geometryList,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformReplayPlan currentPlan,
            VertexTransformOperation operation,
            out VertexTransformReplayPlan candidatePlan)
        {
            candidatePlan = currentPlan;
            if (currentPlan.HasInvalidPoseMappings ||
                !IsStructurallyValid(
                    geometryList,
                    selectionState,
                    affectedVertexIndices,
                    falloffWeights,
                    operation) ||
                !HasRequiredWeightMatrices(selectionState, falloffWeights, currentPlan) ||
                !TryAppendMatrix(
                    currentPlan.RawMatrices,
                    operation.Transform,
                    operation.PivotPoint,
                    out var rawMatrices,
                    out var rawStepMatrices))
            {
                return false;
            }

            var weightedMatrices =
                new Dictionary<float, VertexTransformMatrixPair>(currentPlan.WeightedMatrices.Count);
            var weightedStepMatrices =
                new Dictionary<float, VertexTransformMatrixPair>(currentPlan.WeightedMatrices.Count);
            foreach (var (weight, currentMatrices) in currentPlan.WeightedMatrices)
            {
                if (!TryCreateWeightedTransform(operation, weight, out var weightedTransform) ||
                    !TryAppendMatrix(
                        currentMatrices,
                        weightedTransform,
                        operation.PivotPoint,
                        out var candidateMatrices,
                        out var stepMatrices))
                {
                    return false;
                }

                weightedMatrices.Add(weight, candidateMatrices);
                weightedStepMatrices.Add(weight, stepMatrices);
            }

            var nextPlan = new VertexTransformReplayPlan(
                rawMatrices,
                weightedMatrices,
                currentPlan.ContainsScale || operation.Mode == VertexTransformOperationMode.Scale,
                currentPlan.TransformsBasis || operation.Mode != VertexTransformOperationMode.Translate,
                currentPlan.OperationCount + 1,
                currentPlan.PoseSnapshots,
                currentPlan.PoseMappings,
                currentPlan.HasInvalidPoseMappings);
            // Keep the stricter single-step scale guard in addition to baseline aggregate validation.
            if (operation.Mode == VertexTransformOperationMode.Scale &&
                !AreAffectedCurrentPositionsReversible(
                    geometryList,
                    selectionState,
                    affectedVertexIndices,
                    falloffWeights,
                    rawStepMatrices,
                    weightedStepMatrices,
                    currentPlan.PoseSnapshots,
                    currentPlan.PoseMappings,
                    currentPlan.HasInvalidPoseMappings))
            {
                return false;
            }

            candidatePlan = nextPlan;
            return true;
        }

        public static bool IsReplayPlanReversibleFromBaseline(
            IReadOnlyList<MeshObject> geometryList,
            IReadOnlyList<VertexPositionNormalTextureCustom[]> baselineVertexArrays,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformReplayPlan replayPlan)
        {
            if (!IsReplayTargetValid(
                    geometryList,
                    selectionState,
                    affectedVertexIndices,
                    falloffWeights,
                    replayPlan) ||
                baselineVertexArrays.Count != geometryList.Count)
            {
                return false;
            }

            for (var meshIndex = 0; meshIndex < geometryList.Count; meshIndex++)
            {
                var geometry = geometryList[meshIndex];
                var baseline = baselineVertexArrays[meshIndex];
                if (baseline.Length != geometry.VertexCount())
                    return false;

                if (selectionState.Mode == GeometrySelectionMode.Vertex)
                {
                    var vertexSelectionState = (VertexSelectionState)selectionState;
                    for (var vertexIndex = 0; vertexIndex < vertexSelectionState.VertexWeights.Count; vertexIndex++)
                    {
                        var weight = vertexSelectionState.VertexWeights[vertexIndex];
                        if (weight == 0)
                            continue;

                        if (!replayPlan.WeightedMatrices.TryGetValue(weight, out var poseMatrices) ||
                            !TryGetVertexMatrices(
                                replayPlan,
                                geometry,
                                vertexIndex,
                                poseMatrices,
                                out var matrices) ||
                            !IsPositionReversible(
                                baseline[vertexIndex].Position,
                                matrices,
                                MaximumPositionRoundTripError))
                        {
                            return false;
                        }
                    }
                }
                else if (affectedVertexIndices != null &&
                         falloffWeights != null &&
                         falloffWeights.Count > 0)
                {
                    foreach (var (vertexIndex, weight) in falloffWeights)
                    {
                        if (weight == 0)
                            continue;

                        if (vertexIndex < 0 ||
                            vertexIndex >= baseline.Length ||
                            !replayPlan.WeightedMatrices.TryGetValue(weight, out var poseMatrices) ||
                            !TryGetVertexMatrices(
                                replayPlan,
                                geometry,
                                vertexIndex,
                                poseMatrices,
                                out var matrices) ||
                            !IsPositionReversible(
                                baseline[vertexIndex].Position,
                                matrices,
                                MaximumPositionRoundTripError))
                        {
                            return false;
                        }
                    }
                }
                else if (affectedVertexIndices != null)
                {
                    foreach (var vertexIndex in affectedVertexIndices)
                    {
                        if (vertexIndex < 0 ||
                            vertexIndex >= baseline.Length ||
                            !TryGetVertexMatrices(
                                replayPlan,
                                geometry,
                                vertexIndex,
                                replayPlan.RawMatrices,
                                out var matrices) ||
                            !IsPositionReversible(
                                baseline[vertexIndex].Position,
                                matrices,
                                MaximumPositionRoundTripError))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    for (var vertexIndex = 0; vertexIndex < baseline.Length; vertexIndex++)
                    {
                        if (!TryGetVertexMatrices(
                                replayPlan,
                                geometry,
                                vertexIndex,
                                replayPlan.RawMatrices,
                                out var matrices) ||
                            !IsPositionReversible(
                            baseline[vertexIndex].Position,
                            matrices,
                            MaximumPositionRoundTripError))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        public static bool TryApplyReplayPlan(
            IReadOnlyList<MeshObject> geometryList,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformReplayPlan replayPlan,
            bool inverse,
            out IReadOnlyList<VertexTransformMeshResult> results)
        {
            if (!IsReplayTargetValid(
                    geometryList,
                    selectionState,
                    affectedVertexIndices,
                    falloffWeights,
                    replayPlan))
            {
                results = Array.Empty<VertexTransformMeshResult>();
                return false;
            }

            results = ApplyReplayPlan(
                geometryList,
                selectionState,
                affectedVertexIndices,
                falloffWeights,
                replayPlan,
                inverse);
            return true;
        }

        public static IReadOnlyList<VertexTransformMeshResult> ApplyReplayPlan(
            IReadOnlyList<MeshObject> geometryList,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformReplayPlan replayPlan,
            bool inverse)
        {
            return ApplyReplayPlanCore(
                geometryList,
                selectionState,
                affectedVertexIndices,
                falloffWeights,
                replayPlan,
                inverse,
                useFastPosePath: false);
        }

        internal static IReadOnlyList<VertexTransformMeshResult>
            ApplyReplayPlanPreview(
                IReadOnlyList<MeshObject> geometryList,
                ISelectionState selectionState,
                HashSet<int>? affectedVertexIndices,
                IReadOnlyDictionary<int, float>? falloffWeights,
                VertexTransformReplayPlan replayPlan)
        {
            return ApplyReplayPlanCore(
                geometryList,
                selectionState,
                affectedVertexIndices,
                falloffWeights,
                replayPlan,
                inverse: false,
                useFastPosePath: true);
        }

        static IReadOnlyList<VertexTransformMeshResult>
            ApplyReplayPlanCore(
                IReadOnlyList<MeshObject> geometryList,
                ISelectionState selectionState,
                HashSet<int>? affectedVertexIndices,
                IReadOnlyDictionary<int, float>? falloffWeights,
                VertexTransformReplayPlan replayPlan,
                bool inverse,
                bool useFastPosePath)
        {
            if (replayPlan.OperationCount == 0)
                return Array.Empty<VertexTransformMeshResult>();

            var results = new List<VertexTransformMeshResult>(geometryList.Count);
            foreach (var geometry in geometryList)
            {
                var firstModifiedVertex = int.MaxValue;
                var lastModifiedVertex = -1;
                ApplyToMesh(
                    geometry,
                    selectionState,
                    affectedVertexIndices,
                    falloffWeights,
                    replayPlan,
                    inverse,
                    useFastPosePath,
                    ref firstModifiedVertex,
                    ref lastModifiedVertex);
                results.Add(new VertexTransformMeshResult(
                    geometry,
                    firstModifiedVertex,
                    lastModifiedVertex));
            }

            return results;
        }

        public static void RestoreAffectedVerticesFromBaseline(
            IReadOnlyList<MeshObject> geometryList,
            IReadOnlyList<VertexPositionNormalTextureCustom[]> baselineVertexArrays,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights)
        {
            for (var meshIndex = 0; meshIndex < geometryList.Count; meshIndex++)
            {
                var geometry = geometryList[meshIndex];
                var baseline = baselineVertexArrays[meshIndex];
                if (selectionState.Mode == GeometrySelectionMode.Vertex)
                {
                    var vertexSelectionState = (VertexSelectionState)selectionState;
                    for (var vertexIndex = 0; vertexIndex < vertexSelectionState.VertexWeights.Count; vertexIndex++)
                    {
                        if (vertexSelectionState.VertexWeights[vertexIndex] != 0)
                            geometry.VertexArray[vertexIndex] = baseline[vertexIndex];
                    }
                }
                else if (affectedVertexIndices != null &&
                         falloffWeights != null &&
                         falloffWeights.Count > 0)
                {
                    foreach (var (vertexIndex, weight) in falloffWeights)
                    {
                        if (weight != 0)
                            geometry.VertexArray[vertexIndex] = baseline[vertexIndex];
                    }
                }
                else if (affectedVertexIndices != null)
                {
                    foreach (var vertexIndex in affectedVertexIndices)
                        geometry.VertexArray[vertexIndex] = baseline[vertexIndex];
                }
                else
                {
                    Array.Copy(baseline, geometry.VertexArray, baseline.Length);
                }
            }
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
                !TryCreateReplayMatrix(operation.Transform, operation.PivotPoint, out _))
            {
                return false;
            }

            if (selectionState.Mode == GeometrySelectionMode.Vertex)
            {
                if (selectionState is not VertexSelectionState vertexSelectionState)
                    return false;

                foreach (var geometry in geometryList)
                {
                    if (vertexSelectionState.VertexWeights.Count > geometry.VertexCount())
                        return false;
                }

                if (!AreWeightsValid(vertexSelectionState.VertexWeights, operation))
                    return false;
            }
            else if (affectedVertexIndices != null &&
                     falloffWeights != null &&
                     falloffWeights.Count > 0)
            {
                foreach (var geometry in geometryList)
                {
                    foreach (var vertexIndex in falloffWeights.Keys)
                    {
                        if (vertexIndex < 0 ||
                            vertexIndex >= geometry.VertexCount())
                        {
                            return false;
                        }
                    }
                }

                if (!AreWeightsValid(falloffWeights.Values, operation))
                    return false;
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

        static bool HasRequiredWeightMatrices(
            ISelectionState selectionState,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformReplayPlan replayPlan)
        {
            if (selectionState is VertexSelectionState vertexSelectionState)
            {
                foreach (var weight in vertexSelectionState.VertexWeights)
                {
                    if (weight != 0 && !replayPlan.WeightedMatrices.ContainsKey(weight))
                        return false;
                }
            }
            else if (falloffWeights != null && falloffWeights.Count > 0)
            {
                foreach (var weight in falloffWeights.Values)
                {
                    if (weight != 0 && !replayPlan.WeightedMatrices.ContainsKey(weight))
                        return false;
                }
            }

            return true;
        }

        static bool IsReplayPlanValid(
            ISelectionState selectionState,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformReplayPlan replayPlan)
        {
            if (!IsMatrixPairValid(replayPlan.RawMatrices) ||
                !HasRequiredWeightMatrices(selectionState, falloffWeights, replayPlan))
            {
                return false;
            }

            foreach (var matrices in replayPlan.WeightedMatrices.Values)
            {
                if (!IsMatrixPairValid(matrices))
                    return false;
            }

            return true;
        }

        static bool IsReplayTargetValid(
            IReadOnlyList<MeshObject> geometryList,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformReplayPlan replayPlan)
        {
            if (!IsReplayPlanValid(selectionState, falloffWeights, replayPlan))
                return false;

            if (selectionState.Mode == GeometrySelectionMode.Vertex)
            {
                if (selectionState is not VertexSelectionState vertexSelectionState)
                    return false;

                foreach (var geometry in geometryList)
                {
                    if (vertexSelectionState.VertexWeights.Count > geometry.VertexCount())
                        return false;
                }
            }
            else if (affectedVertexIndices != null &&
                     falloffWeights != null &&
                     falloffWeights.Count > 0)
            {
                foreach (var geometry in geometryList)
                {
                    foreach (var vertexIndex in falloffWeights.Keys)
                    {
                        if (vertexIndex < 0 || vertexIndex >= geometry.VertexCount())
                            return false;
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

        static bool AreAffectedCurrentPositionsReversible(
            IReadOnlyList<MeshObject> geometryList,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformMatrixPair rawStepMatrices,
            IReadOnlyDictionary<float, VertexTransformMatrixPair> weightedStepMatrices,
            IReadOnlyDictionary<MeshObject, MeshPoseSnapshot> poseSnapshots,
            IReadOnlyDictionary<MeshObject, VertexPoseMapping[]> poseMappings,
            bool hasInvalidPoseMappings)
        {
            var stepPlan = new VertexTransformReplayPlan(
                rawStepMatrices,
                weightedStepMatrices,
                containsScale: false,
                transformsBasis: false,
                operationCount: 1,
                poseSnapshots,
                poseMappings,
                hasInvalidPoseMappings);
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

                        if (!weightedStepMatrices.TryGetValue(weight, out var poseMatrices) ||
                            !TryGetVertexMatrices(
                                stepPlan,
                                geometry,
                                vertexIndex,
                                poseMatrices,
                                out var matrices) ||
                            !IsPositionReversible(
                                geometry.VertexArray[vertexIndex].Position,
                                matrices,
                                MaximumScalePositionRoundTripError))
                        {
                            return false;
                        }
                    }
                }
                else if (affectedVertexIndices != null &&
                         falloffWeights != null &&
                         falloffWeights.Count > 0)
                {
                    foreach (var (vertexIndex, weight) in falloffWeights)
                    {
                        if (weight == 0)
                            continue;

                        if (!weightedStepMatrices.TryGetValue(weight, out var poseMatrices) ||
                            !TryGetVertexMatrices(
                                stepPlan,
                                geometry,
                                vertexIndex,
                                poseMatrices,
                                out var matrices) ||
                            !IsPositionReversible(
                                geometry.VertexArray[vertexIndex].Position,
                                matrices,
                                MaximumScalePositionRoundTripError))
                        {
                            return false;
                        }
                    }
                }
                else if (affectedVertexIndices != null)
                {
                    foreach (var vertexIndex in affectedVertexIndices)
                    {
                        if (!TryGetVertexMatrices(
                                stepPlan,
                                geometry,
                                vertexIndex,
                                rawStepMatrices,
                                out var matrices) ||
                            !IsPositionReversible(
                            geometry.VertexArray[vertexIndex].Position,
                            matrices,
                            MaximumScalePositionRoundTripError))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    for (var vertexIndex = 0;
                         vertexIndex < geometry.VertexArray.Length;
                         vertexIndex++)
                    {
                        if (!TryGetVertexMatrices(
                                stepPlan,
                                geometry,
                                vertexIndex,
                                rawStepMatrices,
                                out var matrices) ||
                            !IsPositionReversible(
                            geometry.VertexArray[vertexIndex].Position,
                            matrices,
                            MaximumScalePositionRoundTripError))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        static bool IsPositionReversible(
            Vector4 position,
            VertexTransformMatrixPair matrices,
            float maximumRoundTripError)
        {
            if (!TryTransformPosition(position, matrices.Forward, out var transformed) ||
                !TryTransformPosition(
                    transformed,
                    matrices.Reverse,
                    out var roundTrip))
            {
                return false;
            }

            return
                MathF.Abs(roundTrip.X - position.X) <= maximumRoundTripError &&
                MathF.Abs(roundTrip.Y - position.Y) <= maximumRoundTripError &&
                MathF.Abs(roundTrip.Z - position.Z) <= maximumRoundTripError;
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
            VertexTransformOperation operation)
        {
            if (!float.IsFinite(weight))
                return false;
            if (weight == 0)
                return true;

            return TryCreateWeightedTransform(operation, weight, out var weightedTransform) &&
                   (operation.Mode != VertexTransformOperationMode.Scale ||
                    PassesScaleAxisSafetyGuards(weightedTransform)) &&
                   IsInvertible(weightedTransform) &&
                   TryCreateReplayMatrix(
                       weightedTransform,
                       operation.PivotPoint,
                       out _);
        }

        static bool AreWeightsValid(
            IEnumerable<float> weights,
            VertexTransformOperation operation)
        {
            var validatedWeights = new HashSet<float>();
            foreach (var weight in weights)
            {
                if (validatedWeights.Add(weight) &&
                    !IsValidWeightedTransform(weight, operation))
                {
                    return false;
                }
            }

            return true;
        }

        static void ApplyToMesh(
            MeshObject geometry,
            ISelectionState selectionState,
            HashSet<int>? affectedVertexIndices,
            IReadOnlyDictionary<int, float>? falloffWeights,
            VertexTransformReplayPlan replayPlan,
            bool inverse,
            bool useFastPosePath,
            ref int firstModifiedVertex,
            ref int lastModifiedVertex)
        {
            replayPlan.PoseMappings.TryGetValue(
                geometry,
                out var poseMappings);
            Matrix? sharedRawPoseTransform = null;
            if (useFastPosePath &&
                poseMappings?.Length == 1 &&
                TryGetPoseMapping(
                    poseMappings,
                    0,
                    out var sharedPoseMapping))
            {
                var rawTransform = inverse
                    ? replayPlan.RawMatrices.Reverse
                    : replayPlan.RawMatrices.Forward;
                var mappedTransform =
                    sharedPoseMapping.VertexToWorld *
                    rawTransform *
                    sharedPoseMapping.WorldToVertex;
                if (IsFinite(mappedTransform))
                    sharedRawPoseTransform = mappedTransform;
            }

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
                        replayPlan.WeightedMatrices[weight],
                        replayPlan,
                        poseMappings,
                        inverse,
                        useFastPosePath,
                        precomputedPoseTransform: null);
                    IncludeVertex(vertexIndex, ref firstModifiedVertex, ref lastModifiedVertex);
                }
            }
            else if (affectedVertexIndices != null &&
                     falloffWeights != null &&
                     falloffWeights.Count > 0)
            {
                foreach (var (vertexIndex, weight) in falloffWeights)
                {
                    if (weight == 0)
                        continue;

                    ApplyVertex(
                        geometry,
                        vertexIndex,
                        replayPlan.WeightedMatrices[weight],
                        replayPlan,
                        poseMappings,
                        inverse,
                        useFastPosePath,
                        precomputedPoseTransform: null);
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
                        replayPlan.RawMatrices,
                        replayPlan,
                        poseMappings,
                        inverse,
                        useFastPosePath,
                        sharedRawPoseTransform);
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
                        replayPlan.RawMatrices,
                        replayPlan,
                        poseMappings,
                        inverse,
                        useFastPosePath,
                        sharedRawPoseTransform);
                    IncludeVertex(vertexIndex, ref firstModifiedVertex, ref lastModifiedVertex);
                }
            }
        }

        static void ApplyVertex(
            MeshObject geometry,
            int vertexIndex,
            VertexTransformMatrixPair matrices,
            VertexTransformReplayPlan replayPlan,
            VertexPoseMapping[]? poseMappings,
            bool inverse,
            bool useFastPosePath,
            Matrix? precomputedPoseTransform)
        {
            var replayMatrix = inverse
                ? matrices.Reverse
                : matrices.Forward;
            if (poseMappings != null)
            {
                if (!TryGetPoseMapping(
                        poseMappings,
                        vertexIndex,
                        out var poseMapping))
                {
                    throw new InvalidOperationException(
                        "The paused pose cannot be mapped back to bind geometry.");
                }

                var inversePoseTransform = inverse
                    ? matrices.Forward
                    : matrices.Reverse;
                if (precomputedPoseTransform.HasValue)
                {
                    TransformPoseMappedVertexExact(
                        geometry,
                        vertexIndex,
                        precomputedPoseTransform.Value,
                        inversePoseTransform,
                        poseMapping,
                        replayPlan.TransformsBasis);
                }
                else if (useFastPosePath)
                {
                    TransformPoseMappedVertexPreview(
                        geometry,
                        vertexIndex,
                        replayMatrix,
                        inversePoseTransform,
                        poseMapping,
                        replayPlan.TransformsBasis);
                }
                else
                {
                    var mappedTransform =
                        poseMapping.VertexToWorld *
                        replayMatrix *
                        poseMapping.WorldToVertex;
                    if (!IsFinite(mappedTransform))
                    {
                        throw new InvalidOperationException(
                            "The paused pose transform is invalid.");
                    }

                    TransformPoseMappedVertexExact(
                        geometry,
                        vertexIndex,
                        mappedTransform,
                        inversePoseTransform,
                        poseMapping,
                        replayPlan.TransformsBasis);
                }
                return;
            }

            if (!replayPlan.TransformsBasis)
            {
                geometry.TransformVertexTranslation(vertexIndex, replayMatrix);
                return;
            }

            var normalMatrix = Matrix.Transpose(
                inverse
                    ? matrices.Forward
                    : matrices.Reverse);
            if (replayPlan.ContainsScale)
            {
                TransformScaleVertexPreservingBasisMagnitude(
                    geometry,
                    vertexIndex,
                    replayMatrix,
                    normalMatrix);
            }
            else
            {
                geometry.TransformVertexRotation(vertexIndex, replayMatrix, normalMatrix);
            }
        }

        static void TransformPoseMappedVertexExact(
            MeshObject geometry,
            int vertexIndex,
            Matrix positionTransform,
            Matrix inversePoseTransform,
            VertexPoseMapping poseMapping,
            bool transformsBasis)
        {
            var vertex = geometry.VertexArray[vertexIndex];
            vertex.Position = Vector4.Transform(
                vertex.Position,
                positionTransform);
            NormalizePosition(ref vertex.Position);
            TransformPoseMappedBasis(
                ref vertex,
                inversePoseTransform,
                poseMapping,
                transformsBasis);
            geometry.VertexArray[vertexIndex] = vertex;
        }

        static void TransformPoseMappedVertexPreview(
            MeshObject geometry,
            int vertexIndex,
            Matrix positionTransform,
            Matrix inversePoseTransform,
            VertexPoseMapping poseMapping,
            bool transformsBasis)
        {
            var vertex = geometry.VertexArray[vertexIndex];
            vertex.Position = Vector4.Transform(
                vertex.Position,
                poseMapping.VertexToWorld);
            vertex.Position = Vector4.Transform(
                vertex.Position,
                positionTransform);
            vertex.Position = Vector4.Transform(
                vertex.Position,
                poseMapping.WorldToVertex);
            NormalizePosition(ref vertex.Position);
            TransformPoseMappedBasis(
                ref vertex,
                inversePoseTransform,
                poseMapping,
                transformsBasis);
            geometry.VertexArray[vertexIndex] = vertex;
        }

        static void TransformPoseMappedBasis(
            ref VertexPositionNormalTextureCustom vertex,
            Matrix inversePoseTransform,
            VertexPoseMapping poseMapping,
            bool transformsBasis)
        {
            if (!transformsBasis)
                return;

            var poseNormalTransform =
                Matrix.Transpose(inversePoseTransform);
            vertex.Normal = TransformBasisPreservingMagnitude(
                vertex.Normal,
                poseMapping.VertexToWorld,
                poseNormalTransform,
                poseMapping.WorldToVertex);
            vertex.Tangent = TransformBasisPreservingMagnitude(
                vertex.Tangent,
                poseMapping.VertexToWorld,
                poseNormalTransform,
                poseMapping.WorldToVertex);
            vertex.BiNormal = TransformBasisPreservingMagnitude(
                vertex.BiNormal,
                poseMapping.VertexToWorld,
                poseNormalTransform,
                poseMapping.WorldToVertex);
        }

        static void NormalizePosition(ref Vector4 position)
        {
            position.X /= position.W;
            position.Y /= position.W;
            position.Z /= position.W;
            position.W = 1;
        }

        static Vector3 TransformBasisPreservingMagnitude(
            Vector3 basis,
            Matrix linearVertexToWorld,
            Matrix poseNormalTransform,
            Matrix linearWorldToVertex)
        {
            var magnitude = basis.Length();
            if (magnitude == 0)
                return Vector3.Zero;

            var transformed = Vector3.TransformNormal(
                basis,
                linearVertexToWorld);
            transformed = Vector3.TransformNormal(
                transformed,
                poseNormalTransform);
            transformed = Vector3.TransformNormal(
                transformed,
                linearWorldToVertex);
            if (transformed == Vector3.Zero)
                return Vector3.Zero;
            transformed.Normalize();
            return transformed * magnitude;
        }

        static bool TryGetVertexMatrices(
            VertexTransformReplayPlan replayPlan,
            MeshObject geometry,
            int vertexIndex,
            VertexTransformMatrixPair poseMatrices,
            out VertexTransformMatrixPair vertexMatrices)
        {
            if (!TryGetVertexMatrix(
                    replayPlan,
                    geometry,
                    vertexIndex,
                    poseMatrices.Forward,
                    out var forward) ||
                !TryGetVertexMatrix(
                    replayPlan,
                    geometry,
                    vertexIndex,
                    poseMatrices.Reverse,
                    out var reverse))
            {
                vertexMatrices = default;
                return false;
            }

            vertexMatrices = new VertexTransformMatrixPair(
                forward,
                reverse);
            return true;
        }

        static bool TryGetVertexMatrix(
            VertexTransformReplayPlan replayPlan,
            MeshObject geometry,
            int vertexIndex,
            Matrix poseMatrix,
            out Matrix vertexMatrix)
        {
            if (!replayPlan.PoseSnapshots.ContainsKey(
                    geometry))
            {
                vertexMatrix = poseMatrix;
                return true;
            }

            if (!replayPlan.PoseMappings.TryGetValue(
                    geometry,
                    out var poseMappings))
            {
                vertexMatrix = default;
                return false;
            }

            if (!TryGetPoseMapping(
                    poseMappings,
                    vertexIndex,
                    out var poseMapping))
            {
                vertexMatrix = default;
                return false;
            }

            vertexMatrix =
                poseMapping.VertexToWorld *
                poseMatrix *
                poseMapping.WorldToVertex;
            return IsFinite(vertexMatrix);
        }

        static bool TryGetPoseMapping(
            VertexPoseMapping[] poseMappings,
            int vertexIndex,
            out VertexPoseMapping poseMapping)
        {
            if (poseMappings.Length == 1)
            {
                poseMapping = poseMappings[0];
                return poseMapping.IsValid;
            }

            if (vertexIndex < 0 ||
                vertexIndex >= poseMappings.Length)
            {
                poseMapping = default;
                return false;
            }

            poseMapping = poseMappings[vertexIndex];
            return poseMapping.IsValid;
        }

        static IReadOnlyDictionary<MeshObject, VertexPoseMapping[]>
            CreatePoseMappings(
                IReadOnlyDictionary<MeshObject, MeshPoseSnapshot>?
                    poseSnapshots,
                out bool hasInvalidPoseMappings)
        {
            var result =
                new Dictionary<MeshObject, VertexPoseMapping[]>();
            hasInvalidPoseMappings = false;
            if (poseSnapshots == null)
                return result;

            foreach (var (geometry, poseSnapshot) in poseSnapshots)
            {
                if (!ReferenceEquals(
                        poseSnapshot.Geometry,
                        geometry))
                {
                    hasInvalidPoseMappings = true;
                    result.Add(
                        geometry,
                        Array.Empty<VertexPoseMapping>());
                    continue;
                }

                if (!poseSnapshot.ApplyAnimation)
                {
                    if (!TryCreatePoseMapping(
                            poseSnapshot.WorldTransform,
                            out var rigidMapping))
                    {
                        hasInvalidPoseMappings = true;
                    }

                    result.Add(
                        geometry,
                        [rigidMapping]);
                    continue;
                }

                var mappings =
                    new VertexPoseMapping[
                        geometry.VertexCount()];
                var invalidMappingFound = 0;
                void CreateMapping(int vertexIndex)
                {
                    if (!TryCreatePoseMapping(
                            poseSnapshot
                                .GetVertexToWorldTransform(
                                    vertexIndex),
                            out mappings[vertexIndex]))
                    {
                        Interlocked.Exchange(
                            ref invalidMappingFound,
                            1);
                    }
                }

                if (mappings.Length >=
                    ParallelPoseMappingVertexThreshold)
                {
                    Parallel.For(
                        0,
                        mappings.Length,
                        CreateMapping);
                }
                else
                {
                    for (var vertexIndex = 0;
                         vertexIndex < mappings.Length;
                         vertexIndex++)
                    {
                        CreateMapping(vertexIndex);
                    }
                }

                if (invalidMappingFound != 0)
                    hasInvalidPoseMappings = true;

                result.Add(geometry, mappings);
            }

            return result;
        }

        static bool TryCreatePoseMapping(
            Matrix vertexToWorld,
            out VertexPoseMapping mapping)
        {
            if (!TryInvert(
                    vertexToWorld,
                    out var worldToVertex))
            {
                mapping = default;
                return false;
            }

            mapping = new VertexPoseMapping(
                vertexToWorld,
                worldToVertex,
                true);
            return true;
        }

        static bool TryInvert(
            Matrix transform,
            out Matrix inverse)
        {
            inverse = default;
            if (!IsFinite(transform))
                return false;

            var determinant = transform.Determinant();
            if (!float.IsFinite(determinant) ||
                determinant == 0)
            {
                return false;
            }

            inverse = Matrix.Invert(transform);
            return IsFinite(inverse);
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

        static bool TryAppendMatrix(
            VertexTransformMatrixPair currentMatrices,
            Matrix transform,
            Vector3 pivotPoint,
            out VertexTransformMatrixPair candidateMatrices,
            out VertexTransformMatrixPair stepMatrices)
        {
            candidateMatrices = default;
            stepMatrices = default;
            if (!TryCreateReplayMatrix(transform, pivotPoint, out var replayMatrix) ||
                !TryCreateReplayMatrix(
                    Matrix.Invert(transform),
                    pivotPoint,
                    out var conservativeReverseStep))
                return false;

            var forward = currentMatrices.Forward * replayMatrix;
            if (!IsInvertible(forward))
                return false;

            var reverse = Matrix.Invert(forward);
            if (!IsFinite(reverse))
                return false;

            candidateMatrices = new VertexTransformMatrixPair(
                forward,
                reverse);
            stepMatrices = new VertexTransformMatrixPair(
                replayMatrix,
                conservativeReverseStep);
            return true;
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
                    if (!operation.Transform.Decompose(out _, out var rotation, out _) ||
                        !IsFinite(rotation))
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
            out Matrix replayMatrix)
        {
            replayMatrix =
                Matrix.CreateTranslation(-pivotPoint) *
                transform *
                Matrix.CreateTranslation(pivotPoint);
            return IsInvertible(replayMatrix);
        }

        static bool IsInvertible(Matrix transform)
        {
            if (!IsFinite(transform))
                return false;

            var determinant = transform.Determinant();
            if (!float.IsFinite(determinant) || determinant == 0)
                return false;

            return IsFinite(Matrix.Invert(transform));
        }

        static bool IsMatrixPairValid(VertexTransformMatrixPair matrices)
        {
            return
                IsInvertible(matrices.Forward) &&
                IsInvertible(matrices.Reverse);
        }

        static bool PassesScaleAxisSafetyGuards(Matrix transform)
        {
            var x = MathF.Abs(transform.M11);
            var y = MathF.Abs(transform.M22);
            var z = MathF.Abs(transform.M33);
            var minimum = MathF.Min(x, MathF.Min(y, z));
            var maximum = MathF.Max(x, MathF.Max(y, z));

            // Cheap early rejection; cumulative coordinate validation is authoritative.
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
