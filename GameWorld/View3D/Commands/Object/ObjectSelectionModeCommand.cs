using GameWorld.Core.Commands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using System.Collections.Generic;

namespace GameWorld.Core.Commands.Object
{

    public class ObjectSelectionModeCommand : ICommand
    {
        SelectionManager _selectionManager;

        GeometrySelectionMode _newMode;
        ISelectable _selectedItem;
        ISelectionState _oldState;


        public string HintText { get => "Select Object"; }
        public bool IsMutation { get => true; }
        public bool AffectsDocument => false;

        public void Configure(GeometrySelectionMode newMode)
        {
            _newMode = newMode;
        }

        public void Configure(ISelectable selectedItem, GeometrySelectionMode newMode)
        {
            _selectedItem = selectedItem;
            _newMode = newMode;
        }


        public ObjectSelectionModeCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public void Execute()
        {
            _oldState = _selectionManager.GetStateCopy();
            var newSelectionState = _selectionManager.CreateSelectionSate(_newMode, _selectedItem);

            if (newSelectionState.Mode == GeometrySelectionMode.Object && _selectedItem != null)
                (newSelectionState as ObjectSelectionState).ModifySelectionSingleObject(_selectedItem, false);
            else if (newSelectionState.Mode == GeometrySelectionMode.Face)
                (newSelectionState as FaceSelectionState).RenderObject = _selectedItem;
            else if (newSelectionState.Mode == GeometrySelectionMode.Edge)
                (newSelectionState as EdgeSelectionState).RenderObject = _selectedItem;
            else if (newSelectionState.Mode == GeometrySelectionMode.Vertex)
                (newSelectionState as VertexSelectionState).RenderObject = _selectedItem;
            else if (newSelectionState.Mode == GeometrySelectionMode.Bone)
                (newSelectionState as BoneSelectionState).RenderObject = _selectedItem;

            // Convert selection between sub-element modes (Blender behavior)
            ConvertSelection(_oldState, newSelectionState);
        }

