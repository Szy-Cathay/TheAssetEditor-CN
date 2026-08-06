using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.SceneNodes;

namespace GameWorld.Core.Components.Selection
{
    public class FaceSelectionState : ISelectionState
    {
        public GeometrySelectionMode Mode => GeometrySelectionMode.Face;
        public event SelectionStateChanged SelectionChanged;

        public ISelectable RenderObject { get; set; }
        public List<int> SelectedFaces { get; set; } = new List<int>();
        public int? ActiveFace { get; set; }

        public void ModifySelection(IEnumerable<int> newSelectionItems, bool onlyRemove)
        {
            var requestedItems = newSelectionItems.ToList();
            if (onlyRemove)
            {
                foreach (var newSelectionItem in requestedItems)
                {
                    if (SelectedFaces.Contains(newSelectionItem))
                        SelectedFaces.Remove(newSelectionItem);
                }
            }
            else
            {
                foreach (var newSelectionItem in requestedItems)
                {
                    if (!SelectedFaces.Contains(newSelectionItem))
                        SelectedFaces.Add(newSelectionItem);
                }
            }
            if (!onlyRemove && requestedItems.Count > 0)
                ActiveFace = requestedItems[^1];
            else if (ActiveFace.HasValue &&
                     !SelectedFaces.Contains(ActiveFace.Value))
                ActiveFace = SelectedFaces.Count > 0
                    ? SelectedFaces[^1]
                    : null;
            SelectionChanged?.Invoke(this, true);
        }


        public List<int> CurrentSelection()
        {
            return SelectedFaces;
        }

        public void Clear()
        {
            SelectedFaces.Clear();
            ActiveFace = null;
            SelectionChanged?.Invoke(this, true);
        }


        public void EnsureSorted()
        {
            SelectedFaces = SelectedFaces.Distinct().OrderBy(x => x).ToList();
            if (ActiveFace.HasValue &&
                !SelectedFaces.Contains(ActiveFace.Value))
                ActiveFace = SelectedFaces.Count > 0
                    ? SelectedFaces[^1]
                    : null;
        }


        public ISelectionState Clone()
        {
            return new FaceSelectionState()
            {
                RenderObject = RenderObject,
                SelectedFaces = new List<int>(SelectedFaces),
                ActiveFace = ActiveFace
            };
        }

        public int SelectionCount()
        {
            return SelectedFaces.Count();
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

