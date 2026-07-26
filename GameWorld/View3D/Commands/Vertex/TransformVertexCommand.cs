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
        readonly List<VertexTransformOperation> _previewOperations = new();

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

        internal void SetPreviewOperations(IEnumerable<VertexTransformOperation> operations)
        {
            _previewOperations.Clear();
            _previewOperations.AddRange(operations);
        }

        private void ApplyTransform(bool inverse)
        {
            if (!VertexTransformOperationApplier.AreValid(
                _geometryList,
                _oldSelectionState,
                AffectedVertexIndices,
                FalloffWeights,
                _previewOperations))
            {
                return;
            }

            if (inverse)
            {
                for (var operationIndex = _previewOperations.Count - 1; operationIndex >= 0; operationIndex--)
                    ApplyOperation(_previewOperations[operationIndex], inverse: true);
            }
            else
            {
                for (var operationIndex = 0; operationIndex < _previewOperations.Count; operationIndex++)
                    ApplyOperation(_previewOperations[operationIndex], inverse: false);
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

        void ApplyOperation(VertexTransformOperation operation, bool inverse)
        {
            VertexTransformOperationApplier.TryApply(
                _geometryList,
                _oldSelectionState,
                AffectedVertexIndices,
                FalloffWeights,
                operation,
                inverse,
                out _);
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
