using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel;

namespace Editors.KitbasherEditor.ChildEditors.PinTool
{
    public static class RegiggingHelper
    {
        public static SkinWrapWeightTransfer CreateWeightTransfer(MeshObject mesh, Matrix modelMatrix)
        {
            return new SkinWrapWeightTransfer(mesh, modelMatrix);
        }

        public static (Vector3 Position, Vector4 Bones, Vector4 BlendWeights) FindClosestUV(
            Vector3 worldPosition,
            MeshObject mesh,
            Vector3 position)
        {
            return CreateWeightTransfer(mesh, Matrix.CreateTranslation(position))
                .FindClosestWeights(worldPosition);
        }
    }

    public sealed class SkinWrapWeightTransfer
    {
        const int LeafTriangleCount = 8;
        const float MinimumTriangleAreaSquared = 1e-12f;
        const float MinimumWeight = 1e-8f;
        const float WeightSortTieTolerance = 1e-5f;

        readonly Triangle[] _triangles;
        readonly int[] _triangleIndices;
        readonly BvhNode _root;
        readonly int _weightSlotCount;

        public SkinWrapWeightTransfer(MeshObject mesh, Matrix modelMatrix)
        {
            _weightSlotCount = mesh.VertexFormat switch
            {
                UiVertexFormat.Weighted => 2,
                UiVertexFormat.Cinematic => 4,
                _ => throw new InvalidOperationException("源网格必须包含骨骼权重。")
            };

            var triangles = new List<Triangle>(mesh.IndexArray.Length / 3);
            for (var index = 0; index + 2 < mesh.IndexArray.Length; index += 3)
            {
                var vertex0 = mesh.VertexArray[mesh.IndexArray[index]];
                var vertex1 = mesh.VertexArray[mesh.IndexArray[index + 1]];
                var vertex2 = mesh.VertexArray[mesh.IndexArray[index + 2]];
                var point0 = Vector3.Transform(vertex0.Position3(), modelMatrix);
                var point1 = Vector3.Transform(vertex1.Position3(), modelMatrix);
                var point2 = Vector3.Transform(vertex2.Position3(), modelMatrix);

                if (Vector3.Cross(point1 - point0, point2 - point0).LengthSquared() <= MinimumTriangleAreaSquared)
                    continue;

                triangles.Add(new Triangle(point0, point1, point2, vertex0, vertex1, vertex2));
            }

            if (triangles.Count == 0)
                throw new InvalidOperationException("源网格不包含可用于蒙皮包裹的有效三角形。");

            _triangles = triangles.ToArray();
            _triangleIndices = Enumerable.Range(0, _triangles.Length).ToArray();
            _root = BuildNode(0, _triangleIndices.Length);
        }

        public (Vector3 Position, Vector4 Bones, Vector4 BlendWeights) FindClosestWeights(Vector3 worldPosition)
        {
            var closestTriangleIndex = -1;
            var closestPosition = Vector3.Zero;
            var minimumDistanceSquared = float.MaxValue;
            FindClosest(
                _root,
                worldPosition,
                ref closestTriangleIndex,
                ref closestPosition,
                ref minimumDistanceSquared);

            if (closestTriangleIndex < 0)
                throw new InvalidOperationException("无法在源网格上找到有效的蒙皮包裹位置。");

            var triangle = _triangles[closestTriangleIndex];
            var barycentric = ComputeBarycentricCoordinates(
                closestPosition,
                triangle.Point0,
                triangle.Point1,
                triangle.Point2);
            var (bones, weights) = InterpolateWeights(triangle, barycentric);
            return (closestPosition, bones, weights);
        }

