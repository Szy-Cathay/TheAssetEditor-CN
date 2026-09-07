using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Test.TestUtility;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Utility;

public class IntersectionMathTests
{
    [Test]
    public void CircleStrokeCoversFastMotionWithoutSelectingOutsideItsRadius()
    {
        Vector3[] positions = [new(-0.6f, 0, 0.5f), new(0, 0, 0.5f), new(0.6f, 0, 0.5f), new(0, 0.6f, 0.5f)];
        var mesh = CreateMesh(positions, [0, 1, 3, 1, 2, 3]);
        var result = IntersectionMath.IntersectVisibleVertices(new Rectangle(10, 10, 80, 80), mesh,
            positions, Matrix.Identity, 100, 100, true, new Vector2(80, 50), 10, new Vector2(20, 50));
        Assert.That(result, Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void XrayPointCanPickAnOccludedVertex()
    {
        Vector3[] positions = [new(-0.6f, -0.6f, 0.2f), new(0.6f, -0.6f, 0.2f), new(0, 0.6f, 0.2f),
            new(0, 0, 0.8f), new(0.3f, -0.2f, 0.8f), new(-0.3f, -0.2f, 0.8f)];
        var mesh = CreateMesh(positions, [0, 1, 2, 3, 4, 5]);
        Assert.That(IntersectionMath.IntersectVertex(new Vector2(50), mesh, positions, Matrix.Identity, 100, 100,
            out _, includeOccluded: false), Is.Null);
        Assert.That(IntersectionMath.IntersectVertex(new Vector2(50), mesh, positions, Matrix.Identity, 100, 100,
            out var selected, includeOccluded: true), Is.Not.Null);
        Assert.That(selected, Is.EqualTo(3));
    }

    [Test]
    public void IntersectVertex_OccludedVertexDoesNotBeatVisibleVertex()
    {
        var result = IntersectionMath.IntersectVertex(
            new Vector2(50.0f, 50.0f),
            CreateMesh(
                [
                    new Vector3(-0.1f, 0.0f, 0.2f),
                    new Vector3(0.5f, -0.5f, 0.2f),
                    new Vector3(0.5f, 0.5f, 0.2f),
                    new Vector3(0.0f, 0.0f, 0.8f),
                    new Vector3(-0.5f, -0.5f, 0.8f),
                    new Vector3(-0.5f, 0.5f, 0.8f)
                ],
                [0, 1, 2, 3, 4, 5]),
            Matrix.Identity,
            Matrix.Identity,
            100.0f,
            100.0f,
            out var selectedVertex);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(5.0f).Within(0.001f));
            Assert.That(selectedVertex, Is.EqualTo(0));
        });
    }

    [Test]
    public void IntersectVertex_SelectedVertexUsesBlenderSelectionBias()
    {
        var result = IntersectionMath.IntersectVertex(
            new Vector2(50.0f, 50.0f),
            CreateMesh(
                [
                    new Vector3(-0.04f, 0.0f, 0.2f),
                    new Vector3(0.1f, 0.0f, 0.2f),
                    new Vector3(0.0f, 0.5f, 0.2f)
                ],
                [0, 1, 2]),
            Matrix.Identity,
            Matrix.Identity,
            100.0f,
            100.0f,
            out var selectedVertex,
            new HashSet<int> { 0 });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(5.0f).Within(0.001f));
            Assert.That(selectedVertex, Is.EqualTo(1));
        });
    }

    [Test]
    public void IntersectEdge_ClickWithinPixelThreshold_SelectsClosestEdge()
    {
        var result = IntersectionMath.IntersectEdge(
            new Vector2(50.0f, 56.0f),
            CreateTriangle(),
            Matrix.Identity,
            Matrix.Identity,
            100.0f,
            100.0f,
            out var selectedEdge);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(6.0f).Within(0.001f));
            Assert.That(selectedEdge, Is.EqualTo((0, 1)));
        });
    }

    [Test]
    public void IntersectEdge_OccludedEdgeDoesNotBeatVisibleEdge()
    {
        var result = IntersectionMath.IntersectEdge(
            new Vector2(50.0f, 50.0f),
            CreateMesh(
                [
                    new Vector3(-0.5f, 0.1f, 0.2f),
                    new Vector3(0.5f, 0.1f, 0.2f),
                    new Vector3(0.0f, -0.6f, 0.2f),
                    new Vector3(-0.5f, 0.0f, 0.8f),
                    new Vector3(0.5f, 0.0f, 0.8f),
                    new Vector3(0.0f, -0.6f, 0.8f)
                ],
                [0, 1, 2, 3, 4, 5]),
            Matrix.Identity,
            Matrix.Identity,
            100.0f,
            100.0f,
            out var selectedEdge);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(5.0f).Within(0.001f));
            Assert.That(selectedEdge, Is.EqualTo((0, 1)));
        });
    }

    [Test]
    public void IntersectEdge_UsesBlenderManhattanDistance()
    {
        var result = IntersectionMath.IntersectEdge(
            new Vector2(50.0f, 50.0f),
            CreateMesh(
                [
                    new Vector3(0.12f, -0.12f, 0.2f),
                    new Vector3(0.4f, -0.12f, 0.2f),
                    new Vector3(0.4f, -0.4f, 0.2f),
                    new Vector3(-0.2f, 0.2f, 0.2f),
                    new Vector3(0.2f, 0.2f, 0.2f),
                    new Vector3(-0.2f, 0.4f, 0.2f)
                ],
                [0, 1, 2, 3, 4, 5]),
            Matrix.Identity,
            Matrix.Identity,
            100.0f,
            100.0f,
            out var selectedEdge);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(10.0f).Within(0.001f));
            Assert.That(selectedEdge, Is.EqualTo((3, 4)));
        });
    }

    [Test]
    public void IntersectEdge_SelectedEdgeUsesBlenderSelectionBias()
    {
        var result = IntersectionMath.IntersectEdge(
            new Vector2(50.0f, 50.0f),
            CreateMesh(
                [
                    new Vector3(-0.2f, -0.04f, 0.2f),
                    new Vector3(0.2f, -0.04f, 0.2f),
                    new Vector3(0.0f, -0.4f, 0.2f),
                    new Vector3(-0.2f, 0.1f, 0.2f),
                    new Vector3(0.2f, 0.1f, 0.2f),
                    new Vector3(0.0f, 0.4f, 0.2f)
                ],
                [0, 1, 2, 3, 4, 5]),
            Matrix.Identity,
            Matrix.Identity,
            100.0f,
            100.0f,
            out var selectedEdge,
            new HashSet<(int, int)> { (0, 1) });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(5.0f).Within(0.001f));
            Assert.That(selectedEdge, Is.EqualTo((3, 4)));
        });
    }

    [Test]
    public void IntersectEdge_ClickOutsidePixelThreshold_DoesNotSelectEdge()
    {
        var result = IntersectionMath.IntersectEdge(
            new Vector2(50.0f, 80.0f),
            CreateTriangle(),
            Matrix.Identity,
            Matrix.Identity,
            100.0f,
            100.0f,
            out var selectedEdge);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(selectedEdge, Is.EqualTo((-1, -1)));
        });
    }

    [Test]
    public void IntersectObject_EvaluatedWorldPositionsPickVisiblePoseInsteadOfBindPose()
    {
        var mesh = CreateTriangle();
        var evaluatedPositions = mesh
            .GetVertexList()
            .Select(position => position + new Vector3(2, 0, 0))
            .ToArray();

        var visiblePoseHit = IntersectionMath.IntersectObject(
            new Ray(
                new Vector3(2, 0.2f, 1),
                -Vector3.UnitZ),
            mesh,
            evaluatedPositions);
        var bindPoseHit = IntersectionMath.IntersectObject(
            new Ray(
                new Vector3(0, 0.2f, 1),
                -Vector3.UnitZ),
            mesh,
            evaluatedPositions);

        Assert.Multiple(() =>
        {
            Assert.That(visiblePoseHit, Is.Not.Null);
            Assert.That(bindPoseHit, Is.Null);
        });
    }

    [Test]
    public void IntersectVertex_EvaluatedWorldPositionsPickVisibleVertex()
    {
        var mesh = CreateTriangle();
        var evaluatedPositions = mesh
            .GetVertexList()
            .Select(position => position + new Vector3(0.8f, 0, 0))
            .ToArray();

        var result = IntersectionMath.IntersectVertex(
            new Vector2(65, 50),
            mesh,
            evaluatedPositions,
            Matrix.Identity,
            100,
            100,
            out var selectedVertex);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(0).Within(0.001f));
            Assert.That(selectedVertex, Is.EqualTo(0));
        });
    }

    [Test]
    public void IntersectEdge_EvaluatedWorldPositionsPickVisibleEdge()
    {
        var mesh = CreateTriangle();
        var evaluatedPositions = mesh
            .GetVertexList()
            .Select(position => position + new Vector3(0.8f, 0, 0))
            .ToArray();

        var result = IntersectionMath.IntersectEdge(
            new Vector2(80, 50),
            mesh,
            evaluatedPositions,
            Matrix.Identity,
            100,
            100,
            out var selectedEdge);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(0).Within(0.001f));
            Assert.That(selectedEdge, Is.EqualTo((0, 1)));
        });
    }

    [Test]
    public void VisibleRectangle_FaceInteriorDoesNotRequireVertexInsideBox()
    {
        Vector3[] positions = [new(-0.9f, -0.9f, 0.2f), new(0.9f, -0.9f, 0.2f), new(0, 0.9f, 0.2f)];
        var mesh = CreateMesh(positions, [0, 1, 2]);
        Assert.That(IntersectionMath.IntersectVisibleFaces(new Rectangle(45, 45, 10, 10), mesh,
            positions, Matrix.Identity, 100, 100), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void VisibleRectangle_EdgesPreferFullyContainedThenFallBackToCrossing()
    {
        Vector3[] positions = [new(-0.8f, 0, 0.2f), new(0.8f, 0, 0.2f), new(0, 0.8f, 0.2f)];
        var mesh = CreateMesh(positions, [0, 1, 2]);
        Assert.That(IntersectionMath.IntersectVisibleEdges(new Rectangle(5, 45, 90, 10), mesh,
            positions, Matrix.Identity, 100, 100), Is.EqualTo(new[] { (0, 1) }));
        Assert.That(IntersectionMath.IntersectVisibleEdges(new Rectangle(45, 45, 10, 10), mesh,
            positions, Matrix.Identity, 100, 100), Is.EqualTo(new[] { (0, 1) }));
    }

    [Test]
    public void VisibleRectangle_ClippedForegroundFaceStillOccludesBackground()
    {
        Vector3[] positions =
        [
            new(-0.9f, -0.9f, 0.2f), new(0.9f, -0.9f, 0.2f), new(0, 0.9f, -0.1f),
            new(-0.1f, -0.1f, 0.8f), new(0.1f, -0.1f, 0.8f), new(0, 0.1f, 0.8f)
        ];
        var mesh = CreateMesh(positions, [0, 1, 2, 3, 4, 5]);
        var rectangle = new Rectangle(40, 40, 20, 20);
        Assert.That(IntersectionMath.IntersectVisibleFaces(rectangle, mesh, positions, Matrix.Identity, 100, 100), Is.EqualTo(new[] { 0 }));
        Assert.That(IntersectionMath.IntersectVisibleVertices(rectangle, mesh, positions, Matrix.Identity, 100, 100), Is.Empty);
        Assert.That(IntersectionMath.IntersectVisibleEdges(rectangle, mesh, positions, Matrix.Identity, 100, 100), Is.Empty);
    }

    [Test]
    public void VisibleRectangle_EdgeCrossingNearPlaneRetainsVisiblePortion()
    {
        Vector3[] positions = [new(-0.8f, 0, 0.2f), new(0.8f, 0, -0.2f), new(0, 0.8f, -0.2f)];
        var mesh = CreateMesh(positions, [0, 1, 2]);
        Assert.That(IntersectionMath.IntersectVisibleEdges(new Rectangle(15, 48, 10, 4), mesh,
            positions, Matrix.Identity, 100, 100), Is.EqualTo(new[] { (0, 1) }));
    }

    private static MeshObject CreateTriangle()
    {
        return CreateMesh(
            [
                new Vector3(-0.5f, 0.0f, 0.0f),
                new Vector3(0.5f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.8f, 0.0f)
            ],
            [0, 1, 2]);
    }

    private static MeshObject CreateMesh(Vector3[] positions, ushort[] indices)
    {
        return new MeshObject(new TestGraphicsCardGeometry(), string.Empty)
        {
            VertexArray = positions.Select(CreateVertex).ToArray(),
            IndexArray = indices
        };
    }

    private static VertexPositionNormalTextureCustom CreateVertex(Vector3 position)
    {
        return new VertexPositionNormalTextureCustom
        {
            Position = new Vector4(position, 1.0f)
        };
    }
}
