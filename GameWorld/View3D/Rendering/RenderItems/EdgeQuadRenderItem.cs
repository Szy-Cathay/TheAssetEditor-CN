using System;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.RenderItems
{
    public class EdgeQuadRenderItem : IRenderItem
    {
        public bool IncludeInPhotoCapture => false;

        public EdgeQuadInstanceMesh EdgeQuadRenderer { get; set; }
        public EdgeData[] Edges { get; set; }
        private EdgeData[] _lastUploadedEdges;
        private bool _needsUpload = true;

        public void MarkDirty() => _needsUpload = true;

        public void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique)
        {
            if (renderingTechnique != RenderingTechnique.Normal)
                return;

            if (EdgeQuadRenderer == null)
                return;

            var edges = Edges ?? Array.Empty<EdgeData>();

            // Only upload to GPU when edge data changed
            if (_needsUpload || _lastUploadedEdges != edges)
            {
                EdgeQuadRenderer.Update(edges);
                _lastUploadedEdges = edges;
                _needsUpload = false;
            }

            if (EdgeQuadRenderer.CurrentInstanceCount == 0)
                return;

            var viewportHeight = device.Viewport.Height;
            var viewportWidth = device.Viewport.Width;

            EdgeQuadRenderer.Draw(parameters.View, parameters.Projection, viewportHeight, viewportWidth, device);
        }
    }
}
