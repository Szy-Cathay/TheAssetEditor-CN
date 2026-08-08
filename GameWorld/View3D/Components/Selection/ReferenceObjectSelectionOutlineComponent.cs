using System;
using System.Collections.Generic;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Components.Selection
{
    public sealed class ReferenceObjectSelectionOutlineComponent :
        BaseComponent,
        IDisposable
    {
        private readonly SelectionManager _selectionManager;
        private readonly RenderEngineComponent _renderEngine;
        private readonly HashSet<Rmv2MeshNode> _outlinedMeshes = [];

        public ReferenceObjectSelectionOutlineComponent(
            SelectionManager selectionManager,
            RenderEngineComponent renderEngine)
        {
            _selectionManager = selectionManager;
            _renderEngine = renderEngine;
        }

        public override void Draw(GameTime gameTime)
        {
            ClearOutlines();
            if (_selectionManager.GetState() is not
                ObjectSelectionState objectSelection)
            {
                return;
            }

            foreach (var item in objectSelection.CurrentSelection())
            {
                if (item is not Rmv2MeshNode mesh)
                    continue;

                mesh.SetSelectionOutline(true);
                _outlinedMeshes.Add(mesh);
            }

            if (_outlinedMeshes.Count > 0)
                _renderEngine.RequestSelectionOutline();
        }

        private void ClearOutlines()
        {
            foreach (var mesh in _outlinedMeshes)
                mesh.SetSelectionOutline(false);
            _outlinedMeshes.Clear();
        }

        public void Dispose() => ClearOutlines();
    }
}
