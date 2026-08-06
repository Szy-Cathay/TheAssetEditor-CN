using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Shaders;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.RenderItems
{
    public class GeometryRenderItem : IRenderItem
    {
        private readonly MeshObject _geometry;
        private readonly IShader _shader;
        private Matrix _modelMatrix;
        private bool _selectionMaskEnabled;

        public GeometryRenderItem(MeshObject geometry, IShader shader, Matrix modelMatrix)
        {
            _geometry = geometry;
            _shader = shader;
            _modelMatrix = modelMatrix;
        }

        // Update world matrix for pooled reuse (avoids per-frame allocation)
        public void UpdateWorldMatrix(Matrix modelMatrix)
        {
            _modelMatrix = modelMatrix;
        }

        public void SetSelectionMask(bool enabled)
        {
            _selectionMaskEnabled = enabled;
        }

        public bool SupportsTechnique(RenderingTechnique technique) => _shader.SupportsTechnique(technique);

        public void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique)
        {
            if (_shader.SupportsTechnique(renderingTechnique) == false)
                return;

            _shader.SetTechnique(renderingTechnique);
            _shader.SetSelectionMask(
                renderingTechnique != RenderingTechnique.Emissive &&
                _selectionMaskEnabled);
            _shader.Apply(parameters, _modelMatrix);

            ApplyMesh(_shader, device, _geometry.GetGeometryContext());
        }

        void ApplyMesh(IShader effect, GraphicsDevice device, IGraphicsCardGeometry geometry)
        {
            device.Indices = geometry.IndexBuffer;
            device.SetVertexBuffer(geometry.VertexBuffer);
            // Use actual index count from CPU-side data, not GPU buffer capacity.
            // GraphicsCardGeometry may reuse a larger buffer via SetData() after face deletion,
            // in which case IndexBuffer.IndexCount returns the old capacity, not the current data size.
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _geometry.GetIndexCount() / 3);
        }
    }
}

