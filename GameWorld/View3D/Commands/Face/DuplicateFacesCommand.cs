using System.Collections.Generic;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;

namespace GameWorld.Core.Commands.Face
{
    public class DuplicateFacesCommand : IRedoableCommand
    {
        readonly SelectionManager _selectionManager;

        // Undo variables
        ISelectionState _oldState;
        ISelectable _newObject;
        MeshObject _originalGeometry;
        MeshObject _remainderGeometry;

        // Input variables
        List<int> _facesToDelete;
        ISelectable _inputNode;
        bool _deleteOriginal;

        public string HintText { get => "Duplicate Faces"; }
        public bool IsMutation { get => true; }

        public DuplicateFacesCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public void Configure(ISelectable geoObject, List<int> facesToDelete, bool deleteOriginal)
        {
            _facesToDelete = facesToDelete;
            _inputNode = geoObject;
            _deleteOriginal = deleteOriginal;
        }



        public void Execute()
        {
            _oldState = _selectionManager.GetStateCopy();

            // Clone the object
            _newObject = SceneNodeHelper.CloneNode(_inputNode);

            if (!_deleteOriginal)
                _newObject.Name += "_copy";

            var selectedFaceIndecies = new List<ushort>();
            var indexBuffer = _newObject.Geometry.GetIndexBuffer();
            foreach (var face in _facesToDelete)
            {
                selectedFaceIndecies.Add(indexBuffer[face]);
                selectedFaceIndecies.Add(indexBuffer[face + 1]);
                selectedFaceIndecies.Add(indexBuffer[face + 2]);
            }

            _newObject.Geometry.RemoveUnusedVertexes(selectedFaceIndecies.ToArray());

            if (_deleteOriginal)
            {
                _originalGeometry = _inputNode.Geometry;
                var selectedFaces = _facesToDelete.ToHashSet();
                var remainingIndices = new List<ushort>();
                for (var face = 0; face < indexBuffer.Count; face += 3)
                {
                    if (!selectedFaces.Contains(face))
                        remainingIndices.AddRange(indexBuffer.GetRange(face, 3));
                }
                _remainderGeometry = _originalGeometry.CloneSubMesh(remainingIndices.ToArray());
            }

            Redo();
        }

        public void Redo()
        {
            _newObject.Parent.AddObject(_newObject);
            if (_deleteOriginal)
            {
                if (_remainderGeometry.GetIndexCount() == 0)
                    _inputNode.Parent.RemoveObject(_inputNode);
                else
                    _inputNode.Geometry = _remainderGeometry;
            }

            // Object state
            var objectState = new ObjectSelectionState();
            objectState.ModifySelectionSingleObject(_newObject, false);
            _selectionManager.SetState(objectState);
        }

        public void Undo()
        {
            _newObject.Parent.RemoveObject(_newObject);

            if (_deleteOriginal)
            {
                _inputNode.Geometry = _originalGeometry;
                if (!_inputNode.Parent.Children.Contains(_inputNode))
                    _inputNode.Parent.AddObject(_inputNode);
            }

            _selectionManager.SetState(_oldState);
        }
    }
}
