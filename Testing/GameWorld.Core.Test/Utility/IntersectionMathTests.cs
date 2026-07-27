using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Test.TestUtility;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Utility;

public class IntersectionMathTests
{
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
