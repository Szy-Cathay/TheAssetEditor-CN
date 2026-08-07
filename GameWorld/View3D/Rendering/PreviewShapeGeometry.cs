using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering;

public sealed record PreviewShape(
    VertexPositionColor[] Triangles,
    EdgeData[] Edges);

public static class PreviewShapeGeometry
{
    public const int SmoothSegments = 36;

    public static Color WithPremultipliedAlpha(
        Color color,
        byte alpha)
    {
        var factor = alpha / 255f;
        return new Color(
            (byte)MathF.Round(color.R * factor),
            (byte)MathF.Round(color.G * factor),
            (byte)MathF.Round(color.B * factor),
            alpha);
    }

    public static PreviewShape CreateSplashCone(
        Vector3 start,
        Vector3 end,
        float coneAngleDegrees,
        Color fill,
        Color edge,
        float edgeHalfWidth,
        int segments = SmoothSegments)
    {
        ValidateAxis(start, end);
        ValidateSegments(segments);
        if (!float.IsFinite(coneAngleDegrees) ||
            coneAngleDegrees <= 0 ||
            coneAngleDegrees > 360)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coneAngleDegrees));
        }

        if (MathF.Abs(coneAngleDegrees - 360f) < 0.001f)
        {
            return AddDirectionArrow(
                CreateSplashSphere(
                    start,
                    Vector3.Distance(start, end),
                    fill,
                    edge,
                    edgeHalfWidth,
                    segments),
                start,
                end,
                edge,
                edgeHalfWidth);
        }

        var direction = Vector3.Normalize(end - start);
        CreateBasis(direction, out var side, out var up);
        var radius = Vector3.Distance(start, end);
        var halfAngle = MathHelper.ToRadians(coneAngleDegrees / 2f);
        var polarSteps = Math.Max(
            2,
            (int)MathF.Ceiling(
                halfAngle / MathHelper.ToRadians(7.5f)));
        var rings = new Vector3[polarSteps + 1][];
        for (var polarIndex = 0;
            polarIndex <= polarSteps;
            polarIndex++)
        {
            var polarAngle = halfAngle * polarIndex / polarSteps;
            rings[polarIndex] = CreateSectorRing(
                start,
                direction,
                side,
                up,
                radius,
                polarAngle,
                segments);
        }

        var triangles = new List<VertexPositionColor>(
            polarSteps * segments * 6 + segments * 3);
        for (var polarIndex = 0;
            polarIndex < polarSteps;
            polarIndex++)
        {
            for (var segmentIndex = 0;
                segmentIndex < segments;
                segmentIndex++)
            {
                var next = (segmentIndex + 1) % segments;
                if (polarIndex == 0)
                {
                    AddTriangle(
                        triangles,
                        end,
                        rings[1][next],
                        rings[1][segmentIndex],
                        fill);
                }
                else
                {
                    AddQuad(
                        triangles,
                        rings[polarIndex][segmentIndex],
                        rings[polarIndex][next],
                        rings[polarIndex + 1][next],
                        rings[polarIndex + 1][segmentIndex],
                        fill);
                }
            }
        }

        var outerRing = rings[^1];
        for (var segmentIndex = 0;
            segmentIndex < segments;
            segmentIndex++)
        {
            var next = (segmentIndex + 1) % segments;
            AddTriangle(
                triangles,
                start,
                outerRing[segmentIndex],
                outerRing[next],
                fill);
        }

        var edges = new List<EdgeData>(
            segments + polarSteps * 2 + 5);
        AddRingEdges(edges, outerRing, edge, edgeHalfWidth);
        AddSectorMeridianEdges(
            edges,
            rings,
            0,
            edge,
            edgeHalfWidth);
        AddSectorMeridianEdges(
            edges,
            rings,
            segments / 2,
            edge,
            edgeHalfWidth);
        AddEdge(
            edges,
            start,
            end,
            edge,
            edgeHalfWidth);
        AddArrowHead(
            edges,
            start,
            end,
            edge,
            edgeHalfWidth);
        return new PreviewShape(
            triangles.ToArray(),
            edges.ToArray());
    }

    public static PreviewShape CreateSplashSphere(
        Vector3 center,
        float radius,
        Color fill,
        Color edge,
        float edgeHalfWidth,
        int segments = SmoothSegments)
    {
        ValidateRadius(radius);
        ValidateSegments(segments);

        var stacks = Math.Max(8, segments / 2);
        var triangles = new List<VertexPositionColor>(
            stacks * segments * 6);
        for (var stack = 0; stack < stacks; stack++)
        {
            var latitude0 = -MathHelper.PiOver2 +
                MathHelper.Pi * stack / stacks;
            var latitude1 = -MathHelper.PiOver2 +
                MathHelper.Pi * (stack + 1) / stacks;
            for (var slice = 0; slice < segments; slice++)
            {
                var longitude0 = MathHelper.TwoPi * slice / segments;
                var longitude1 = MathHelper.TwoPi * (slice + 1) / segments;
                var p00 = SpherePoint(center, radius, latitude0, longitude0);
                var p01 = SpherePoint(center, radius, latitude0, longitude1);
                var p10 = SpherePoint(center, radius, latitude1, longitude0);
                var p11 = SpherePoint(center, radius, latitude1, longitude1);
                AddTriangle(triangles, p00, p01, p11, fill);
                AddTriangle(triangles, p00, p11, p10, fill);
            }
        }

        var edges = new List<EdgeData>(segments * 2);
        AddRingEdges(
            edges,
            CreateRing(
                center,
                radius,
                Vector3.Right,
                Vector3.Forward,
                segments),
            edge,
            edgeHalfWidth);
        AddRingEdges(
            edges,
            CreateRing(
                center,
                radius,
                Vector3.Right,
                Vector3.Up,
                segments),
            edge,
            edgeHalfWidth);
        return new PreviewShape(triangles.ToArray(), edges.ToArray());
    }

    public static PreviewShape CreateSplashCorridor(
        Vector3 start,
        Vector3 end,
        float radius,
        Color fill,
        Color edge,
        float edgeHalfWidth,
        int segments = SmoothSegments)
    {
        ValidateAxis(start, end);
        ValidateRadius(radius);
        ValidateSegments(segments);

        var direction = Vector3.Normalize(end - start);
        CreateBasis(direction, out var side, out var up);
        var startRing = CreateRing(start, radius, side, up, segments);
        var endRing = CreateRing(end, radius, side, up, segments);
        var triangles = new List<VertexPositionColor>(segments * 12);
        var edges = new List<EdgeData>(segments * 2 + 2);

        for (var index = 0; index < segments; index++)
        {
            var next = (index + 1) % segments;
            AddQuad(
                triangles,
                startRing[index],
                startRing[next],
                endRing[next],
                endRing[index],
                fill);
            AddTriangle(
                triangles,
                start,
                startRing[next],
                startRing[index],
                fill);
            AddTriangle(
                triangles,
                end,
                endRing[index],
                endRing[next],
                fill);
        }

        AddRingEdges(edges, startRing, edge, edgeHalfWidth);
        AddRingEdges(edges, endRing, edge, edgeHalfWidth);
        AddEdge(edges, startRing[0], endRing[0], edge, edgeHalfWidth);
        AddEdge(
            edges,
            startRing[segments / 2],
            endRing[segments / 2],
            edge,
            edgeHalfWidth);
        AddEdge(edges, start, end, edge, edgeHalfWidth);
        AddArrowHead(
            edges,
            start,
            end,
            edge,
            edgeHalfWidth);
        return new PreviewShape(triangles.ToArray(), edges.ToArray());
    }

    public static PreviewShape CreateCircleMarker(
        Vector3 center,
        float radius,
        Color fill,
        Color edge,
        float edgeHalfWidth,
        int segments = SmoothSegments)
    {
        ValidateRadius(radius);
        ValidateSegments(segments);
        var ring = CreateRing(
            center,
            radius,
            Vector3.Right,
            Vector3.Forward,
            segments);
        var triangles = new List<VertexPositionColor>(segments * 3);
        var edges = new List<EdgeData>(segments);
        for (var index = 0; index < segments; index++)
        {
            var next = (index + 1) % segments;
            AddTriangle(
                triangles,
                center,
                ring[index],
                ring[next],
                fill);
        }
        AddRingEdges(edges, ring, edge, edgeHalfWidth);
        return new PreviewShape(triangles.ToArray(), edges.ToArray());
    }

    public static PreviewShape CreateBoxMarker(
        Vector3 center,
        float size,
        Color fill,
        Color edge,
        float edgeHalfWidth)
    {
        ValidateRadius(size);
        var half = size / 2;
        Vector3[] corners =
        [
            center + new Vector3(-half, -half, -half),
            center + new Vector3(half, -half, -half),
            center + new Vector3(half, half, -half),
            center + new Vector3(-half, half, -half),
            center + new Vector3(-half, -half, half),
            center + new Vector3(half, -half, half),
            center + new Vector3(half, half, half),
            center + new Vector3(-half, half, half)
        ];
        var triangles = new List<VertexPositionColor>(36);
        AddQuad(triangles, corners[0], corners[1], corners[2], corners[3], fill);
        AddQuad(triangles, corners[5], corners[4], corners[7], corners[6], fill);
        AddQuad(triangles, corners[4], corners[0], corners[3], corners[7], fill);
        AddQuad(triangles, corners[1], corners[5], corners[6], corners[2], fill);
        AddQuad(triangles, corners[3], corners[2], corners[6], corners[7], fill);
        AddQuad(triangles, corners[4], corners[5], corners[1], corners[0], fill);

        var edges = new List<EdgeData>(12);
        int[,] pairs =
        {
            { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
            { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };
        for (var index = 0; index < pairs.GetLength(0); index++)
        {
            AddEdge(
                edges,
                corners[pairs[index, 0]],
                corners[pairs[index, 1]],
                edge,
                edgeHalfWidth);
        }
        return new PreviewShape(triangles.ToArray(), edges.ToArray());
    }

    public static PreviewShape CreateLocatorMarker(
        Vector3 center,
        float size,
        Color fill,
        Color edge,
        float edgeHalfWidth)
    {
        ValidateRadius(size);
        var half = size / 2;
        var panelHalfWidth = size * 0.08f;
        var triangles = new List<VertexPositionColor>(18);
        var edges = new List<EdgeData>(15);
        AddLocatorPanel(
            triangles,
            edges,
            center,
            Vector3.Right,
            Vector3.Up,
            half,
            panelHalfWidth,
            fill,
            edge,
            edgeHalfWidth);
        AddLocatorPanel(
            triangles,
            edges,
            center,
            Vector3.Up,
            Vector3.Forward,
            half,
            panelHalfWidth,
            fill,
            edge,
            edgeHalfWidth);
        AddLocatorPanel(
            triangles,
            edges,
            center,
            Vector3.Forward,
            Vector3.Right,
            half,
            panelHalfWidth,
            fill,
            edge,
            edgeHalfWidth);
        return new PreviewShape(triangles.ToArray(), edges.ToArray());
    }

    private static Vector3 SpherePoint(
        Vector3 center,
        float radius,
        float latitude,
        float longitude) =>
        center + radius * new Vector3(
            MathF.Cos(latitude) * MathF.Cos(longitude),
            MathF.Sin(latitude),
            MathF.Cos(latitude) * MathF.Sin(longitude));

    private static Vector3[] CreateSectorRing(
        Vector3 center,
        Vector3 direction,
        Vector3 side,
        Vector3 up,
        float radius,
        float polarAngle,
        int segments)
    {
        var ring = new Vector3[segments];
        var axial = direction * MathF.Cos(polarAngle);
        var radialScale = MathF.Sin(polarAngle);
        for (var index = 0; index < segments; index++)
        {
            var azimuth = MathHelper.TwoPi * index / segments;
            var radial = side * MathF.Cos(azimuth) +
                up * MathF.Sin(azimuth);
            ring[index] = center + radius *
                (axial + radial * radialScale);
        }
        return ring;
    }

    private static void AddSectorMeridianEdges(
        List<EdgeData> edges,
        IReadOnlyList<Vector3[]> rings,
        int segmentIndex,
        Color color,
        float width)
    {
        for (var polarIndex = 0;
            polarIndex < rings.Count - 1;
            polarIndex++)
        {
            AddEdge(
                edges,
                rings[polarIndex][segmentIndex],
                rings[polarIndex + 1][segmentIndex],
                color,
                width);
        }
    }

    private static PreviewShape AddDirectionArrow(
        PreviewShape shape,
        Vector3 start,
        Vector3 end,
        Color color,
        float width)
    {
        var edges = shape.Edges.ToList();
        AddEdge(edges, start, end, color, width);
        AddArrowHead(edges, start, end, color, width);
        return new PreviewShape(shape.Triangles, edges.ToArray());
    }

    private static void AddArrowHead(
        List<EdgeData> edges,
        Vector3 start,
        Vector3 end,
        Color color,
        float width)
    {
        var direction = Vector3.Normalize(end - start);
        CreateBasis(direction, out var side, out var up);
        var length = Vector3.Distance(start, end);
        var arrowLength = MathF.Min(length * 0.18f, 0.35f);
        var arrowRadius = arrowLength * 0.45f;
        var baseCenter = end - direction * arrowLength;
        AddEdge(edges, end, baseCenter + side * arrowRadius, color, width);
        AddEdge(edges, end, baseCenter - side * arrowRadius, color, width);
        AddEdge(edges, end, baseCenter + up * arrowRadius, color, width);
        AddEdge(edges, end, baseCenter - up * arrowRadius, color, width);
    }

    private static void AddLocatorPanel(
        List<VertexPositionColor> triangles,
        List<EdgeData> edges,
        Vector3 center,
        Vector3 axis,
        Vector3 widthAxis,
        float halfLength,
        float halfWidth,
        Color fill,
        Color edge,
        float edgeHalfWidth)
    {
        var a = center - axis * halfLength - widthAxis * halfWidth;
        var b = center + axis * halfLength - widthAxis * halfWidth;
        var c = center + axis * halfLength + widthAxis * halfWidth;
        var d = center - axis * halfLength + widthAxis * halfWidth;
        AddQuad(triangles, a, b, c, d, fill);
        AddEdge(edges, a, b, edge, edgeHalfWidth);
        AddEdge(edges, b, c, edge, edgeHalfWidth);
        AddEdge(edges, c, d, edge, edgeHalfWidth);
        AddEdge(edges, d, a, edge, edgeHalfWidth);
        AddEdge(
            edges,
            center - axis * halfLength,
            center + axis * halfLength,
            edge,
            edgeHalfWidth);
    }

    private static Vector3[] CreateRing(
        Vector3 center,
        float radius,
        Vector3 side,
        Vector3 up,
        int segments)
    {
        var ring = new Vector3[segments];
        for (var index = 0; index < segments; index++)
        {
            var angle = MathHelper.TwoPi * index / segments;
            ring[index] = center +
                side * (MathF.Cos(angle) * radius) +
                up * (MathF.Sin(angle) * radius);
        }
        return ring;
    }

    private static void CreateBasis(
        Vector3 direction,
        out Vector3 side,
        out Vector3 up)
    {
        var reference = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.92f
            ? Vector3.Right
            : Vector3.Up;
        side = Vector3.Normalize(Vector3.Cross(direction, reference));
        up = Vector3.Normalize(Vector3.Cross(side, direction));
    }

    private static void AddRingEdges(
        List<EdgeData> edges,
        IReadOnlyList<Vector3> ring,
        Color color,
        float width)
    {
        for (var index = 0; index < ring.Count; index++)
        {
            AddEdge(
                edges,
                ring[index],
                ring[(index + 1) % ring.Count],
                color,
                width);
        }
    }

    private static void AddQuad(
        List<VertexPositionColor> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Color color)
    {
        AddTriangle(triangles, a, b, c, color);
        AddTriangle(triangles, a, c, d, color);
    }

    private static void AddTriangle(
        List<VertexPositionColor> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color color)
    {
        triangles.Add(new VertexPositionColor(a, color));
        triangles.Add(new VertexPositionColor(b, color));
        triangles.Add(new VertexPositionColor(c, color));
    }

    private static void AddEdge(
        List<EdgeData> edges,
        Vector3 start,
        Vector3 end,
        Color color,
        float width)
    {
        edges.Add(new EdgeData
        {
            P0 = start,
            P1 = end,
            C0 = color.ToVector3(),
            C1 = color.ToVector3(),
            Width = width
        });
    }

    private static void ValidateAxis(Vector3 start, Vector3 end)
    {
        if (!IsFinite(start) ||
            !IsFinite(end) ||
            Vector3.DistanceSquared(start, end) < 0.000001f)
        {
            throw new ArgumentException("Shape axis must be finite and non-zero.");
        }
    }

    private static void ValidateRadius(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
    }

    private static void ValidateSegments(int segments)
    {
        if (segments < 8)
            throw new ArgumentOutOfRangeException(nameof(segments));
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
