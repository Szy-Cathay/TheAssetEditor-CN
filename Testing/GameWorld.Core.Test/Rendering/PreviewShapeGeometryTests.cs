using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Rendering;

public class PreviewShapeGeometryTests
{
    private static readonly Color Fill = new(36, 17, 10, 48);
    private static readonly Color Edge = new(220, 141, 85);

    [Test]
    public void SplashCone_HasSmoothSphericalSectorAndCenterArrow()
    {
        const int segments = 36;
        var shape = PreviewShapeGeometry.CreateSplashCone(
            Vector3.Zero,
            new Vector3(0, 0, 3),
            180,
            Fill,
            Edge,
            0.75f,
            segments);

        Assert.Multiple(() =>
        {
            Assert.That(shape.Triangles.Length, Is.GreaterThan(segments * 6));
            Assert.That(
                shape.Triangles.Any(vertex =>
                    Vector3.DistanceSquared(
                        vertex.Position,
                        new Vector3(0, 0, 3)) < 0.0001f),
                Is.True,
                "The original spherical-sector end point must remain on the surface.");
            Assert.That(
                shape.Triangles
                    .Select(vertex => vertex.Position.Z)
                    .Distinct()
                    .Count(),
                Is.GreaterThan(3),
                "A 180 degree sector must be a curved hemisphere, not a flat disk.");
            Assert.That(
                shape.Edges.Any(edge =>
                    Vector3.DistanceSquared(edge.P0, Vector3.Zero) < 0.0001f &&
                    Vector3.DistanceSquared(
                        edge.P1,
                        new Vector3(0, 0, 3)) < 0.0001f),
                Is.True,
                "The Start-End guide line must be preserved.");
            Assert.That(
                shape.Triangles.Select(vertex => vertex.Color).Distinct(),
                Is.EqualTo(new[] { Fill }));
            Assert.That(shape.Edges.All(edge => edge.Width == 0.75f), Is.True);
            Assert.That(AllPositionsAreFinite(shape), Is.True);
        });
    }

