using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Test.TestUtility;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Components.Selection;

public class EdgeOverlayDataBuilderTests
{
    [Test]
    public void Fill_TransformsEndpointsAndInterpolatesSelectionColors()
    {
        var mesh = CreateMesh();
        var destination = new EdgeData[2];
        var edges = new[] { (0, 1), (1, 2) };
        var weights = new[] { 0.0f, 1.0f, 0.5f };
        var modelMatrix = Matrix.CreateTranslation(10.0f, 20.0f, 30.0f);

        EdgeOverlayDataBuilder.Fill(destination, mesh, modelMatrix, edges, weights);

        Assert.Multiple(() =>
        {
            Assert.That(destination[0].P0, Is.EqualTo(new Vector3(11.0f, 22.0f, 33.0f)));
            Assert.That(destination[0].P1, Is.EqualTo(new Vector3(9.0f, 20.0f, 32.0f)));
            Assert.That(destination[0].C0, Is.EqualTo(new Vector3(0.15f, 0.15f, 0.15f)));
            Assert.That(destination[0].C1, Is.EqualTo(new Vector3(1.0f, 0.47f, 0.0f)));
            Assert.That(destination[0].Width, Is.Zero);

            Assert.That(destination[1].P0, Is.EqualTo(new Vector3(9.0f, 20.0f, 32.0f)));
            Assert.That(destination[1].P1, Is.EqualTo(new Vector3(10.0f, 19.0f, 34.0f)));
            Assert.That(
                destination[1].C1,
                Is.EqualTo(Vector3.Lerp(
                    new Vector3(0.15f, 0.15f, 0.15f),
                    new Vector3(1.0f, 0.47f, 0.0f),
                    0.5f)));
            Assert.That(destination[1].Width, Is.Zero);
        });
    }

    [Test]
    public void Fill_EmptyInput_IsValid()
    {
        Assert.That(
            () => EdgeOverlayDataBuilder.Fill(
                Span<EdgeData>.Empty,
                CreateMesh(),
                Matrix.Identity,
                Array.Empty<(int, int)>(),
                new[] { 0.0f, 0.0f, 0.0f }),
            Throws.Nothing);
    }

    [Test]
    public void FillSelected_UsesSelectionColorAndWiderScreenSpaceQuad()
    {
        var destination = new EdgeData[1];

        EdgeOverlayDataBuilder.FillSelected(
            destination,
            CreateMesh(),
            Matrix.Identity,
            new[] { (0, 1) });

        Assert.Multiple(() =>
        {
            Assert.That(destination[0].P0, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(destination[0].P1, Is.EqualTo(new Vector3(-1.0f, 0.0f, 2.0f)));
            Assert.That(destination[0].C0, Is.EqualTo(new Vector3(1.0f, 0.47f, 0.0f)));
            Assert.That(destination[0].C1, Is.EqualTo(new Vector3(1.0f, 0.47f, 0.0f)));
            Assert.That(destination[0].Width, Is.EqualTo(1.5f));
        });
    }

    [Test]
    public void Fill_EvaluatedWorldPositionsUseCurrentPoseEndpoints()
    {
        var destination = new EdgeData[1];
        var worldPositions = new[]
        {
            new Vector3(20, 21, 22),
            new Vector3(30, 31, 32),
            new Vector3(40, 41, 42)
        };

        EdgeOverlayDataBuilder.Fill(
            destination,
            worldPositions,
            new[] { (0, 1) },
            new[] { 0.0f, 1.0f, 0.0f });

        Assert.Multiple(() =>
        {
            Assert.That(
                destination[0].P0,
                Is.EqualTo(worldPositions[0]));
            Assert.That(
                destination[0].P1,
                Is.EqualTo(worldPositions[1]));
        });
    }

    [Test]
    public void FillSelected_EvaluatedWorldPositionsUseCurrentPoseEndpoints()
    {
        var destination = new EdgeData[1];
        var worldPositions = new[]
        {
            new Vector3(20, 21, 22),
            new Vector3(30, 31, 32),
            new Vector3(40, 41, 42)
        };

        EdgeOverlayDataBuilder.FillSelected(
            destination,
            worldPositions,
            new[] { (1, 2) });

        Assert.Multiple(() =>
        {
            Assert.That(
                destination[0].P0,
                Is.EqualTo(worldPositions[1]));
            Assert.That(
                destination[0].P1,
                Is.EqualTo(worldPositions[2]));
        });
    }

    [Test]
    public void Fill_DestinationLengthDoesNotMatchEdges_ThrowsArgumentException()
    {
        Assert.That(
            () => EdgeOverlayDataBuilder.Fill(
                new EdgeData[2],
                CreateMesh(),
                Matrix.Identity,
                new[] { (0, 1) },
                new[] { 0.0f, 0.0f, 0.0f }),
            Throws.ArgumentException);
    }

    private static MeshObject CreateMesh()
    {
        return new MeshObject(new TestGraphicsCardGeometry(), string.Empty)
        {
            VertexArray = new[]
            {
                CreateVertex(new Vector3(1.0f, 2.0f, 3.0f)),
                CreateVertex(new Vector3(-1.0f, 0.0f, 2.0f)),
                CreateVertex(new Vector3(0.0f, -1.0f, 4.0f))
            },
            IndexArray = new ushort[] { 0, 1, 2 }
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
