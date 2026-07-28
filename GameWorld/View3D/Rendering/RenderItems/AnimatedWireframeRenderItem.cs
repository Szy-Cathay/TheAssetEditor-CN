using GameWorld.Core.Animation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.RenderItems
{
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
        IndexBuffer? _lineIndexBuffer;

        public float DepthBias { get; set; } = 0.00002f;
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
                _lineIndices.Length == 0)
            {
                return;
            }

            EnsureIndexBuffer(device);
            var effect = _resourceLibrary.GetStaticEffect(
                ShaderTypes.AnimatedSelection);
            effect.CurrentTechnique =
                effect.Techniques[
                    _pose.ApplyAnimation
                        ? "AnimatedSelection"
                        : "StaticSelection"];
            effect.Parameters["World"].SetValue(
                _pose.WorldTransform);
            effect.Parameters["View"].SetValue(parameters.View);
            effect.Parameters["Projection"].SetValue(
                parameters.Projection);
            effect.Parameters["SelectionColour"].SetValue(
                _colour);
            effect.Parameters["SelectionDepthBias"].SetValue(
                DepthBias);
            effect.Parameters["CapabilityFlag_ApplyAnimation"]
                .SetValue(_pose.ApplyAnimation);
            effect.Parameters["Animation_WeightCount"]
                .SetValue(_pose.AnimationWeightCount);
            if (_pose.ApplyAnimation)
            {
                effect.Parameters["Animation_Tranforms"]
                    .SetValue(_pose.AnimationTransforms);
            }

            effect.CurrentTechnique.Passes[0].Apply();
            var graphicsGeometry =
                _pose.Geometry.GetGeometryContext();
            device.Indices = _lineIndexBuffer;
            device.SetVertexBuffer(
                graphicsGeometry.VertexBuffer);
            device.DrawIndexedPrimitives(
                PrimitiveType.LineList,
                0,
                0,
                EdgePrimitiveCount);
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
            _lineIndexBuffer?.Dispose();
            _lineIndexBuffer = null;
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
            _lineIndexBuffer?.Dispose();
            _lineIndexBuffer = null;
        }

        void EnsureIndexBuffer(GraphicsDevice device)
        {
            if (_lineIndexBuffer != null)
                return;

            _lineIndexBuffer = new IndexBuffer(
                device,
                IndexElementSize.SixteenBits,
                _lineIndices.Length,
                BufferUsage.WriteOnly);
            _lineIndexBuffer.SetData(_lineIndices);
            IndexBufferBuildCount++;
        }

        public void Dispose()
        {
            _lineIndexBuffer?.Dispose();
            _lineIndexBuffer = null;
        }
    }
}
