using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Test.TestUtility;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Components.Selection;

public class VertexSelectionStateTests
{
    [Test]
    public void SetSelection_ReplacesAndNormalizesSelectionWithSingleNotification()
    {
        var state = CreateState([0.0f, 1.0f, 2.0f, 3.0f], 0.0f);
        var notificationCount = 0;
        state.SelectionChanged += (_, _) => notificationCount++;

        state.SetSelection([3, 1, 3]);

        Assert.Multiple(() =>
        {
            Assert.That(state.SelectedVertices, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(state.VertexWeights, Is.EqualTo(new[] { 0.0f, 1.0f, 0.0f, 1.0f }));
            Assert.That(notificationCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void UpdateWeights_UsesDistanceToNearestSelectedVertex()
    {
        var state = CreateState([0.0f, 3.0f, 9.0f, 10.0f, 15.0f], 4.0f);

        state.SetSelection([0, 3]);

        Assert.Multiple(() =>
        {
            Assert.That(state.VertexWeights[0], Is.EqualTo(1.0f));
            Assert.That(state.VertexWeights[1], Is.EqualTo(0.250f).Within(1.0f / 255.0f));
            Assert.That(state.VertexWeights[2], Is.EqualTo(0.750f).Within(1.0f / 255.0f));
            Assert.That(state.VertexWeights[3], Is.EqualTo(1.0f));
            Assert.That(state.VertexWeights[4], Is.Zero);
        });
    }

    [Test]
    public void Clear_ResetsProportionalWeights()
    {
        var state = CreateState([0.0f, 1.0f, 2.0f], 3.0f);
        state.SetSelection([0]);
        var notificationCount = 0;
        state.SelectionChanged += (_, _) => notificationCount++;

        state.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(state.SelectedVertices, Is.Empty);
            Assert.That(state.VertexWeights, Is.All.Zero);
            Assert.That(notificationCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void UpdateWeights_LimitsReplayPlanTo256WeightBands()
    {
        var positions = Enumerable.Range(0, 1025)
            .Select(index => (float)index)
            .ToArray();
        var state = CreateState(positions, 1024.0f);

        state.SetSelection([0]);
        var replayPlan = VertexTransformOperationApplier.CreateEmptyReplayPlan(state, null);

        Assert.That(replayPlan.WeightedMatrices.Count, Is.LessThanOrEqualTo(256));
    }

    private static VertexSelectionState CreateState(float[] xPositions, float falloffDistance)
    {
        var mesh = new MeshObject(new TestGraphicsCardGeometry(), string.Empty)
        {
            VertexArray = xPositions
                .Select(x => new VertexPositionNormalTextureCustom
                {
                    Position = new Vector4(x, 0.0f, 0.0f, 1.0f)
                })
                .ToArray(),
            IndexArray = []
        };
        return new VertexSelectionState(
            new TestSelectableNode { Geometry = mesh },
            falloffDistance);
    }

    private sealed class TestSelectableNode : SceneNode, ISelectable
    {
        public MeshObject Geometry { get; set; }
        public bool IsSelectable { get; set; } = true;

        public override ISceneNode CreateCopyInstance()
        {
            return new TestSelectableNode();
        }
    }
}
