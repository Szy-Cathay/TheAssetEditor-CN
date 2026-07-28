using GameWorld.Core.Animation;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace GameWorld.Core.Utility
{
    public static class IntersectionMath
    {
        const float ElementSelectionDistancePixels = 25.0f;
        // Blender gives already selected elements a small disadvantage when picking nearby items.
        const float SelectedElementBiasPixels = 5.0f;
        const float DepthComparisonEpsilon = 0.00001f;

        public static float? IntersectObject(Ray ray, MeshObject geometry, Matrix matrix)
        {
            // BoundingBox pre-check: skip expensive per-triangle test if ray misses the whole mesh
            var inverseTransform = Matrix.Invert(matrix);
            var localRay = new Ray(
                Vector3.Transform(ray.Position, inverseTransform),
                Vector3.TransformNormal(ray.Direction, inverseTransform));
            if (localRay.Intersects(geometry.BoundingBox) == null)
                return null;

            var res = IntersectFace(ray, geometry, matrix, out var _);
            return res;
        }

        public static float? IntersectObject(
            Ray ray,
            MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions)
        {
            ValidateWorldPositions(geometry, worldPositions);
            if (worldPositions.Count == 0 ||
                ray.Intersects(
                    BoundingBox.CreateFromPoints(worldPositions)) == null)
            {
                return null;
            }

            return IntersectFace(
                ray,
                geometry,
                worldPositions,
                out _);
        }

        public static float? IntersectVertex(Vector2 mouseScreenPos, MeshObject geometry, Matrix modelMatrix,
            Matrix viewProjection, float viewportWidth, float viewportHeight, out int selectedVertex,
            IReadOnlySet<int>? selectedVertices = null)
        {
            var projectedVertices = ProjectVertices(
                geometry,
                modelMatrix,
                viewProjection,
                viewportWidth,
                viewportHeight);
            return IntersectVertex(
                mouseScreenPos,
                geometry,
                projectedVertices,
                viewportWidth,
                viewportHeight,
                out selectedVertex,
                selectedVertices);
        }

        public static float? IntersectVertex(
            Vector2 mouseScreenPos,
            MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions,
            Matrix viewProjection,
            float viewportWidth,
            float viewportHeight,
            out int selectedVertex,
            IReadOnlySet<int>? selectedVertices = null)
        {
            ValidateWorldPositions(geometry, worldPositions);
            return IntersectVertex(
                mouseScreenPos,
                geometry,
                ProjectVertices(
                    worldPositions,
                    viewProjection,
                    viewportWidth,
                    viewportHeight),
                viewportWidth,
                viewportHeight,
                out selectedVertex,
                selectedVertices);
        }

        static float? IntersectVertex(
            Vector2 mouseScreenPos,
            MeshObject geometry,
            ProjectedVertex[] projectedVertices,
            float viewportWidth,
            float viewportHeight,
            out int selectedVertex,
            IReadOnlySet<int>? selectedVertices)
        {
            var depthBuffer = BuildLocalDepthBuffer(
                mouseScreenPos,
                ElementSelectionDistancePixels,
                projectedVertices,
                geometry.IndexArray,
                viewportWidth,
                viewportHeight);

            selectedVertex = -1;
            var bestDistance = float.MaxValue;
            var bestBiasedDistance = float.MaxValue;
            var bestDepth = float.MaxValue;

            for (var i = 0; i < projectedVertices.Length; i++)
            {
                var projectedVertex = projectedVertices[i];
                if (!projectedVertex.IsValid ||
                    !IsInsideViewport(projectedVertex.ScreenPosition, viewportWidth, viewportHeight))
                    continue;

                var distance = ManhattanDistance(mouseScreenPos, projectedVertex.ScreenPosition);
                if (distance > ElementSelectionDistancePixels ||
                    !depthBuffer.IsVisible(projectedVertex.ScreenPosition, projectedVertex.Depth))
                    continue;

                var biasedDistance = distance +
                    (selectedVertices?.Contains(i) == true ? SelectedElementBiasPixels : 0.0f);
                if (biasedDistance > ElementSelectionDistancePixels)
                    continue;

                if (biasedDistance < bestBiasedDistance ||
                    MathF.Abs(biasedDistance - bestBiasedDistance) <= DepthComparisonEpsilon &&
                    projectedVertex.Depth < bestDepth)
                {
                    bestDistance = distance;
                    bestBiasedDistance = biasedDistance;
                    bestDepth = projectedVertex.Depth;
                    selectedVertex = i;
                }
            }

            return selectedVertex == -1 ? null : bestDistance;
        }

        public static float? IntersectFace(Ray ray, MeshObject geometry, Matrix matrix, out int? face)
        {
            face = null;

            var inverseTransform = Matrix.Invert(matrix);
            ray.Position = Vector3.Transform(ray.Position, inverseTransform);
            ray.Direction = Vector3.TransformNormal(ray.Direction, inverseTransform);

            // BoundingBox pre-check: skip O(n) triangle test if ray misses the whole mesh
            if (ray.Intersects(geometry.BoundingBox) == null)
                return null;

            var faceIndex = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < geometry.GetIndexCount(); i += 3)
            {
                var index0 = geometry.GetIndex(i + 0);
                var index1 = geometry.GetIndex(i + 1);
                var index2 = geometry.GetIndex(i + 2);

                var vert0 = geometry.GetVertexById(index0);
                var vert1 = geometry.GetVertexById(index1);
                var vert2 = geometry.GetVertexById(index2);

                var res = MollerTrumboreIntersection(ray, vert0, vert1, vert2, out var intersectionPoint);
                if (res)
                {
                    var dist = intersectionPoint;
                    if (dist < bestDistance)
                    {
                        faceIndex = i;
                        bestDistance = dist.Value;
                    }
                }
            }

            if (faceIndex == -1)
                return null;

            face = faceIndex;
            return bestDistance;
        }

        public static float? IntersectFace(
            Ray ray,
            MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions,
            out int? face)
        {
            ValidateWorldPositions(geometry, worldPositions);
            face = null;
            if (worldPositions.Count == 0 ||
                ray.Intersects(
                    BoundingBox.CreateFromPoints(worldPositions)) == null)
            {
                return null;
            }

            var faceIndex = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < geometry.GetIndexCount(); i += 3)
            {
                var index0 = geometry.GetIndex(i);
                var index1 = geometry.GetIndex(i + 1);
                var index2 = geometry.GetIndex(i + 2);
                if (!MollerTrumboreIntersection(
                        ray,
                        worldPositions[index0],
                        worldPositions[index1],
                        worldPositions[index2],
                        out var distance) ||
                    distance >= bestDistance)
                {
                    continue;
                }

                faceIndex = i;
                bestDistance = distance.Value;
            }

            if (faceIndex == -1)
                return null;

            face = faceIndex;
            return bestDistance;
        }

        public static bool IntersectObject(BoundingFrustum boundingFrustum, MeshObject geometry, Matrix matrix)
        {
            // BoundingBox pre-check: transform mesh bounds to world space and test against frustum
            var transformedBox = TransformBoundingBox(geometry.BoundingBox, matrix);
            if (boundingFrustum.Contains(transformedBox) == ContainmentType.Disjoint)
                return false;

            // Detailed vertex check for meshes whose BoundingBox intersects the frustum
            for (var i = 0; i < geometry.VertexCount(); i++)
            {
                if (boundingFrustum.Contains(Vector3.Transform(geometry.GetVertexById(i), matrix)) != ContainmentType.Disjoint)
                    return true;
            }

            return false;
        }

        public static bool IntersectObject(
            BoundingFrustum boundingFrustum,
            MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions)
        {
            ValidateWorldPositions(geometry, worldPositions);
            if (worldPositions.Count == 0 ||
                boundingFrustum.Contains(
                    BoundingBox.CreateFromPoints(worldPositions)) ==
                    ContainmentType.Disjoint)
            {
                return false;
            }

            for (var i = 0; i < worldPositions.Count; i++)
            {
                if (boundingFrustum.Contains(worldPositions[i]) !=
                    ContainmentType.Disjoint)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IntersectFaces(BoundingFrustum boundingFrustum, MeshObject geometry, Matrix matrix, out List<int> faces)
        {
            faces = new List<int>();

            // BoundingBox pre-check
            var transformedBox = TransformBoundingBox(geometry.BoundingBox, matrix);
            if (boundingFrustum.Contains(transformedBox) == ContainmentType.Disjoint)
                return false;

            // Transform vertices directly from VertexArray to avoid GetVertexList() allocation
            var vertCount = geometry.VertexArray.Length;
            var transformedVerts = new Vector3[vertCount];
            for (var i = 0; i < vertCount; i++)
                transformedVerts[i] = Vector3.Transform(geometry.GetVertexById(i), matrix);

            // Use IndexArray directly to avoid GetIndexBuffer() allocation
            for (var i = 0; i < geometry.IndexArray.Length; i += 3)
            {
                var index0 = geometry.IndexArray[i + 0];
                var index1 = geometry.IndexArray[i + 1];
                var index2 = geometry.IndexArray[i + 2];

                if (boundingFrustum.Contains(transformedVerts[index0]) != ContainmentType.Disjoint)
                    faces.Add(i);
                else if (boundingFrustum.Contains(transformedVerts[index1]) != ContainmentType.Disjoint)
                    faces.Add(i);
                else if (boundingFrustum.Contains(transformedVerts[index2]) != ContainmentType.Disjoint)
                    faces.Add(i);
            }

            if (faces.Count == 0)
                faces = null;
            return faces != null;
        }

        public static bool IntersectFaces(
            BoundingFrustum boundingFrustum,
            MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions,
            out List<int> faces)
        {
            ValidateWorldPositions(geometry, worldPositions);
            faces = new List<int>();
            if (worldPositions.Count == 0 ||
                boundingFrustum.Contains(
                    BoundingBox.CreateFromPoints(worldPositions)) ==
                    ContainmentType.Disjoint)
            {
                return false;
            }

            for (var i = 0; i < geometry.IndexArray.Length; i += 3)
            {
                var index0 = geometry.IndexArray[i];
                var index1 = geometry.IndexArray[i + 1];
                var index2 = geometry.IndexArray[i + 2];

                if (boundingFrustum.Contains(worldPositions[index0]) !=
                        ContainmentType.Disjoint ||
                    boundingFrustum.Contains(worldPositions[index1]) !=
                        ContainmentType.Disjoint ||
                    boundingFrustum.Contains(worldPositions[index2]) !=
                        ContainmentType.Disjoint)
                {
                    faces.Add(i);
                }
            }

            if (faces.Count == 0)
                faces = null;
            return faces != null;
        }

        public static bool IntersectVertices(BoundingFrustum boundingFrustum, MeshObject geometry, Matrix matrix, out List<int> vertices)
        {
            vertices = new List<int>();
            var visitedVertices = new bool[geometry.VertexArray.Length];

            for (var i = 0; i < geometry.IndexArray.Length; i++)
            {
                var index = geometry.IndexArray[i];
                if (!visitedVertices[index] &&
                    boundingFrustum.Contains(Vector3.Transform(geometry.GetVertexById(index), matrix)) != ContainmentType.Disjoint)
                {
                    visitedVertices[index] = true;
                    vertices.Add(index);
                }
            }

            if (vertices.Count == 0)
                vertices = null;
            return vertices != null;
        }

        public static bool IntersectVertices(
            BoundingFrustum boundingFrustum,
            MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions,
            out List<int> vertices)
        {
            ValidateWorldPositions(geometry, worldPositions);
            vertices = new List<int>();
            var visitedVertices =
                new bool[geometry.VertexArray.Length];
            for (var i = 0; i < geometry.IndexArray.Length; i++)
            {
                var index = geometry.IndexArray[i];
                if (!visitedVertices[index] &&
                    boundingFrustum.Contains(worldPositions[index]) !=
                        ContainmentType.Disjoint)
                {
                    visitedVertices[index] = true;
                    vertices.Add(index);
                }
            }

            if (vertices.Count == 0)
                vertices = null;
            return vertices != null;
        }

        public static ushort FindClosestVertexIndex(MeshObject mesh, Vector3 point, out float distance)
        {
            var closestDist = float.PositiveInfinity;
            var bestVertexIndex = -1;

            for (var i = 0; i < mesh.VertexArray.Length; i++)
            {
                var dist = (point - mesh.VertexArray[i].Position3()).LengthSquared();
                if (dist < closestDist)
                {
                    closestDist = dist;
                    bestVertexIndex = i;
                }
            }

            distance = closestDist;
            return (ushort)bestVertexIndex;
        }

        public static bool MollerTrumboreIntersection(Ray r, Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, out float? distance)
        {
            //Source : https://en.wikipedia.org/wiki/M%C3%B6ller%E2%80%93Trumbore_intersection_algorithm
            const float EPSILON = 0.0000001f;
            Vector3 edge1, edge2, h, s, q;
            float a, f, u, v;
            edge1 = vertex1 - vertex0;
            edge2 = vertex2 - vertex0;
            h = Vector3.Cross(r.Direction, edge2);
            a = Vector3.Dot(edge1, h);
            if (a > -EPSILON && a < EPSILON)
            {
                distance = null;
                return false;    // This ray is parallel to this triangle.
            }
            f = 1.0f / a;
            s = r.Position - vertex0;
            u = f * Vector3.Dot(s, h);
            if (u < 0.0 || u > 1.0)
            {
                distance = null;
                return false;
            }
            q = Vector3.Cross(s, edge1);
            v = f * Vector3.Dot(r.Direction, q);
            if (v < 0.0 || u + v > 1.0)
            {
                distance = null;
                return false;
            }
            // At this stage we can compute t to find out where the intersection point is on the line.
            var t = f * Vector3.Dot(edge2, q);
            if (t > EPSILON) // ray intersection
            {
                distance = t;
                return true;
            }
            else // This means that there is a line intersection but not a ray intersection.
            {
                distance = null;
                return false;
            }
        }

        public static bool IntersectBones(BoundingFrustum boundingFrustum, Rmv2MeshNode sceneNode, GameSkeleton skeleton, Matrix matrix, out List<int> bones)
        {
            bones = new List<int>();

            if (sceneNode.AnimationPlayer == null) return false;

            var animPlayer = sceneNode.AnimationPlayer;
            var currentFrame = animPlayer.GetCurrentAnimationFrame();

            if (currentFrame == null) return false;
            var totalBones = currentFrame.BoneTransforms.Count;

            for (var boneIdx = 0; boneIdx < totalBones; boneIdx++)
            {
                var bone = currentFrame.GetSkeletonAnimatedWorld(skeleton, boneIdx);
                bone.Decompose(out var _, out var _, out var trans);
                if (boundingFrustum.Contains(Vector3.Transform(trans, matrix)) != ContainmentType.Disjoint)
                    bones.Add(boneIdx);
            }

            bones = bones.Distinct().ToList();
            if (bones.Count() == 0)
                bones = null;
            return bones != null;
        }

        /// <summary>
        /// Transform a BoundingBox by a matrix (transform 8 corners, rebuild from transformed points).
        /// </summary>
        public static BoundingBox TransformBoundingBox(BoundingBox box, Matrix matrix)
        {
            var corners = box.GetCorners();
            Vector3.Transform(corners, ref matrix, corners);
            return BoundingBox.CreateFromPoints(corners);
        }

        /// <summary>
        /// Pick an edge using a stable screen-space threshold in both perspective
        /// and orthographic projections.
        /// </summary>
        public static float? IntersectEdge(Vector2 mouseScreenPos, MeshObject geometry, Matrix modelMatrix,
            Matrix viewProjection, float viewportWidth, float viewportHeight, out (int v0, int v1) selectedEdge,
            IReadOnlySet<(int v0, int v1)>? selectedEdges = null)
        {
            var projectedVertices = ProjectVertices(
                geometry,
                modelMatrix,
                viewProjection,
                viewportWidth,
                viewportHeight);
            return IntersectEdge(
                mouseScreenPos,
                geometry,
                projectedVertices,
                viewportWidth,
                viewportHeight,
                out selectedEdge,
                selectedEdges);
        }

        public static float? IntersectEdge(
            Vector2 mouseScreenPos,
            MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions,
            Matrix viewProjection,
            float viewportWidth,
            float viewportHeight,
            out (int v0, int v1) selectedEdge,
            IReadOnlySet<(int v0, int v1)>? selectedEdges = null)
        {
            ValidateWorldPositions(geometry, worldPositions);
            return IntersectEdge(
                mouseScreenPos,
                geometry,
                ProjectVertices(
                    worldPositions,
                    viewProjection,
                    viewportWidth,
                    viewportHeight),
                viewportWidth,
                viewportHeight,
                out selectedEdge,
                selectedEdges);
        }

        /// <summary>
        /// Pick edges within a frustum (for rectangle selection).
        /// </summary>
        public static bool IntersectEdges(BoundingFrustum boundingFrustum, MeshObject geometry, Matrix matrix, out List<(int v0, int v1)> edges)
        {
            edges = new List<(int, int)>();
            var processedEdges = new HashSet<(int, int)>();
            var indexBuffer = geometry.IndexArray;

            // Pre-transform all vertices
            var vertCount = geometry.VertexArray.Length;
            var transformedVerts = new Vector3[vertCount];
            for (var i = 0; i < vertCount; i++)
                transformedVerts[i] = Vector3.Transform(geometry.GetVertexById(i), matrix);

            for (var i = 0; i < indexBuffer.Length; i += 3)
            {
                var i0 = indexBuffer[i];
                var i1 = indexBuffer[i + 1];
                var i2 = indexBuffer[i + 2];

                var edgeList = new[] { (Math.Min(i0, i1), Math.Max(i0, i1)), (Math.Min(i1, i2), Math.Max(i1, i2)), (Math.Min(i0, i2), Math.Max(i0, i2)) };

                foreach (var edge in edgeList)
                {
                    if (processedEdges.Contains(edge))
                        continue;
                    processedEdges.Add(edge);

                    // Edge is selected if both vertices are in frustum
                    if (boundingFrustum.Contains(transformedVerts[edge.Item1]) != ContainmentType.Disjoint &&
                        boundingFrustum.Contains(transformedVerts[edge.Item2]) != ContainmentType.Disjoint)
                    {
                        edges.Add(edge);
                    }
                }
            }

            if (edges.Count == 0)
                return false;
            return true;
        }

        public static bool IntersectEdges(
            BoundingFrustum boundingFrustum,
            MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions,
            out List<(int v0, int v1)> edges)
        {
            ValidateWorldPositions(geometry, worldPositions);
            edges = new List<(int, int)>();
            var processedEdges = new HashSet<(int, int)>();
            var indexBuffer = geometry.IndexArray;
            for (var i = 0; i < indexBuffer.Length; i += 3)
            {
                var i0 = indexBuffer[i];
                var i1 = indexBuffer[i + 1];
                var i2 = indexBuffer[i + 2];
                var edgeList = new[]
                {
                    (Math.Min(i0, i1), Math.Max(i0, i1)),
                    (Math.Min(i1, i2), Math.Max(i1, i2)),
                    (Math.Min(i0, i2), Math.Max(i0, i2))
                };

                foreach (var edge in edgeList)
                {
                    if (!processedEdges.Add(edge))
                        continue;

                    if (boundingFrustum.Contains(
                            worldPositions[edge.Item1]) !=
                            ContainmentType.Disjoint &&
                        boundingFrustum.Contains(
                            worldPositions[edge.Item2]) !=
                            ContainmentType.Disjoint)
                    {
                        edges.Add(edge);
                    }
                }
            }

            return edges.Count != 0;
        }

        static ProjectedVertex[] ProjectVertices(
            MeshObject geometry,
            Matrix modelMatrix,
            Matrix viewProjection,
            float viewportWidth,
            float viewportHeight)
        {
            var projectedVertices = new ProjectedVertex[geometry.VertexArray.Length];
            for (var i = 0; i < projectedVertices.Length; i++)
            {
                var worldPosition = Vector3.Transform(geometry.GetVertexById(i), modelMatrix);
                var clipPosition = Vector4.Transform(new Vector4(worldPosition, 1.0f), viewProjection);
                if (clipPosition.W <= 0.0f)
                    continue;

                var inverseW = 1.0f / clipPosition.W;
                var depth = clipPosition.Z * inverseW;
                var screenPosition = new Vector2(
                    (clipPosition.X * inverseW + 1.0f) * 0.5f * viewportWidth,
                    (1.0f - clipPosition.Y * inverseW) * 0.5f * viewportHeight);
                if (!float.IsFinite(screenPosition.X) ||
                    !float.IsFinite(screenPosition.Y) ||
                    !float.IsFinite(depth) ||
                    depth < 0.0f ||
                    depth > 1.0f)
                    continue;

                projectedVertices[i] = new ProjectedVertex(screenPosition, depth);
            }

            return projectedVertices;
        }

        static ProjectedVertex[] ProjectVertices(
            IReadOnlyList<Vector3> worldPositions,
            Matrix viewProjection,
            float viewportWidth,
            float viewportHeight)
        {
            var projectedVertices =
                new ProjectedVertex[worldPositions.Count];
            for (var i = 0; i < projectedVertices.Length; i++)
            {
                var clipPosition = Vector4.Transform(
                    new Vector4(worldPositions[i], 1.0f),
                    viewProjection);
                if (clipPosition.W <= 0.0f)
                    continue;

                var inverseW = 1.0f / clipPosition.W;
                var depth = clipPosition.Z * inverseW;
                var screenPosition = new Vector2(
                    (clipPosition.X * inverseW + 1.0f) *
                        0.5f * viewportWidth,
                    (1.0f - clipPosition.Y * inverseW) *
                        0.5f * viewportHeight);
                if (!float.IsFinite(screenPosition.X) ||
                    !float.IsFinite(screenPosition.Y) ||
                    !float.IsFinite(depth) ||
                    depth < 0.0f ||
                    depth > 1.0f)
                {
                    continue;
                }

                projectedVertices[i] =
                    new ProjectedVertex(screenPosition, depth);
            }

            return projectedVertices;
        }

        static float? IntersectEdge(
            Vector2 mouseScreenPos,
            MeshObject geometry,
            ProjectedVertex[] projectedVertices,
            float viewportWidth,
            float viewportHeight,
            out (int v0, int v1) selectedEdge,
            IReadOnlySet<(int v0, int v1)>? selectedEdges)
        {
            var depthBuffer = BuildLocalDepthBuffer(
                mouseScreenPos,
                ElementSelectionDistancePixels,
                projectedVertices,
                geometry.IndexArray,
                viewportWidth,
                viewportHeight);
            var processedEdges = new HashSet<(int, int)>();
            var bestEdge = (-1, -1);
            var bestDistance = float.MaxValue;
            var bestBiasedDistance = float.MaxValue;
            var bestDepth = float.MaxValue;

            void TestEdge(int firstIndex, int secondIndex)
            {
                var edge = (
                    Math.Min(firstIndex, secondIndex),
                    Math.Max(firstIndex, secondIndex));
                if (!processedEdges.Add(edge))
                    return;

                var firstProjected = projectedVertices[edge.Item1];
                var secondProjected = projectedVertices[edge.Item2];
                if (!firstProjected.IsValid ||
                    !secondProjected.IsValid)
                {
                    return;
                }

                var distance = PointToLineSegmentManhattanDistance(
                    mouseScreenPos,
                    firstProjected.ScreenPosition,
                    secondProjected.ScreenPosition,
                    out var amount,
                    out var closestPoint);
                if (distance > ElementSelectionDistancePixels ||
                    !IsInsideViewport(
                        closestPoint,
                        viewportWidth,
                        viewportHeight))
                {
                    return;
                }

                var depth = MathHelper.Lerp(
                    firstProjected.Depth,
                    secondProjected.Depth,
                    amount);
                if (!depthBuffer.IsVisible(closestPoint, depth))
                    return;

                var biasedDistance = distance +
                    (selectedEdges?.Contains(edge) == true
                        ? SelectedElementBiasPixels
                        : 0.0f);
                if (biasedDistance > ElementSelectionDistancePixels)
                    return;

                if (biasedDistance < bestBiasedDistance ||
                    MathF.Abs(
                        biasedDistance - bestBiasedDistance) <=
                        DepthComparisonEpsilon &&
                    depth < bestDepth)
                {
                    bestDistance = distance;
                    bestBiasedDistance = biasedDistance;
                    bestDepth = depth;
                    bestEdge = edge;
                }
            }

            var indexBuffer = geometry.IndexArray;
            for (var i = 0; i < indexBuffer.Length; i += 3)
            {
                var first = indexBuffer[i];
                var second = indexBuffer[i + 1];
                var third = indexBuffer[i + 2];
                TestEdge(first, second);
                TestEdge(second, third);
                TestEdge(first, third);
            }

            selectedEdge = bestEdge;
            return bestEdge.Item1 == -1
                ? null
                : bestDistance;
        }

        static void ValidateWorldPositions(
            MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions)
        {
            ArgumentNullException.ThrowIfNull(geometry);
            ArgumentNullException.ThrowIfNull(worldPositions);
            if (worldPositions.Count != geometry.VertexCount())
            {
                throw new ArgumentException(
                    "The evaluated position count must match the mesh vertex count.",
                    nameof(worldPositions));
            }
        }

        static LocalDepthBuffer BuildLocalDepthBuffer(
            Vector2 mouseScreenPosition,
            float selectionDistance,
            ProjectedVertex[] projectedVertices,
            ushort[] indices,
            float viewportWidth,
            float viewportHeight)
        {
            // Blender uses a depth-tested selection buffer. A small CPU window avoids GPU readback.
            var radius = (int)MathF.Ceiling(selectionDistance);
            var viewportPixelWidth = (int)viewportWidth;
            var viewportPixelHeight = (int)viewportHeight;
            var centerX = (int)MathF.Floor(mouseScreenPosition.X);
            var centerY = (int)MathF.Floor(mouseScreenPosition.Y);
            var left = Math.Max(0, centerX - radius);
            var top = Math.Max(0, centerY - radius);
            var right = Math.Min(viewportPixelWidth - 1, centerX + radius);
            var bottom = Math.Min(viewportPixelHeight - 1, centerY + radius);
            if (right < left || bottom < top)
                return LocalDepthBuffer.Empty;

            var depthBuffer = new LocalDepthBuffer(left, top, right - left + 1, bottom - top + 1);
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var first = projectedVertices[indices[i]];
                var second = projectedVertices[indices[i + 1]];
                var third = projectedVertices[indices[i + 2]];
                if (!first.IsValid || !second.IsValid || !third.IsValid)
                    continue;

                depthBuffer.RasterizeTriangle(first, second, third);
            }

            return depthBuffer;
        }

        static bool IsInsideViewport(Vector2 point, float viewportWidth, float viewportHeight)
        {
            return point.X >= 0.0f &&
                   point.Y >= 0.0f &&
                   point.X < viewportWidth &&
                   point.Y < viewportHeight;
        }

        static float ManhattanDistance(Vector2 first, Vector2 second)
        {
            return MathF.Abs(first.X - second.X) + MathF.Abs(first.Y - second.Y);
        }

        static float PointToLineSegmentManhattanDistance(
            Vector2 point,
            Vector2 segmentStart,
            Vector2 segmentEnd,
            out float amount,
            out Vector2 closestPoint)
        {
            var segment = segmentEnd - segmentStart;
            var segmentLengthSquared = segment.LengthSquared();
            if (segmentLengthSquared == 0.0f)
            {
                amount = 0.0f;
                closestPoint = segmentStart;
                return ManhattanDistance(point, closestPoint);
            }

            amount = Vector2.Dot(point - segmentStart, segment) / segmentLengthSquared;
            amount = MathHelper.Clamp(amount, 0.0f, 1.0f);
            closestPoint = segmentStart + segment * amount;
            return ManhattanDistance(point, closestPoint);
        }

        readonly struct ProjectedVertex
        {
            public bool IsValid { get; }
            public Vector2 ScreenPosition { get; }
            public float Depth { get; }

            public ProjectedVertex(Vector2 screenPosition, float depth)
            {
                IsValid = true;
                ScreenPosition = screenPosition;
                Depth = depth;
            }
        }

        sealed class LocalDepthBuffer
        {
            public static LocalDepthBuffer Empty { get; } = new(0, 0, 0, 0);

            readonly int _left;
            readonly int _top;
            readonly int _width;
            readonly int _height;
            readonly float[] _depths;

            public LocalDepthBuffer(int left, int top, int width, int height)
            {
                _left = left;
                _top = top;
                _width = width;
                _height = height;
                _depths = new float[width * height];
                Array.Fill(_depths, float.PositiveInfinity);
            }

            public void RasterizeTriangle(
                ProjectedVertex first,
                ProjectedVertex second,
                ProjectedVertex third)
            {
                var area = EdgeFunction(
                    first.ScreenPosition,
                    second.ScreenPosition,
                    third.ScreenPosition);
                if (MathF.Abs(area) <= DepthComparisonEpsilon)
                    return;

                var minX = Math.Max(
                    _left,
                    (int)MathF.Floor(MathF.Min(
                        first.ScreenPosition.X,
                        MathF.Min(second.ScreenPosition.X, third.ScreenPosition.X))));
                var maxX = Math.Min(
                    _left + _width - 1,
                    (int)MathF.Ceiling(MathF.Max(
                        first.ScreenPosition.X,
                        MathF.Max(second.ScreenPosition.X, third.ScreenPosition.X))));
                var minY = Math.Max(
                    _top,
                    (int)MathF.Floor(MathF.Min(
                        first.ScreenPosition.Y,
                        MathF.Min(second.ScreenPosition.Y, third.ScreenPosition.Y))));
                var maxY = Math.Min(
                    _top + _height - 1,
                    (int)MathF.Ceiling(MathF.Max(
                        first.ScreenPosition.Y,
                        MathF.Max(second.ScreenPosition.Y, third.ScreenPosition.Y))));

                for (var y = minY; y <= maxY; y++)
                {
                    for (var x = minX; x <= maxX; x++)
                    {
                        var pixelCenter = new Vector2(x + 0.5f, y + 0.5f);
                        var firstWeight = EdgeFunction(
                            second.ScreenPosition,
                            third.ScreenPosition,
                            pixelCenter) / area;
                        var secondWeight = EdgeFunction(
                            third.ScreenPosition,
                            first.ScreenPosition,
                            pixelCenter) / area;
                        var thirdWeight = 1.0f - firstWeight - secondWeight;
                        if (firstWeight < -DepthComparisonEpsilon ||
                            secondWeight < -DepthComparisonEpsilon ||
                            thirdWeight < -DepthComparisonEpsilon)
                            continue;

                        var depth =
                            first.Depth * firstWeight +
                            second.Depth * secondWeight +
                            third.Depth * thirdWeight;
                        var bufferIndex = (y - _top) * _width + x - _left;
                        if (depth < _depths[bufferIndex])
                            _depths[bufferIndex] = depth;
                    }
                }
            }

            public bool IsVisible(Vector2 screenPosition, float depth)
            {
                if (_width == 0 || _height == 0)
                    return true;

                var centerX = (int)MathF.Floor(screenPosition.X);
                var centerY = (int)MathF.Floor(screenPosition.Y);
                var farthestSurfaceDepth = float.NegativeInfinity;
                for (var y = centerY - 1; y <= centerY + 1; y++)
                {
                    if (y < _top || y >= _top + _height)
                        continue;

                    for (var x = centerX - 1; x <= centerX + 1; x++)
                    {
                        if (x < _left || x >= _left + _width)
                            continue;

                        var surfaceDepth = _depths[(y - _top) * _width + x - _left];
                        if (float.IsFinite(surfaceDepth))
                            farthestSurfaceDepth = MathF.Max(farthestSurfaceDepth, surfaceDepth);
                    }
                }

                return !float.IsFinite(farthestSurfaceDepth) ||
                       depth <= farthestSurfaceDepth + DepthComparisonEpsilon;
            }

            static float EdgeFunction(Vector2 first, Vector2 second, Vector2 point)
            {
                return (point.X - first.X) * (second.Y - first.Y) -
                       (point.Y - first.Y) * (second.X - first.X);
            }
        }
    }
}
