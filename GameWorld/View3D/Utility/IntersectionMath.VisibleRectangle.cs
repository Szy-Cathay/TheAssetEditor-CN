using GameWorld.Core.Rendering.Geometry;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Utility
{
    public static partial class IntersectionMath
    {
        public static List<int> IntersectVisibleVertices(Rectangle rectangle, MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions, Matrix viewProjection, int width, int height,
            bool includeOccluded = false, Vector2? circleCenter = null, float circleRadius = 0, Vector2? circleStart = null)
        {
            var (vertices, depth, bounds) = ProjectSelectionRectangle(rectangle, geometry, worldPositions, viewProjection, width, height, skipDepth: includeOccluded);
            var result = new List<int>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                if (vertex.IsValid && Contains(bounds, vertex.ScreenPosition) && InCircle(vertex.ScreenPosition, circleCenter, circleRadius, circleStart) && depth.IsVisible(vertex.ScreenPosition, vertex.Depth))
                    result.Add(i);
            }
            return result;
        }

        public static List<int> IntersectVisibleFaces(Rectangle rectangle, MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions, Matrix viewProjection, int width, int height,
            bool includeOccluded = false, Vector2? circleCenter = null, float circleRadius = 0, Vector2? circleStart = null)
        {
            var (_, depth, bounds) = ProjectSelectionRectangle(rectangle, geometry, worldPositions, viewProjection, width, height, true, includeOccluded);
            if (!includeOccluded)
                return depth.VisibleFaces(circleCenter, circleRadius, circleStart);
            var faces = new List<int>();
            for (var i = 0; i + 2 < geometry.IndexArray.Length; i += 3)
            {
                var center = (worldPositions[geometry.IndexArray[i]] + worldPositions[geometry.IndexArray[i + 1]] +
                    worldPositions[geometry.IndexArray[i + 2]]) / 3;
                var clip = Vector4.Transform(new Vector4(center, 1), viewProjection);
                var projected = ProjectClip(clip, width, height);
                if (clip.Z >= 0 && clip.Z <= clip.W && projected.IsValid && Contains(bounds, projected.ScreenPosition) &&
                    InCircle(projected.ScreenPosition, circleCenter, circleRadius, circleStart))
                    faces.Add(i);
            }
            return faces;
        }

        public static List<(int, int)> IntersectVisibleEdges(Rectangle rectangle, MeshObject geometry,
            IReadOnlyList<Vector3> worldPositions, Matrix viewProjection, int width, int height,
            bool includeOccluded = false, Vector2? circleCenter = null, float circleRadius = 0, Vector2? circleStart = null)
        {
            var (vertices, depth, bounds) = ProjectSelectionRectangle(rectangle, geometry, worldPositions, viewProjection, width, height, skipDepth: includeOccluded);
            var contained = new List<(int, int)>();
            var crossing = new List<(int, int)>();
            var visited = new HashSet<(int, int)>();
            for (var i = 0; i + 2 < geometry.IndexArray.Length; i += 3)
            {
                Test(geometry.IndexArray[i], geometry.IndexArray[i + 1]);
                Test(geometry.IndexArray[i + 1], geometry.IndexArray[i + 2]);
                Test(geometry.IndexArray[i + 2], geometry.IndexArray[i]);
            }
            // Blender prefers fully contained edges, then falls back to crossing edges.
            return circleCenter.HasValue ? contained.Concat(crossing).ToList() : contained.Count > 0 ? contained : crossing;

            void Test(int firstIndex, int secondIndex)
            {
                var edge = (Math.Min(firstIndex, secondIndex), Math.Max(firstIndex, secondIndex));
                if (!visited.Add(edge))
                    return;
                var first = vertices[firstIndex];
                var second = vertices[secondIndex];
                if (bounds.IsEmpty || !ClipProjectedEdge(ref first, ref second, worldPositions[firstIndex], worldPositions[secondIndex], viewProjection, width, height))
                    return;
                var start = 0f;
                var stop = 1f;
                var delta = second.ScreenPosition - first.ScreenPosition;
                if (!ClipInterval(first.ScreenPosition.X, delta.X, bounds.Left, bounds.Right, ref start, ref stop) ||
                    !ClipInterval(first.ScreenPosition.Y, delta.Y, bounds.Top, bounds.Bottom, ref start, ref stop))
                    return;
                var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y)) * (stop - start)));
                for (var step = 0; step <= steps; step++)
                {
                    var amount = MathHelper.Lerp(start, stop, (float)step / steps);
                    var point = first.ScreenPosition + delta * amount;
                    if (!Contains(bounds, point) || !InCircle(point, circleCenter, circleRadius, circleStart) || !depth.IsVisible(point, MathHelper.Lerp(first.Depth, second.Depth, amount)))
                        continue;
                    var result = Contains(bounds, first.ScreenPosition) && Contains(bounds, second.ScreenPosition) ? contained : crossing;
                    result.Add(edge);
                    return;
                }
            }
        }

        static bool InCircle(Vector2 point, Vector2? center, float radius, Vector2? start = null)
        {
            if (!center.HasValue) return true;
            var closest = center.Value;
            if (start.HasValue)
            {
                var direction = center.Value - start.Value;
                var lengthSquared = direction.LengthSquared();
                closest = start.Value + direction * (lengthSquared > 0 ? Math.Clamp(Vector2.Dot(point - start.Value, direction) / lengthSquared, 0, 1) : 0);
            }
            return Vector2.DistanceSquared(point, closest) <= radius * radius;
        }

        static bool ClipInterval(float position, float delta, float minimum, float maximum, ref float start, ref float stop)
        {
            if (MathF.Abs(delta) < 0.000001f)
                return position >= minimum && position < maximum;
            var first = (minimum - position) / delta;
            var second = (maximum - position) / delta;
            start = MathF.Max(start, MathF.Min(first, second));
            stop = MathF.Min(stop, MathF.Max(first, second));
            return start <= stop;
        }

        static bool Contains(Rectangle rectangle, Vector2 point) =>
            point.X >= rectangle.Left && point.X < rectangle.Right && point.Y >= rectangle.Top && point.Y < rectangle.Bottom;

        static (ProjectedVertex[], LocalDepthBuffer, Rectangle) ProjectSelectionRectangle(Rectangle rectangle,
            MeshObject geometry, IReadOnlyList<Vector3> worldPositions, Matrix viewProjection, int width, int height, bool trackFaces = false, bool skipDepth = false)
        {
            ValidateWorldPositions(geometry, worldPositions);
            var bounds = Rectangle.Intersect(rectangle, new Rectangle(0, 0, Math.Max(0, width), Math.Max(0, height)));
            var vertices = ProjectVertices(worldPositions, viewProjection, width, height);
            if (skipDepth)
                return (vertices, LocalDepthBuffer.Empty, bounds);
            var depth = new LocalDepthBuffer(bounds.X, bounds.Y, bounds.Width, bounds.Height, trackFaces);
            Span<Vector4> polygon = stackalloc Vector4[12];
            Span<Vector4> clipped = stackalloc Vector4[12];
            for (var i = 0; i + 2 < geometry.IndexArray.Length; i += 3)
            {
                var first = vertices[geometry.IndexArray[i]];
                var second = vertices[geometry.IndexArray[i + 1]];
                var third = vertices[geometry.IndexArray[i + 2]];
                if (first.IsValid && second.IsValid && third.IsValid)
                    depth.RasterizeTriangle(first, second, third, i);
                else
                {
                    for (var corner = 0; corner < 3; corner++)
                        polygon[corner] = Vector4.Transform(new Vector4(worldPositions[geometry.IndexArray[i + corner]], 1), viewProjection);
                    var count = 3;
                    for (var plane = 0; plane < 6 && count > 0; plane++)
                    {
                        var outputCount = 0;
                        var previous = polygon[count - 1];
                        var previousDistance = ClipDistance(previous, plane);
                        for (var corner = 0; corner < count; corner++)
                        {
                            var current = polygon[corner];
                            var distance = ClipDistance(current, plane);
                            if ((distance >= 0) != (previousDistance >= 0))
                                clipped[outputCount++] = Vector4.Lerp(previous, current, previousDistance / (previousDistance - distance));
                            if (distance >= 0)
                                clipped[outputCount++] = current;
                            previous = current;
                            previousDistance = distance;
                        }
                        var swap = polygon;
                        polygon = clipped;
                        clipped = swap;
                        count = outputCount;
                    }
                    for (var corner = 1; corner + 1 < count; corner++)
                    {
                        var a = ProjectClip(polygon[0], width, height);
                        var b = ProjectClip(polygon[corner], width, height);
                        var c = ProjectClip(polygon[corner + 1], width, height);
                        if (a.IsValid && b.IsValid && c.IsValid)
                            depth.RasterizeTriangle(a, b, c, i);
                    }
                }
                if (!trackFaces)
                {
                    RasterizeSelectionEdge(first, second, depth, bounds);
                    RasterizeSelectionEdge(second, third, depth, bounds);
                    RasterizeSelectionEdge(third, first, depth, bounds);
                }
            }
            return (vertices, depth, bounds);
        }

        static ProjectedVertex ProjectClip(Vector4 clip, int width, int height) => clip.W <= 0 || !float.IsFinite(clip.W)
            ? default
            : new(new Vector2((clip.X / clip.W + 1) * 0.5f * width, (1 - clip.Y / clip.W) * 0.5f * height), clip.Z / clip.W);

        static bool ClipProjectedEdge(ref ProjectedVertex first, ref ProjectedVertex second,
            Vector3 worldFirst, Vector3 worldSecond, Matrix viewProjection, int width, int height)
        {
            if (first.IsValid && second.IsValid)
                return true;
            var a = Vector4.Transform(new Vector4(worldFirst, 1), viewProjection);
            var b = Vector4.Transform(new Vector4(worldSecond, 1), viewProjection);
            var start = 0f;
            var stop = 1f;
            for (var plane = 0; plane < 6; plane++)
            {
                var da = ClipDistance(a, plane);
                var db = ClipDistance(b, plane);
                if (da < 0 && db < 0)
                    return false;
                if (da < 0)
                    start = MathF.Max(start, da / (da - db));
                else if (db < 0)
                    stop = MathF.Min(stop, da / (da - db));
            }
            first = ProjectClip(Vector4.Lerp(a, b, start), width, height);
            second = ProjectClip(Vector4.Lerp(a, b, stop), width, height);
            return start <= stop && first.IsValid && second.IsValid;
        }

        static float ClipDistance(Vector4 point, int plane) => plane switch
        {
            0 => point.Z,
            1 => point.W - point.Z,
            2 => point.X + point.W,
            3 => point.W - point.X,
            4 => point.Y + point.W,
            _ => point.W - point.Y
        };

        static void RasterizeSelectionEdge(ProjectedVertex first, ProjectedVertex second, LocalDepthBuffer depth, Rectangle bounds)
        {
            if (!first.IsValid || !second.IsValid || bounds.IsEmpty)
                return;
            var start = 0f;
            var stop = 1f;
            var delta = second.ScreenPosition - first.ScreenPosition;
            if (!ClipInterval(first.ScreenPosition.X, delta.X, bounds.Left, bounds.Right, ref start, ref stop) ||
                !ClipInterval(first.ScreenPosition.Y, delta.Y, bounds.Top, bounds.Bottom, ref start, ref stop))
                return;
            // Include the displayed wire at silhouettes where a triangle covers no pixel center.
            var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y)) * (stop - start)));
            for (var step = 0; step <= steps; step++)
            {
                var amount = MathHelper.Lerp(start, stop, (float)step / steps);
                depth.WriteDepth(first.ScreenPosition + delta * amount, MathHelper.Lerp(first.Depth, second.Depth, amount));
            }
        }
    }
}
