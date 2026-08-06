using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace GameWorld.Core.Components.Selection
{
    public class VertexSelectionState : ISelectionState
    {
        public GeometrySelectionMode Mode => GeometrySelectionMode.Vertex;
        public event SelectionStateChanged SelectionChanged;

        public ISelectable RenderObject { get; set; }
        public List<int> SelectedVertices { get; set; } = new List<int>();
        public int? ActiveVertex { get; set; }
        public List<float> VertexWeights { get; set; } = new List<float>();

        float _selectionDistanceFallof;

        public VertexSelectionState(ISelectable renderObj, float vertexSelectionFallof)
        {
            RenderObject = renderObj;
            VertexWeights = Enumerable.Repeat(0.0f, RenderObject.Geometry.VertexCount()).ToList();
            _selectionDistanceFallof = vertexSelectionFallof;
        }

        public void ModifySelection(IEnumerable<int> newSelectionItems, bool onlyRemove)
        {
            var requestedItems = newSelectionItems.ToList();
            var updatedSelection = new HashSet<int>(SelectedVertices);
            if (onlyRemove)
                updatedSelection.ExceptWith(requestedItems);
            else
                updatedSelection.UnionWith(requestedItems);

            SelectedVertices = updatedSelection.OrderBy(index => index).ToList();
            if (!onlyRemove && requestedItems.Count > 0)
                ActiveVertex = requestedItems[^1];
            else if (ActiveVertex.HasValue &&
                     !updatedSelection.Contains(ActiveVertex.Value))
                ActiveVertex = SelectedVertices.Count > 0
                    ? SelectedVertices[^1]
                    : null;
            UpdateWeights(_selectionDistanceFallof);
            SelectionChanged?.Invoke(this, true);
        }

        public void SetSelection(IEnumerable<int> newSelectionItems)
        {
            var requestedItems = newSelectionItems.ToList();
            SelectedVertices = requestedItems
                .Distinct()
                .OrderBy(index => index)
                .ToList();
            ActiveVertex = requestedItems.Count > 0
                ? requestedItems[^1]
                : null;
            UpdateWeights(_selectionDistanceFallof);
            SelectionChanged?.Invoke(this, true);
        }

        public void UpdateWeights(float distanceOffset)
        {
            _selectionDistanceFallof = distanceOffset;
            var geo = RenderObject.Geometry;
            var vertexArray = geo.VertexArray;
            var vertCount = vertexArray.Length;
            var selectedSet = new HashSet<int>(SelectedVertices);

            if (VertexWeights.Count != vertCount)
                VertexWeights = Enumerable.Repeat(0.0f, vertCount).ToList();

            for (var i = 0; i < vertCount; i++)
                VertexWeights[i] = 0;

            if (selectedSet.Count == 0)
                return;

            if (selectedSet.Count == vertCount || distanceOffset <= 0.0f)
            {
                foreach (var vert in selectedSet)
                    VertexWeights[vert] = 1.0f;
                return;
            }

            var selectedPositions = new Vector3[selectedSet.Count];
            var selectedPositionIndex = 0;
            foreach (var selectedVertex in selectedSet)
            {
                var position = vertexArray[selectedVertex].Position;
                selectedPositions[selectedPositionIndex++] =
                    new Vector3(position.X, position.Y, position.Z);
            }

            var nearestPointSearch = new NearestPointSearch(selectedPositions);
            for (var i = 0; i < vertCount; i++)
            {
                if (selectedSet.Contains(i))
                {
                    VertexWeights[i] = 1.0f;
                }
                else
                {
                    var pos = vertexArray[i].Position;
                    var currentPos = new Vector3(pos.X, pos.Y, pos.Z);
                    var distanceSquared =
                        nearestPointSearch.FindNearestDistanceSquared(currentPos);
                    VertexWeights[i] = ProportionalEditingMath.CalculateLinearWeight(
                        distanceSquared,
                        distanceOffset);
                }
            }
        }

        public List<int> CurrentSelection()
        {
            return SelectedVertices;
        }

        public void Clear()
        {
            SelectedVertices.Clear();
            ActiveVertex = null;
            UpdateWeights(_selectionDistanceFallof);
            SelectionChanged?.Invoke(this, true);
        }

        public ISelectionState Clone()
        {
            return new VertexSelectionState(RenderObject, _selectionDistanceFallof)
            {
                SelectedVertices = new List<int>(SelectedVertices),
                ActiveVertex = ActiveVertex,
                VertexWeights = new List<float>(VertexWeights),
            };
        }

        public int SelectionCount()
        {
            return SelectedVertices.Count();
        }

        public ISelectable GetSingleSelectedObject()
        {
            return RenderObject;
        }

        public List<ISelectable> SelectedObjects()
        {
            return new List<ISelectable>() { RenderObject };
        }
    }
}
