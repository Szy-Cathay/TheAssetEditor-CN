using GameWorld.Core.Commands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ChildEditors.PinTool.Commands
{
    public class PinMeshToVertexCommand : ICommand
    {
        ISelectionState _selectionOldState;
        SelectionManager _selectionManager;

        List<MeshObject> _originalGeos;
        List<Vector3> _originalPivots;

        List<Rmv2MeshNode> _meshesToPin;
        Rmv2MeshNode _source;
        int _vertexId;

        public void Configure(IEnumerable<Rmv2MeshNode> meshesToPin, Rmv2MeshNode source, int vertexId)
        {
            _meshesToPin = meshesToPin.ToList();
            _source = source;
            _vertexId = vertexId;
        }

        public string HintText => LocalizationManager.Instance.Get("Kitbash.CommandHint.PinMeshesToVertex");
        public bool IsMutation { get => true; }

        public PinMeshToVertexCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public void Execute()
        {
            var sourceVert = _source.Geometry.GetVertexExtented(_vertexId);
            var updatedGeometries = new List<MeshObject>(_meshesToPin.Count);
            _originalGeos = _meshesToPin.Select(x => x.Geometry).ToList();
            _originalPivots = _meshesToPin.Select(x => x.PivotPoint).ToList();
            _selectionOldState = _selectionManager.GetStateCopy();

            foreach (var currentMesh in _meshesToPin)
            {
                var updatedGeometry = currentMesh.Geometry.Clone();
                updatedGeometry.ChangeVertexType(_source.Geometry.VertexFormat, false);
                updatedGeometry.UpdateSkeletonName(_source.Geometry.SkeletonName);

                for (var i = 0; i < updatedGeometry.VertexCount(); i++)
                {
                    updatedGeometry.SetVertexBlendIndex(i, sourceVert.BlendIndices);
                    updatedGeometry.SetVertexWeights(i, sourceVert.BlendWeights);
                }

                updatedGeometry.RebuildVertexBuffer();
                updatedGeometries.Add(updatedGeometry);
            }

            for (var index = 0; index < _meshesToPin.Count; index++)
            {
                _meshesToPin[index].Geometry = updatedGeometries[index];
                _meshesToPin[index].PivotPoint = Vector3.Zero;
            }
        }

        public void Undo()
        {
            for (var i = 0; i < _meshesToPin.Count; i++)
            {
                _meshesToPin[i].Geometry = _originalGeos[i];
                _meshesToPin[i].PivotPoint = _originalPivots[i];
            }

            _selectionManager.SetState(_selectionOldState);
        }
    }
}
