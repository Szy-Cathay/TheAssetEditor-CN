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

        private void ApplyTransform(bool inverse)
        {
            Transform.Decompose(out var scale, out var rot, out var trans);

            for (var meshIndex = 0; meshIndex < _geometryList.Count; meshIndex++)
            {
                var geo = _geometryList[meshIndex];
                if (_oldSelectionState.Mode == GeometrySelectionMode.Vertex)
                {
                    var vState = _oldSelectionState as VertexSelectionState;
                    for (var vertIndex = 0; vertIndex < vState.VertexWeights.Count; vertIndex++)
                    {
                        if (vState.VertexWeights[vertIndex] != 0)
                        {
                            var weight = vState.VertexWeights[vertIndex];
                            var vertexScale = Vector3.Lerp(Vector3.One, scale, weight);
                            var vertRot = Quaternion.Slerp(Quaternion.Identity, rot, weight);
                            var vertTrnas = trans * weight;

                            var weightedTransform = Matrix.CreateScale(vertexScale) * Matrix.CreateFromQuaternion(vertRot) * Matrix.CreateTranslation(vertTrnas);
                            var replayTransform = inverse ? Matrix.Invert(weightedTransform) : weightedTransform;
                            var finalMatrix = Matrix.CreateTranslation(-PivotPoint) * replayTransform * Matrix.CreateTranslation(PivotPoint);
                            var normalMatrix = Matrix.Transpose(Matrix.Invert(finalMatrix));

                            geo.TransformVertex(vertIndex, finalMatrix, normalMatrix);
                        }
                    }
                }
                else if (AffectedVertexIndices != null && FalloffWeights != null && FalloffWeights.Count > 0)
                {
                    // Face/Edge mode with falloff: per-vertex weighted inverse
                    // Uses scale/rot/trans already decomposed at the top
                    foreach (var kvp in FalloffWeights)
                    {
                        var vertIdx = kvp.Key;
                        var weight = kvp.Value;
                        var vertexScale = Vector3.Lerp(Vector3.One, scale, weight);
                        var vertRot = Quaternion.Slerp(Quaternion.Identity, rot, weight);
                        var vertTrans = trans * weight;
                        var weightedTransform = Matrix.CreateScale(vertexScale) * Matrix.CreateFromQuaternion(vertRot) * Matrix.CreateTranslation(vertTrans);
                        var replayTransform = inverse ? Matrix.Invert(weightedTransform) : weightedTransform;
                        var finalMatrix = Matrix.CreateTranslation(-PivotPoint) * replayTransform * Matrix.CreateTranslation(PivotPoint);
                        var normalMatrix = Matrix.Transpose(Matrix.Invert(finalMatrix));
                        geo.TransformVertex(vertIdx, finalMatrix, normalMatrix);
                    }
                }
                else if (AffectedVertexIndices != null)
                {
                    // Face/Edge mode without falloff: only replay vertices that were actually transformed
                    var replayTransform = inverse ? Matrix.Invert(Transform) : Transform;
                    var replayMatrix = Matrix.CreateTranslation(-PivotPoint) * replayTransform * Matrix.CreateTranslation(PivotPoint);
                    var normalMatrix = Matrix.Transpose(Matrix.Invert(replayMatrix));
                    foreach (var vertIdx in AffectedVertexIndices)
                        geo.TransformVertex(vertIdx, replayMatrix, normalMatrix);
                }
                else
                {
                    // Object mode: replay all vertices
                    var replayTransform = inverse ? Matrix.Invert(Transform) : Transform;
                    var replayMatrix = Matrix.CreateTranslation(-PivotPoint) * replayTransform * Matrix.CreateTranslation(PivotPoint);
                    var normalMatrix = Matrix.Transpose(Matrix.Invert(replayMatrix));
                    for (var v = 0; v < geo.VertexCount(); v++)
                        geo.TransformVertex(v, replayMatrix, normalMatrix);

                    if (InvertWindingOrder)
                        ReverseWindingOrder(geo);
                }

                geo.RebuildVertexBuffer();
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