    [Test]
    public void SplashCone_Over180DegreesKeepsForwardEndAndContinuesBehindStart()
    {
        var shape = PreviewShapeGeometry.CreateSplashCone(
            Vector3.Zero,
            new Vector3(0, 0, 3),
            240,
            Fill,
            Edge,
            0.75f,
            36);
        var positions = shape.Triangles
            .Select(vertex => vertex.Position)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                positions.Any(position => position.Z > 2.99f),
                Is.True,
                "The End direction must not disappear when the angle exceeds 180 degrees.");
            Assert.That(
                positions.Any(position => position.Z < -1.45f),
                Is.True,
                "The sector must continue smoothly beyond the hemisphere.");
            Assert.That(AllPositionsAreFinite(shape), Is.True);
        });
    }

    [Test]
    public void SplashSphere_IsRoundAndUsesOnlyTwoGuideRings()
    {
        const int segments = 36;
        var shape = PreviewShapeGeometry.CreateSplashSphere(
            Vector3.Zero,
            2,
            Fill,
            Edge,
            0.75f,
            segments);

        var surfacePositions = shape.Triangles
            .Select(vertex => vertex.Position)
            .Where(position => position.LengthSquared() > 0.0001f)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(shape.Edges, Has.Length.EqualTo(segments * 2));
            Assert.That(surfacePositions, Is.Not.Empty);
            Assert.That(
                surfacePositions.Max(position =>
                    MathF.Abs(position.Length() - 2)),
                Is.LessThan(0.0001f));
            Assert.That(AllPositionsAreFinite(shape), Is.True);
        });
    }

    [Test]
    public void SplashCorridor_HasRoundEndsAndOnlyTwoLongEdges()
    {
        const int segments = 36;
        var shape = PreviewShapeGeometry.CreateSplashCorridor(
            Vector3.Zero,
            new Vector3(0, 0, 3),
            0.75f,
            Fill,
            Edge,
            0.75f,
            segments);

        Assert.Multiple(() =>
        {
            Assert.That(shape.Triangles, Has.Length.EqualTo(segments * 12));
            Assert.That(shape.Edges, Has.Length.EqualTo(segments * 2 + 7));
            Assert.That(
                shape.Edges.Any(edge =>
                    Vector3.DistanceSquared(edge.P0, Vector3.Zero) < 0.0001f &&
                    Vector3.DistanceSquared(
                        edge.P1,
                        new Vector3(0, 0, 3)) < 0.0001f),
                Is.True);
            Assert.That(AllPositionsAreFinite(shape), Is.True);
        });
    }

    [Test]
    public void OriginalPointMarkers_RetainCircleBoxAndLocatorSilhouettes()
    {
        var circle = PreviewShapeGeometry.CreateCircleMarker(
            Vector3.Zero,
            1,
            Fill,
            Edge,
            0.75f);
        var box = PreviewShapeGeometry.CreateBoxMarker(
            Vector3.Zero,
            1,
            Fill,
            Edge,
            0.75f);
        var locator = PreviewShapeGeometry.CreateLocatorMarker(
            Vector3.Zero,
            1,
            Fill,
            Edge,
            0.75f);

        Assert.Multiple(() =>
        {
            Assert.That(
                circle.Triangles.All(vertex =>
                    MathF.Abs(vertex.Position.Y) < 0.0001f),
                Is.True,
                "IMPACT_POS must retain its original horizontal circle.");
            Assert.That(box.Edges, Has.Length.EqualTo(12));
            Assert.That(box.Triangles, Has.Length.EqualTo(36));
            Assert.That(
                locator.Edges.Any(edge =>
                    edge.P0.X < -0.49f && edge.P1.X > 0.49f),
                Is.True,
                "The original locator must retain its X cross-axis.");
            Assert.That(
                new[] { circle, box, locator }.All(AllPositionsAreFinite),
                Is.True);
        });
    }

    [Test]
    public void SelectedOutline_DoesNotChangeSplashSurfaceOpacity()
    {
        var normal = PreviewShapeGeometry.CreateSplashSphere(
            Vector3.Zero,
            1,
            Fill,
            Edge,
            0.75f);
        var selected = PreviewShapeGeometry.CreateSplashSphere(
            Vector3.Zero,
            1,
            Fill,
            Color.White,
            1.75f);

        Assert.Multiple(() =>
        {
            Assert.That(
                selected.Triangles.Select(vertex => vertex.Color),
                Is.EqualTo(normal.Triangles.Select(vertex => vertex.Color)));
            Assert.That(selected.Edges.All(edge => edge.Width == 1.75f), Is.True);
            Assert.That(normal.Edges.All(edge => edge.Width == 0.75f), Is.True);
        });
    }

    [Test]
    public void OriginalPointMarkers_CreateFiniteTranslucentSurfaces()
    {
        PreviewShape[] shapes =
        [
            PreviewShapeGeometry.CreateCircleMarker(
                Vector3.Zero,
                0.3f,
                Fill,
                Edge,
                0.75f),
            PreviewShapeGeometry.CreateBoxMarker(
                Vector3.Zero,
                0.3f,
                Fill,
                Edge,
                0.75f),
            PreviewShapeGeometry.CreateLocatorMarker(
                Vector3.Zero,
                0.3f,
                Fill,
                Edge,
                0.75f)
        ];

        Assert.That(
            shapes.All(shape =>
                shape.Triangles.Length > 0 &&
                shape.Edges.Length > 0 &&
                AllPositionsAreFinite(shape)),
            Is.True);
    }

    private static bool AllPositionsAreFinite(PreviewShape shape) =>
        shape.Triangles.All(vertex => IsFinite(vertex.Position)) &&
        shape.Edges.All(edge => IsFinite(edge.P0) && IsFinite(edge.P1));

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
