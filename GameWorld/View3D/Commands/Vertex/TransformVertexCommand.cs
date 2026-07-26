using GameWorld.Core.Commands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace GameWorld.Core.Commands.Vertex
{

    public class TransformVertexCommand : IRedoableCommand
    {
        List<MeshObject> _geometryList;
        public Vector3 PivotPoint;
        public Matrix Transform { get; set; }
        public bool InvertWindingOrder { get; set; } = false;

        // Affected vertex indices for Face/Edge mode undo (null = all vertices, used by Object mode)
        public HashSet<int> AffectedVertexIndices { get; set; } = null;
        // Falloff weights for proportional editing undo (null = no falloff)
        public Dictionary<int, float> FalloffWeights { get; set; } = null;

        SelectionManager _selectionManager;
        ISelectionState _oldSelectionState;
        VertexTransformReplayPlan? _replayPlan;

        public void Configure(List<MeshObject> geometryList, Vector3 pivotPoint)
        {
            _geometryList = geometryList;
            PivotPoint = pivotPoint;
            _oldSelectionState = _selectionManager.GetStateCopy();
        }

        public string HintText { get => "Transform"; }
        public bool IsMutation { get => true; }


        public TransformVertexCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public void Execute()
        {
            // Nothing to do, vertexes already updated
        }

        public void Undo()
        {
            ApplyTransform(inverse: true);
            _selectionManager.SetState(_oldSelectionState);
        }

        public void Redo()
        {
            ApplyTransform(inverse: false);
            _selectionManager.SetState(_oldSelectionState);
        }

        internal void SetReplayPlan(VertexTransformReplayPlan replayPlan)
        {
            _replayPlan = replayPlan;
        }

        private void ApplyTransform(bool inverse)
        {
            if (_replayPlan == null ||
                !VertexTransformOperationApplier.TryApplyReplayPlan(
                _geometryList,
                _oldSelectionState,
                AffectedVertexIndices,
                FalloffWeights,
                _replayPlan,
                inverse,
                out _))
            {
                return;
            }

            var reverseWinding = InvertWindingOrder &&
                                 _oldSelectionState.Mode != GeometrySelectionMode.Vertex &&
                                 AffectedVertexIndices == null;
            foreach (var geometry in _geometryList)
            {
                if (reverseWinding)
                    ReverseWindingOrder(geometry);
                geometry.RebuildVertexBuffer();
            }
        }

        internal static void ReverseWindingOrder(MeshObject geometry)
        {
            var indexes = geometry.GetIndexBuffer();
            for (var index = 0; index < indexes.Count; index += 3)
            {
                var temp = indexes[index + 2];
                indexes[index + 2] = indexes[index];
                indexes[index] = temp;
            }
            geometry.SetIndexBuffer(indexes);
        }

    }
}