        BvhNode BuildNode(int start, int count)
        {
            var bounds = GetTriangleBounds(_triangles[_triangleIndices[start]]);
            var centroidMinimum = _triangles[_triangleIndices[start]].Centroid;
            var centroidMaximum = centroidMinimum;

            for (var offset = 1; offset < count; offset++)
            {
                var triangle = _triangles[_triangleIndices[start + offset]];
                bounds = Merge(bounds, GetTriangleBounds(triangle));
                centroidMinimum = Vector3.Min(centroidMinimum, triangle.Centroid);
                centroidMaximum = Vector3.Max(centroidMaximum, triangle.Centroid);
            }

            if (count <= LeafTriangleCount)
                return new BvhNode(bounds, start, count);

            var centroidSize = centroidMaximum - centroidMinimum;
            var axis = centroidSize.X >= centroidSize.Y && centroidSize.X >= centroidSize.Z
                ? 0
                : centroidSize.Y >= centroidSize.Z ? 1 : 2;
            Array.Sort(
                _triangleIndices,
                start,
                count,
                Comparer<int>.Create((left, right) =>
                    GetComponent(_triangles[left].Centroid, axis)
                        .CompareTo(GetComponent(_triangles[right].Centroid, axis))));

            var leftCount = count / 2;
            return new BvhNode(
                bounds,
                BuildNode(start, leftCount),
                BuildNode(start + leftCount, count - leftCount));
        }

        void FindClosest(
            BvhNode node,
            Vector3 position,
            ref int closestTriangleIndex,
            ref Vector3 closestPosition,
            ref float minimumDistanceSquared)
        {
            if (DistanceSquared(position, node.Bounds) > minimumDistanceSquared)
                return;

            if (node.IsLeaf)
            {
                for (var offset = 0; offset < node.Count; offset++)
                {
                    var triangleIndex = _triangleIndices[node.Start + offset];
                    var triangle = _triangles[triangleIndex];
                    var candidate = ClosestPointOnTriangle(
                        position,
                        triangle.Point0,
                        triangle.Point1,
                        triangle.Point2);
                    var distanceSquared = Vector3.DistanceSquared(candidate, position);
                    if (distanceSquared >= minimumDistanceSquared)
                        continue;

                    minimumDistanceSquared = distanceSquared;
                    closestTriangleIndex = triangleIndex;
                    closestPosition = candidate;
                }
                return;
            }

            var leftDistance = DistanceSquared(position, node.Left!.Bounds);
            var rightDistance = DistanceSquared(position, node.Right!.Bounds);
            if (leftDistance <= rightDistance)
            {
                FindClosest(node.Left, position, ref closestTriangleIndex, ref closestPosition, ref minimumDistanceSquared);
                FindClosest(node.Right, position, ref closestTriangleIndex, ref closestPosition, ref minimumDistanceSquared);
            }
            else
            {
                FindClosest(node.Right, position, ref closestTriangleIndex, ref closestPosition, ref minimumDistanceSquared);
                FindClosest(node.Left, position, ref closestTriangleIndex, ref closestPosition, ref minimumDistanceSquared);
            }
        }

        (Vector4 Bones, Vector4 Weights) InterpolateWeights(Triangle triangle, Vector3 barycentric)
        {
            Span<int> boneIndices = stackalloc int[12];
            Span<float> boneWeights = stackalloc float[12];
            var boneCount = 0;

            AccumulateVertexWeights(triangle.Vertex0, barycentric.X, boneIndices, boneWeights, ref boneCount);
            AccumulateVertexWeights(triangle.Vertex1, barycentric.Y, boneIndices, boneWeights, ref boneCount);
            AccumulateVertexWeights(triangle.Vertex2, barycentric.Z, boneIndices, boneWeights, ref boneCount);

            if (boneCount == 0)
                return (new Vector4(triangle.Vertex0.BlendIndices.X, 0, 0, 0), new Vector4(1, 0, 0, 0));

            for (var outer = 0; outer < boneCount - 1; outer++)
            {
                for (var inner = outer + 1; inner < boneCount; inner++)
                {
                    var weightDifference = boneWeights[inner] - boneWeights[outer];
                    var shouldSwap = weightDifference > WeightSortTieTolerance ||
                        MathF.Abs(weightDifference) <= WeightSortTieTolerance &&
                        boneIndices[inner] < boneIndices[outer];
                    if (!shouldSwap)
                        continue;

                    (boneWeights[outer], boneWeights[inner]) = (boneWeights[inner], boneWeights[outer]);
                    (boneIndices[outer], boneIndices[inner]) = (boneIndices[inner], boneIndices[outer]);
                }
            }

            var outputCount = Math.Min(4, boneCount);
            var totalWeight = 0f;
            for (var index = 0; index < outputCount; index++)
                totalWeight += boneWeights[index];

            if (!float.IsFinite(totalWeight) || totalWeight <= MinimumWeight)
                return (new Vector4(triangle.Vertex0.BlendIndices.X, 0, 0, 0), new Vector4(1, 0, 0, 0));

            var bones = Vector4.Zero;
            var weights = Vector4.Zero;
            for (var index = 0; index < outputCount; index++)
            {
                SetComponent(ref bones, index, boneIndices[index]);
                SetComponent(ref weights, index, boneWeights[index] / totalWeight);
            }

            return (bones, weights);
        }

