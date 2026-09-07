using System.Collections.Generic;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving.Lod.MeshDecimatorIntegration;

namespace GameWorld.Core.Commands.Object
{
    public class ReduceMeshCommand : IRedoableCommand
    {
        List<Rmv2MeshNode> _meshList;
        List<MeshObject> _originalGeometry = new List<MeshObject>();
        readonly List<MeshObject> _reducedGeometry = new List<MeshObject>();
        float _factor;
        SelectionManager _selectionManager;
        ISelectionState _oldState;

        public ReduceMeshCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public void Configure(List<Rmv2MeshNode> meshList, float factor)
        {
            _meshList = meshList;
            _factor = factor;
        }

        public string HintText { get => "Reduce mesh"; }
        public bool IsMutation { get => true; }


        public void Execute()
        {
            _oldState = _selectionManager.GetStateCopy();

            foreach (var meshNode in _meshList)
            {
                var originalMesh = meshNode.Geometry;

                var reducedMesh = DecimatorMeshOptimizer.GetReducedMeshCopy(originalMesh, _factor);
                _reducedGeometry.Add(reducedMesh);
                _originalGeometry.Add(originalMesh);
            }
            Redo();
        }

        public void Redo()
        {
            for (var i = 0; i < _meshList.Count; i++)
                _meshList[i].Geometry = _reducedGeometry[i];
            _selectionManager.SetState(_oldState.Clone());
        }

        public void Undo()
        {
            for (var i = 0; i < _meshList.Count; i++)
                _meshList[i].Geometry = _originalGeometry[i];

            _selectionManager.SetState(_oldState);
        }
    }
}
