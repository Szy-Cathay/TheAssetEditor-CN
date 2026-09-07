using GameWorld.Core.Animation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.InteropServices;

namespace GameWorld.Core.Rendering.RenderItems
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct AnimatedEdgeQuadInstanceData : IVertexType
    {
        public Vector3 BindP0;
        public Vector4 Weights0;
        public Vector4 BoneIndices0;
        public Vector3 BindP1;
        public Vector4 Weights1;
        public Vector4 BoneIndices1;
        public Vector3 Colour0;
        public Vector3 Colour1;

        public static readonly VertexDeclaration VertexDeclaration =
            new(
                new VertexElement(
                    0,
                    VertexElementFormat.Vector3,
                    VertexElementUsage.Position,
                    1),
                new VertexElement(
                    12,
                    VertexElementFormat.Vector4,
                    VertexElementUsage.Color,
                    1),
                new VertexElement(
                    28,
                    VertexElementFormat.Vector4,
                    VertexElementUsage.BlendIndices,
                    1),
                new VertexElement(
                    44,
                    VertexElementFormat.Vector3,
                    VertexElementUsage.Position,
                    2),
                new VertexElement(
                    56,
                    VertexElementFormat.Vector4,
                    VertexElementUsage.Color,
                    2),
                new VertexElement(
                    72,
                    VertexElementFormat.Vector4,
                    VertexElementUsage.BlendIndices,
                    2),
                new VertexElement(
                    88,
                    VertexElementFormat.Vector3,
                    VertexElementUsage.Normal,
                    5),
                new VertexElement(
                    100,
                    VertexElementFormat.Vector3,
                    VertexElementUsage.Normal,
                    6));

        VertexDeclaration IVertexType.VertexDeclaration =>
            VertexDeclaration;
    }

    public sealed class AnimatedWireframeRenderItem :
        IRenderItem,
        IDisposable
    {
        readonly IScopedResourceLibrary _resourceLibrary;
        readonly Vector4 _colour;
        readonly int _maxEdges;
        MeshPoseSnapshot _pose;
        bool _renderFullTopology = true;
        (int v0, int v1)[] _selectedEdges = [];
        ushort[]? _sourceIndices;
        int _topologyVersion = -1;
        ushort[] _lineIndices = [];
        VertexBuffer? _quadVertexBuffer;
        IndexBuffer? _quadIndexBuffer;
        DynamicVertexBuffer? _instanceBuffer;
        AnimatedEdgeQuadInstanceData[] _instanceData = [];
        VertexBufferBinding[] _bindings = [];
        int _vertexDataVersion = -1;
        bool _instanceDataDirty = true;
        HashSet<int>? _selectedVertices;
        Vector3 _selectedVertexColour;

        public bool IsSelectionOverlay { get; set; }
        public float DepthBias { get; set; } =
            EditOverlayStyle.WireDepthBias;
        public float EdgeHalfWidth { get; set; } =
            EditOverlayStyle.WireHalfWidth;
        internal int IndexBufferBuildCount { get; private set; }
        internal int EdgePrimitiveCount =>
            _lineIndices.Length / 2;

        public AnimatedWireframeRenderItem(
            MeshPoseSnapshot pose,
            IScopedResourceLibrary resourceLibrary,
            Vector4 colour)
            : this(
                pose,
                resourceLibrary,
                colour,
                int.MaxValue)
        {
        }

        public AnimatedWireframeRenderItem(
            MeshPoseSnapshot pose,
            IScopedResourceLibrary resourceLibrary,
            Vector4 colour,
            int maxEdges)
        {
            _pose = pose;
            _resourceLibrary = resourceLibrary;
            _colour = colour;
            _maxEdges = maxEdges;
            UpdateTopology();
        }

        public void UpdatePose(MeshPoseSnapshot pose)
        {
            var geometryChanged =
                !ReferenceEquals(
                    _pose.Geometry,
                    pose.Geometry);
            var vertexDataChanged =
                geometryChanged ||
                _vertexDataVersion !=
                    pose.Geometry.VertexDataVersion;
            _pose = pose;
            if (_renderFullTopology)
            {
                UpdateTopology();
            }
            else if (geometryChanged ||
                     !ReferenceEquals(
                         _sourceIndices,
                         pose.Geometry.IndexArray) ||
                     _topologyVersion !=
                         pose.Geometry.TopologyVersion)
            {
                UpdateSelectedEdgeTopology();
            }

            if (vertexDataChanged)
                _instanceDataDirty = true;
        }

        public void UpdateEdges(
            IEnumerable<(int v0, int v1)> edges)
        {
            ArgumentNullException.ThrowIfNull(edges);

            var uniqueEdges =
                new HashSet<(int v0, int v1)>();
            foreach (var (first, second) in edges)
            {
                if (first == second)
                    continue;

                var edge = first < second
                    ? (first, second)
                    : (second, first);
                uniqueEdges.Add(edge);
            }

            _renderFullTopology = false;
            _selectedEdges = uniqueEdges.ToArray();
            UpdateSelectedEdgeTopology();
        }

        public void UpdateSelectedVertices(
            IEnumerable<int> selectedVertices,
            Vector3 selectedColour)
        {
            ArgumentNullException.ThrowIfNull(selectedVertices);
            _selectedVertices = selectedVertices.ToHashSet();
            _selectedVertexColour = selectedColour;
            _instanceDataDirty = true;
        }

        public void ClearSelectedVertices()
        {
            if (_selectedVertices == null)
                return;

            _selectedVertices = null;
            _instanceDataDirty = true;
        }

        public bool SupportsTechnique(
            RenderingTechnique technique)
        {
            return technique == RenderingTechnique.Normal;
        }

        public void Draw(
            GraphicsDevice device,
            CommonShaderParameters parameters,
            RenderingTechnique renderingTechnique)
        {
            if (!SupportsTechnique(renderingTechnique) ||
                EdgePrimitiveCount == 0)
            {
                return;
            }

            EnsureBuffers(device);
            var effect = _resourceLibrary.GetStaticEffect(
                ShaderTypes.EdgeQuad);
            effect.CurrentTechnique =
                effect.Techniques["AnimatedEdgeQuad"];
            var viewportWidth = device.Viewport.Width;
            var viewportHeight = device.Viewport.Height;
            effect.Parameters["World"].SetValue(
                _pose.WorldTransform);
            effect.Parameters["ViewProjection"].SetValue(
                parameters.View * parameters.Projection);
            effect.Parameters["ViewportWidth"].SetValue(
                (float)viewportWidth);
            effect.Parameters["ViewportHeight"].SetValue(
                (float)viewportHeight);
            effect.Parameters["BaseOpacity"].SetValue(
                _colour.W * (IsSelectionOverlay ? parameters.SelectedOverlayOpacity : parameters.OverlayOpacity));
            effect.Parameters["EdgeDepthBias"].SetValue(
                DepthBias);
            effect.Parameters["EdgeHalfWidth"].SetValue(
                EdgeHalfWidth);
            effect.Parameters["OverlayOpacity"].SetValue(
                IsSelectionOverlay ? 1.0f : EditOverlayVisibility.CalculateDetailOpacity(
                    _pose.GetConservativeAnimatedBounds(),
                    _pose.WorldTransform,
                    parameters.View,
                    parameters.Projection,
                    viewportWidth,
                    viewportHeight,
                    EdgePrimitiveCount));
            effect.Parameters["CapabilityFlag_ApplyAnimation"]
                .SetValue(_pose.ApplyAnimation);
            effect.Parameters["Animation_WeightCount"]
                .SetValue(_pose.AnimationWeightCount);
            if (_pose.ApplyAnimation)
            {
                effect.Parameters["Animation_Tranforms"]
                    .SetValue(_pose.AnimationTransforms);
            }

            var previousBlendState = device.BlendState;
            var previousRasterizerState =
                device.RasterizerState;
            device.BlendState = BlendState.AlphaBlend;
            device.RasterizerState =
                RasterizerState.CullNone;
            try
            {
                device.Indices = _quadIndexBuffer;
                device.SetVertexBuffers(_bindings);
                effect.CurrentTechnique.Passes[0].Apply();
                device.DrawInstancedPrimitives(
                    PrimitiveType.TriangleList,
                    0,
                    0,
                    4,
                    0,
                    2,
                    EdgePrimitiveCount);
            }
            finally
            {
                device.BlendState = previousBlendState;
                device.RasterizerState =
                    previousRasterizerState;
            }
        }

        void UpdateTopology()
        {
            var indices = _pose.Geometry.IndexArray;
            if (ReferenceEquals(indices, _sourceIndices) &&
                _topologyVersion ==
                    _pose.Geometry.TopologyVersion)
            {
                return;
            }

            _sourceIndices = indices;
            _topologyVersion =
                _pose.Geometry.TopologyVersion;
            _lineIndices =
                EdgeIndexCacheBuilder.BuildLineIndices(
                    indices,
                    _maxEdges);
            _instanceDataDirty = true;
        }

        void UpdateSelectedEdgeTopology()
        {
            var vertexCount = _pose.Geometry.VertexCount();
            var lineIndices =
                new List<ushort>(_selectedEdges.Length * 2);
            foreach (var (first, second) in _selectedEdges)
            {
                if (first < 0 ||
                    second < 0 ||
                    first >= vertexCount ||
                    second >= vertexCount ||
                    first > ushort.MaxValue ||
                    second > ushort.MaxValue)
                {
                    continue;
                }

                lineIndices.Add((ushort)first);
                lineIndices.Add((ushort)second);
            }

            _sourceIndices = _pose.Geometry.IndexArray;
            _topologyVersion =
                _pose.Geometry.TopologyVersion;
            _lineIndices = lineIndices.ToArray();
            _instanceDataDirty = true;
        }

        void EnsureBuffers(GraphicsDevice device)
        {
            if (_quadVertexBuffer == null)
                CreateQuadBuffers(device);
            if (_instanceBuffer == null ||
                _instanceBuffer.VertexCount !=
                    EdgePrimitiveCount)
            {
                _instanceBuffer?.Dispose();
                _instanceBuffer = new DynamicVertexBuffer(
                    device,
                    AnimatedEdgeQuadInstanceData.VertexDeclaration,
                    EdgePrimitiveCount,
                    BufferUsage.WriteOnly);
                _bindings =
                [
                    new VertexBufferBinding(_quadVertexBuffer),
                    new VertexBufferBinding(_instanceBuffer, 0, 1)
                ];
                _instanceDataDirty = true;
                IndexBufferBuildCount++;
            }

            if (!_instanceDataDirty)
                return;

            var instances = BuildInstanceData();
            _instanceBuffer.SetData(
                instances,
                0,
                instances.Length,
                SetDataOptions.Discard);
            _vertexDataVersion =
                _pose.Geometry.VertexDataVersion;
            _instanceDataDirty = false;
        }

        AnimatedEdgeQuadInstanceData[] BuildInstanceData()
        {
            if (_instanceData.Length != EdgePrimitiveCount)
            {
                _instanceData =
                    new AnimatedEdgeQuadInstanceData[
                        EdgePrimitiveCount];
            }

            for (var edgeIndex = 0;
                 edgeIndex < _instanceData.Length;
                 edgeIndex++)
            {
                var firstIndex = _lineIndices[edgeIndex * 2];
                var secondIndex = _lineIndices[edgeIndex * 2 + 1];
                var first = _pose.Geometry.VertexArray[firstIndex];
                var second = _pose.Geometry.VertexArray[secondIndex];
                _instanceData[edgeIndex] =
                    new AnimatedEdgeQuadInstanceData
                    {
                        BindP0 = first.Position3(),
                        Weights0 = first.BlendWeights,
                        BoneIndices0 = first.BlendIndices,
                        BindP1 = second.Position3(),
                        Weights1 = second.BlendWeights,
                        BoneIndices1 = second.BlendIndices,
                        Colour0 = GetEndpointColour(firstIndex),
                        Colour1 = GetEndpointColour(secondIndex)
                    };
            }

            return _instanceData;
        }

        private Vector3 GetEndpointColour(int vertexIndex)
        {
            if (_selectedVertices?.Contains(vertexIndex) == true)
                return _selectedVertexColour;

            return new Vector3(_colour.X, _colour.Y, _colour.Z);
        }

        void CreateQuadBuffers(GraphicsDevice device)
        {
            var vertices = new[]
            {
                new VertexPositionTexture(
                    new Vector3(-0.5f, -0.5f, 0),
                    Vector2.Zero),
                new VertexPositionTexture(
                    new Vector3(0.5f, -0.5f, 0),
                    Vector2.UnitX),
                new VertexPositionTexture(
                    new Vector3(-0.5f, 0.5f, 0),
                    Vector2.UnitY),
                new VertexPositionTexture(
                    new Vector3(0.5f, 0.5f, 0),
                    Vector2.One)
            };
            _quadVertexBuffer = new VertexBuffer(
                device,
                VertexPositionTexture.VertexDeclaration,
                vertices.Length,
                BufferUsage.WriteOnly);
            _quadVertexBuffer.SetData(vertices);

            ushort[] indices = [0, 1, 2, 1, 3, 2];
            _quadIndexBuffer = new IndexBuffer(
                device,
                IndexElementSize.SixteenBits,
                indices.Length,
                BufferUsage.WriteOnly);
            _quadIndexBuffer.SetData(indices);
        }

        public void Dispose()
        {
            _instanceBuffer?.Dispose();
            _quadVertexBuffer?.Dispose();
            _quadIndexBuffer?.Dispose();
            _instanceBuffer = null;
            _quadVertexBuffer = null;
            _quadIndexBuffer = null;
            _instanceData = [];
            _bindings = [];
        }
    }
}
