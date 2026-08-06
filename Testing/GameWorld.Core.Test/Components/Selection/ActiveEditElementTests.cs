using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Test.TestUtility;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Components.Selection;

public class ActiveEditElementTests
{
    [Test]
    public void VertexSelection_TracksLastAddedElementAndClone()
    {
        var state = CreateVertexState();

        state.ModifySelection([0, 2], onlyRemove: false);
        var clone = (VertexSelectionState)state.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(state.ActiveVertex, Is.EqualTo(2));
            Assert.That(clone.ActiveVertex, Is.EqualTo(2));
        });

        state.ModifySelection([2], onlyRemove: true);

        Assert.That(state.ActiveVertex, Is.EqualTo(0));
    }

    [Test]
    public void EdgeSelection_TracksLastAddedElementAndClone()
    {
        var state = new EdgeSelectionState();

        state.ModifySelection(
            [(0, 1), (1, 2)],
            onlyRemove: false);
        var clone = (EdgeSelectionState)state.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(state.ActiveEdge, Is.EqualTo((1, 2)));
            Assert.That(clone.ActiveEdge, Is.EqualTo((1, 2)));
        });

        state.ModifySelection([(1, 2)], onlyRemove: true);

        Assert.That(state.ActiveEdge, Is.EqualTo((0, 1)));
    }

    [Test]
    public void FaceSelection_TracksLastAddedElementAndClone()
    {
        var state = new FaceSelectionState();

        state.ModifySelection([0, 6], onlyRemove: false);
        var clone = (FaceSelectionState)state.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(state.ActiveFace, Is.EqualTo(6));
            Assert.That(clone.ActiveFace, Is.EqualTo(6));
        });

        state.ModifySelection([6], onlyRemove: true);

        Assert.That(state.ActiveFace, Is.EqualTo(0));
    }

    [Test]
    public void Clear_RemovesAllActiveElements()
    {
        var vertices = CreateVertexState();
        vertices.ModifySelection([1], onlyRemove: false);
        var edges = new EdgeSelectionState();
        edges.ModifySelection([(0, 1)], onlyRemove: false);
        var faces = new FaceSelectionState();
        faces.ModifySelection([0], onlyRemove: false);

        vertices.Clear();
        edges.Clear();
        faces.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(vertices.ActiveVertex, Is.Null);
            Assert.That(edges.ActiveEdge, Is.Null);
            Assert.That(faces.ActiveFace, Is.Null);
        });
    }

    private static VertexSelectionState CreateVertexState()
    {
        var mesh = new MeshObject(
            new TestGraphicsCardGeometry(),
            string.Empty)
        {
            VertexArray =
            [
                CreateVertex(0),
                CreateVertex(1),
                CreateVertex(2)
            ],
            IndexArray = []
        };
        return new VertexSelectionState(
            new TestSelectableNode { Geometry = mesh },
            0);
    }

    private static VertexPositionNormalTextureCustom CreateVertex(
        float x)
    {
        return new VertexPositionNormalTextureCustom
        {
            Position = new Vector4(x, 0, 0, 1)
        };
    }

    private sealed class TestSelectableNode :
        SceneNode,
        ISelectable
    {
        public MeshObject Geometry { get; set; } = null!;
        public bool IsSelectable { get; set; } = true;

        public override ISceneNode CreateCopyInstance()
        {
            return new TestSelectableNode();
        }
    }
}