        void AccumulateVertexWeights(
            VertexPositionNormalTextureCustom vertex,
            float barycentricWeight,
            Span<int> boneIndices,
            Span<float> boneWeights,
            ref int boneCount)
        {
            for (var slot = 0; slot < _weightSlotCount; slot++)
            {
                var weight = GetComponent(vertex.BlendWeights, slot) * barycentricWeight;
                if (!float.IsFinite(weight) || weight <= MinimumWeight)
                    continue;

                var boneIndex = (int)GetComponent(vertex.BlendIndices, slot);
                var existingIndex = -1;
                for (var index = 0; index < boneCount; index++)
                {
                    if (boneIndices[index] == boneIndex)
                    {
                        existingIndex = index;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    boneWeights[existingIndex] += weight;
                }
                else
                {
                    boneIndices[boneCount] = boneIndex;
                    boneWeights[boneCount] = weight;
                    boneCount++;
                }
            }
        }

        static BoundingBox GetTriangleBounds(Triangle triangle)
        {
            return new BoundingBox(
                Vector3.Min(triangle.Point0, Vector3.Min(triangle.Point1, triangle.Point2)),
                Vector3.Max(triangle.Point0, Vector3.Max(triangle.Point1, triangle.Point2)));
        }

        static BoundingBox Merge(BoundingBox left, BoundingBox right)
        {
            return new BoundingBox(
                Vector3.Min(left.Min, right.Min),
                Vector3.Max(left.Max, right.Max));
        }

        static float DistanceSquared(Vector3 point, BoundingBox bounds)
        {
            var distance = Vector3.Zero;
            if (point.X < bounds.Min.X)
                distance.X = bounds.Min.X - point.X;
            else if (point.X > bounds.Max.X)
                distance.X = point.X - bounds.Max.X;

            if (point.Y < bounds.Min.Y)
                distance.Y = bounds.Min.Y - point.Y;
            else if (point.Y > bounds.Max.Y)
                distance.Y = point.Y - bounds.Max.Y;

            if (point.Z < bounds.Min.Z)
                distance.Z = bounds.Min.Z - point.Z;
            else if (point.Z > bounds.Max.Z)
                distance.Z = point.Z - bounds.Max.Z;

            return distance.LengthSquared();
        }

        static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 point0, Vector3 point1, Vector3 point2)
        {
            var edge01 = point1 - point0;
            var edge02 = point2 - point0;
            var pointFrom0 = point - point0;
            var dot01 = Vector3.Dot(edge01, pointFrom0);
            var dot02 = Vector3.Dot(edge02, pointFrom0);
            if (dot01 <= 0 && dot02 <= 0)
                return point0;

            var pointFrom1 = point - point1;
            var dot11 = Vector3.Dot(edge01, pointFrom1);
            var dot12 = Vector3.Dot(edge02, pointFrom1);
            if (dot11 >= 0 && dot12 <= dot11)
                return point1;

            var vertexRegion2 = dot01 * dot12 - dot11 * dot02;
            if (vertexRegion2 <= 0 && dot01 >= 0 && dot11 <= 0)
                return point0 + dot01 / (dot01 - dot11) * edge01;

            var pointFrom2 = point - point2;
            var dot21 = Vector3.Dot(edge01, pointFrom2);
            var dot22 = Vector3.Dot(edge02, pointFrom2);
            if (dot22 >= 0 && dot21 <= dot22)
                return point2;

            var vertexRegion1 = dot21 * dot02 - dot01 * dot22;
            if (vertexRegion1 <= 0 && dot02 >= 0 && dot22 <= 0)
                return point0 + dot02 / (dot02 - dot22) * edge02;

            var vertexRegion0 = dot11 * dot22 - dot21 * dot12;
            if (vertexRegion0 <= 0 && dot12 - dot11 >= 0 && dot21 - dot22 >= 0)
                return point1 + (dot12 - dot11) / ((dot12 - dot11) + (dot21 - dot22)) * (point2 - point1);

            var inverseDenominator = 1f / (vertexRegion0 + vertexRegion1 + vertexRegion2);
            var coordinate1 = vertexRegion1 * inverseDenominator;
            var coordinate2 = vertexRegion2 * inverseDenominator;
            return point0 + edge01 * coordinate1 + edge02 * coordinate2;
        }

