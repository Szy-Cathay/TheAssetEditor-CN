using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.SceneNodes;

namespace GameWorld.Core.Components.Selection
{
    public class EdgeSelectionState : ISelectionState
    {
        public GeometrySelectionMode Mode => GeometrySelectionMode.Edge;
        public event SelectionStateChanged SelectionChanged;

        public ISelectable RenderObject { get; set; }

        /// <summary>
        /// Selected edges stored as ordered pairs (v0, v1) where v0 &lt; v1
        /// </summary>
        public HashSet<(int v0, int v1)> SelectedEdges { get; set; } = new HashSet<(int, int)>();
        public (int v0, int v1)? ActiveEdge { get; set; }

        public void ModifySelection(IEnumerable<(int v0, int v1)> newEdges, bool onlyRemove)
        {
            var requestedEdges = newEdges.ToList();
            if (onlyRemove)
            {
                foreach (var edge in requestedEdges)
                    SelectedEdges.Remove(edge);
            }
            else
            {
                foreach (var edge in requestedEdges)
                    SelectedEdges.Add(edge);
            }
            if (!onlyRemove && requestedEdges.Count > 0)
                ActiveEdge = requestedEdges[^1];
            else if (ActiveEdge.HasValue &&
                     !SelectedEdges.Contains(ActiveEdge.Value))
            {
                var remainingEdges = SelectedEdges
                    .OrderBy(edge => edge.v0)
                    .ThenBy(edge => edge.v1)
                    .ToList();
                ActiveEdge = remainingEdges.Count > 0
                    ? remainingEdges[^1]
                    : null;
            }
            SelectionChanged?.Invoke(this, true);
        }

        public List<int> CurrentSelection()
        {
            // Return vertex indices for compatibility
            var vertices = new HashSet<int>();
            foreach (var edge in SelectedEdges)
            {
                vertices.Add(edge.v0);
                vertices.Add(edge.v1);
            }
            return vertices.ToList();
        }

        /// <summary>
        /// Get all unique vertex indices from selected edges
        /// </summary>
        public HashSet<int> GetSelectedVertexIndices()
        {
            var vertices = new HashSet<int>();
            foreach (var edge in SelectedEdges)
            {
                vertices.Add(edge.v0);
                vertices.Add(edge.v1);
            }
            return vertices;
        }

        public void Clear()
        {
            SelectedEdges.Clear();
            ActiveEdge = null;
            SelectionChanged?.Invoke(this, true);
        }

        public ISelectionState Clone()
        {
            return new EdgeSelectionState()
            {
                RenderObject = RenderObject,
                SelectedEdges = new HashSet<(int, int)>(SelectedEdges),
                ActiveEdge = ActiveEdge
            };
        }

        public int SelectionCount()
        {
            return SelectedEdges.Count;
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