        /// <summary>
        /// Convert selection data when switching between Face and Vertex modes.
        /// Blender behavior: Face→Vertex extracts all face vertices, Vertex→Face selects faces with all 3 vertices selected.
        /// </summary>
        void ConvertSelection(ISelectionState oldState, ISelectionState newState)
        {
            // Face → Vertex: extract vertex indices from selected faces
            if (oldState is FaceSelectionState oldFaceState && newState is VertexSelectionState newVertState)
            {
                if (oldFaceState.RenderObject == null || oldFaceState.SelectedFaces.Count == 0)
                    return;

                var geometry = oldFaceState.RenderObject.Geometry;
                var indexBuffer = geometry.GetIndexBuffer();
                var vertexIndices = new HashSet<int>();

                foreach (var face in oldFaceState.SelectedFaces)
                {
                    vertexIndices.Add(indexBuffer[face]);
                    vertexIndices.Add(indexBuffer[face + 1]);
                    vertexIndices.Add(indexBuffer[face + 2]);
                }

                newVertState.ModifySelection(new List<int>(vertexIndices), false);
            }
            // Vertex → Face: select faces where all 3 vertices are selected
            else if (oldState is VertexSelectionState oldVertState && newState is FaceSelectionState newFaceState)
            {
                if (oldVertState.RenderObject == null || oldVertState.SelectedVertices.Count == 0)
                    return;

                var geometry = oldVertState.RenderObject.Geometry;
                var indexBuffer = geometry.GetIndexBuffer();
                var selectedVerts = new HashSet<int>(oldVertState.SelectedVertices);
                var faces = new List<int>();

                for (int i = 0; i < indexBuffer.Count; i += 3)
                {
                    if (selectedVerts.Contains(indexBuffer[i]) &&
                        selectedVerts.Contains(indexBuffer[i + 1]) &&
                        selectedVerts.Contains(indexBuffer[i + 2]))
                    {
                        faces.Add(i);
                    }
                }

                newFaceState.RenderObject = oldVertState.RenderObject;
                newFaceState.ModifySelection(faces, false);
            }
            // Face → Edge: extract edges from selected faces
            else if (oldState is FaceSelectionState oldFaceState2 && newState is EdgeSelectionState newEdgeState)
            {
                if (oldFaceState2.RenderObject == null || oldFaceState2.SelectedFaces.Count == 0)
                    return;

                var geometry = oldFaceState2.RenderObject.Geometry;
                var indexBuffer = geometry.GetIndexBuffer();
                var edges = new HashSet<(int, int)>();

                foreach (var face in oldFaceState2.SelectedFaces)
                {
                    var v0 = indexBuffer[face];
                    var v1 = indexBuffer[face + 1];
                    var v2 = indexBuffer[face + 2];
                    edges.Add((Math.Min(v0, v1), Math.Max(v0, v1)));
                    edges.Add((Math.Min(v1, v2), Math.Max(v1, v2)));
                    edges.Add((Math.Min(v0, v2), Math.Max(v0, v2)));
                }

                newEdgeState.ModifySelection(edges, false);
            }
            // Edge → Face: require every boundary edge of the triangle.
            else if (oldState is EdgeSelectionState oldEdges && newState is FaceSelectionState newFaces)
            {
                if (oldEdges.RenderObject == null || oldEdges.SelectedEdges.Count == 0)
                    return;

                var indices = oldEdges.RenderObject.Geometry.GetIndexBuffer();
                var faces = new List<int>();
                for (int i = 0; i < indices.Count; i += 3)
                {
                    var a = indices[i];
                    var b = indices[i + 1];
                    var c = indices[i + 2];
                    if (oldEdges.SelectedEdges.Contains((Math.Min(a, b), Math.Max(a, b))) &&
                        oldEdges.SelectedEdges.Contains((Math.Min(b, c), Math.Max(b, c))) &&
                        oldEdges.SelectedEdges.Contains((Math.Min(c, a), Math.Max(c, a))))
                        faces.Add(i);
                }

                newFaces.RenderObject = oldEdges.RenderObject;
                newFaces.ModifySelection(faces, false);
            }
            // Edge → Vertex: extract vertex indices from selected edges
            else if (oldState is EdgeSelectionState oldEdgeState && newState is VertexSelectionState newVertState2)
            {
                if (oldEdgeState.RenderObject == null || oldEdgeState.SelectedEdges.Count == 0)
                    return;

                newVertState2.ModifySelection(new List<int>(oldEdgeState.GetSelectedVertexIndices()), false);
            }
            // Vertex → Edge: select edges where both vertices are selected
            else if (oldState is VertexSelectionState oldVertState2 && newState is EdgeSelectionState newEdgeState2)
            {
                if (oldVertState2.RenderObject == null || oldVertState2.SelectedVertices.Count == 0)
                    return;

                var geometry = oldVertState2.RenderObject.Geometry;
                var indexBuffer = geometry.GetIndexBuffer();
                var selectedVerts = new HashSet<int>(oldVertState2.SelectedVertices);
                var edges = new List<(int, int)>();
                var processed = new HashSet<(int, int)>();

                for (int i = 0; i < indexBuffer.Count; i += 3)
                {
                    var v0 = indexBuffer[i];
                    var v1 = indexBuffer[i + 1];
                    var v2 = indexBuffer[i + 2];
                    var candidates = new[] {
                        (Math.Min(v0, v1), Math.Max(v0, v1)),
                        (Math.Min(v1, v2), Math.Max(v1, v2)),
                        (Math.Min(v0, v2), Math.Max(v0, v2))
                    };
                    foreach (var edge in candidates)
                    {
                        if (!processed.Contains(edge) && selectedVerts.Contains(edge.Item1) && selectedVerts.Contains(edge.Item2))
                            edges.Add(edge);
                        processed.Add(edge);
                    }
                }

                newEdgeState2.ModifySelection(edges, false);
            }
        }

        public void Undo()
        {
            _selectionManager.SetState(_oldState);
        }


    }
}