        static Vector3 ComputeBarycentricCoordinates(Vector3 point, Vector3 point0, Vector3 point1, Vector3 point2)
        {
            var edge0 = point1 - point0;
            var edge1 = point2 - point0;
            var pointEdge = point - point0;
            var dot00 = Vector3.Dot(edge0, edge0);
            var dot01 = Vector3.Dot(edge0, edge1);
            var dot11 = Vector3.Dot(edge1, edge1);
            var dot20 = Vector3.Dot(pointEdge, edge0);
            var dot21 = Vector3.Dot(pointEdge, edge1);
            var inverseDenominator = 1f / (dot00 * dot11 - dot01 * dot01);
            var coordinate1 = (dot11 * dot20 - dot01 * dot21) * inverseDenominator;
            var coordinate2 = (dot00 * dot21 - dot01 * dot20) * inverseDenominator;
            var coordinate0 = 1f - coordinate1 - coordinate2;
            return new Vector3(coordinate0, coordinate1, coordinate2);
        }

        static float GetComponent(Vector3 vector, int axis)
        {
            return axis switch
            {
                0 => vector.X,
                1 => vector.Y,
                _ => vector.Z
            };
        }

        static float GetComponent(Vector4 vector, int component)
        {
            return component switch
            {
                0 => vector.X,
                1 => vector.Y,
                2 => vector.Z,
                _ => vector.W
            };
        }

        static void SetComponent(ref Vector4 vector, int component, float value)
        {
            switch (component)
            {
                case 0:
                    vector.X = value;
                    break;
                case 1:
                    vector.Y = value;
                    break;
                case 2:
                    vector.Z = value;
                    break;
                default:
                    vector.W = value;
                    break;
            }
        }

        readonly record struct Triangle(
            Vector3 Point0,
            Vector3 Point1,
            Vector3 Point2,
            VertexPositionNormalTextureCustom Vertex0,
            VertexPositionNormalTextureCustom Vertex1,
            VertexPositionNormalTextureCustom Vertex2)
        {
            public Vector3 Centroid => (Point0 + Point1 + Point2) / 3f;
        }

        sealed class BvhNode
        {
            public BoundingBox Bounds { get; }
            public int Start { get; }
            public int Count { get; }
            public BvhNode? Left { get; }
            public BvhNode? Right { get; }
            public bool IsLeaf => Left == null;

            public BvhNode(BoundingBox bounds, int start, int count)
            {
                Bounds = bounds;
                Start = start;
                Count = count;
            }

            public BvhNode(BoundingBox bounds, BvhNode left, BvhNode right)
            {
                Bounds = bounds;
                Left = left;
                Right = right;
            }
        }
    }
}
