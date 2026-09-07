using GameWorld.Core.Services;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.InteropServices;

namespace GameWorld.Core.Rendering
{
    /// <summary>
    /// Instance data for edge quad rendering.
    /// Each instance represents one edge segment as a screen-space quad.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EdgeQuadInstanceData : IVertexType
    {
        public Vector3 InstanceP0;       // Edge start world position
        public Vector3 InstanceP1;       // Edge end world position
        public Vector3 InstanceC0;       // Start endpoint color
        public Vector3 InstanceC1;       // End endpoint color
        public float InstanceWidth;      // Screen-space half-width in pixels

        public static readonly VertexDeclaration VertexDeclaration;
        static EdgeQuadInstanceData()
        {
            var elements = new VertexElement[]
            {
                new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 1),      // InstanceP0
                new VertexElement(sizeof(float) * 3, VertexElementFormat.Vector3, VertexElementUsage.Normal, 1),  // InstanceP1
                new VertexElement(sizeof(float) * 6, VertexElementFormat.Vector3, VertexElementUsage.Normal, 2),  // InstanceC0
                new VertexElement(sizeof(float) * 9, VertexElementFormat.Vector3, VertexElementUsage.Normal, 3),  // InstanceC1
                new VertexElement(sizeof(float) * 12, VertexElementFormat.Single, VertexElementUsage.Normal, 4),  // InstanceWidth
            };
            VertexDeclaration = new VertexDeclaration(elements);
        }

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }

    /// <summary>
    /// Renders wireframe edges as screen-space quads with anti-aliasing.
    /// Based on Blender's overlay_edit_mesh_edge_vert.glsl approach.
    /// </summary>
    public class EdgeQuadInstanceMesh : IDisposable
    {
        Effect _effect;
        VertexDeclaration _instanceVertexDeclaration;

        DynamicVertexBuffer _instanceBuffer;
        VertexBuffer _geometryBuffer;
        IndexBuffer _indexBuffer;

        VertexBufferBinding[] _bindings;
        EdgeQuadInstanceData[] _instanceData;
        BoundingBox _worldBounds;

        readonly int _maxInstanceCount = 50000;
        int _currentInstanceCount;
        internal int CurrentInstanceCount => _currentInstanceCount;

        public float DefaultEdgeHalfWidth { get; set; } =
            EditOverlayStyle.WireHalfWidth;

        public EdgeQuadInstanceMesh(IDeviceResolver deviceResolverComponent, IScopedResourceLibrary resourceLibrary)
        {
            Initialize(
                deviceResolverComponent.Device,
                resourceLibrary.GetStaticEffect(ShaderTypes.EdgeQuad));
        }

        public EdgeQuadInstanceMesh(GraphicsDevice device, Effect effect)
        {
            Initialize(device, effect);
        }

        void Initialize(GraphicsDevice device, Effect effect)
        {
            _effect = effect;

            _instanceVertexDeclaration = EdgeQuadInstanceData.VertexDeclaration;
            GenerateGeometry(device);
            _instanceBuffer = new DynamicVertexBuffer(device, _instanceVertexDeclaration, _maxInstanceCount, BufferUsage.WriteOnly);
            _instanceData = new EdgeQuadInstanceData[_maxInstanceCount];

            _bindings = new VertexBufferBinding[2];
            _bindings[0] = new VertexBufferBinding(_geometryBuffer);
            _bindings[1] = new VertexBufferBinding(_instanceBuffer, 0, 1);
        }

        /// <summary>
        /// Generate a unit quad [-0.5, 0.5] for edge expansion.
        /// </summary>
        void GenerateGeometry(GraphicsDevice device)
        {
            // Unit quad centered at origin
            // Position.xy determines which corner of the edge quad:
            // x: -0.5 = start endpoint, +0.5 = end endpoint
            // y: -0.5 = left side, +0.5 = right side
            var vertices = new VertexPositionTexture[4];
            vertices[0] = new VertexPositionTexture(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 0));  // Start, left
            vertices[1] = new VertexPositionTexture(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 0));   // End, left
            vertices[2] = new VertexPositionTexture(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 1));   // Start, right
            vertices[3] = new VertexPositionTexture(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 1));    // End, right

            _geometryBuffer = new VertexBuffer(device, VertexPositionTexture.VertexDeclaration, 4, BufferUsage.WriteOnly);
            _geometryBuffer.SetData(vertices);

            // Two triangles forming a quad
            var indices = new int[6];
            indices[0] = 0; indices[1] = 1; indices[2] = 2;  // First triangle
            indices[3] = 1; indices[4] = 3; indices[5] = 2;  // Second triangle

            _indexBuffer = new IndexBuffer(device, typeof(int), 6, BufferUsage.WriteOnly);
            _indexBuffer.SetData(indices);
        }

        /// <summary>
        /// Update instance data for all edges.
        /// </summary>
        /// <param name="edges">List of edge data (positions and colors)</param>
        public void Update(EdgeData[] edges)
        {
            ArgumentNullException.ThrowIfNull(edges);

            _currentInstanceCount = Math.Min(edges.Length, _maxInstanceCount);
            if (_currentInstanceCount == 0)
                return;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            for (var i = 0; i < _currentInstanceCount; i++)
            {
                var edge = edges[i];
                _instanceData[i].InstanceP0 = edge.P0;
                _instanceData[i].InstanceP1 = edge.P1;
                min = Vector3.Min(min, Vector3.Min(edge.P0, edge.P1));
                max = Vector3.Max(max, Vector3.Max(edge.P0, edge.P1));
                _instanceData[i].InstanceC0 = edge.C0;
                _instanceData[i].InstanceC1 = edge.C1;
                _instanceData[i].InstanceWidth = edge.Width > 0 ? edge.Width : DefaultEdgeHalfWidth;
            }

            _worldBounds = new BoundingBox(min, max);

            _instanceBuffer.SetData(
                _instanceData,
                0,
                _currentInstanceCount,
                SetDataOptions.Discard);
        }

        public void Draw(Matrix view, Matrix projection, float viewportHeight, float viewportWidth, GraphicsDevice device, float opacity = 1)
        {
            if (_currentInstanceCount == 0)
                return;

            _effect.CurrentTechnique = _effect.Techniques["EdgeQuad"];
            _effect.Parameters["ViewProjection"].SetValue(view * projection);
            _effect.Parameters["ViewportHeight"].SetValue(viewportHeight);
            _effect.Parameters["ViewportWidth"].SetValue(viewportWidth);
            _effect.Parameters["BaseOpacity"].SetValue(opacity);
            _effect.Parameters["EdgeDepthBias"].SetValue(0.0f);
            _effect.Parameters["OverlayOpacity"].SetValue(
                EditOverlayVisibility.CalculateDetailOpacity(
                    _worldBounds,
                    Matrix.Identity,
                    view,
                    projection,
                    (int)viewportWidth,
                    (int)viewportHeight,
                    _currentInstanceCount));

            // Alpha blending for anti-aliased edges
            var previousRasterizerState =
                device.RasterizerState;
            device.BlendState = BlendState.AlphaBlend;
            device.RasterizerState =
                RasterizerState.CullNone;
            try
            {
                device.Indices = _indexBuffer;
                _effect.CurrentTechnique.Passes[0].Apply();

                device.SetVertexBuffers(_bindings);
                // Draw 2 triangles (one quad) per instance
                device.DrawInstancedPrimitives(PrimitiveType.TriangleList, 0, 0, 4, 0, 2, _currentInstanceCount);
            }
            finally
            {
                device.RasterizerState =
                    previousRasterizerState;
                device.BlendState = BlendState.Opaque;
            }
        }

        public void Dispose()
        {
            _instanceBuffer?.Dispose();
            _geometryBuffer?.Dispose();
            _indexBuffer?.Dispose();
        }
    }

    /// <summary>
    /// Data for a single edge segment.
    /// </summary>
    public struct EdgeData
    {
        public Vector3 P0;          // Start world position
        public Vector3 P1;          // End world position
        public Vector3 C0;          // Start color
        public Vector3 C1;          // End color
        public float Width;         // Screen-space half-width in pixels (0 = use default)
    }
}
