using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Test.TestUtility
{
    public sealed record PartialUpload(int StartIndex, int Count);

    public class TestGeometryGraphicsContextFactory :
        IGeometryGraphicsContextFactory
    {
        private readonly List<TestGraphicsCardGeometry>
            _createdContexts = [];

        public IReadOnlyList<TestGraphicsCardGeometry> CreatedContexts =>
            _createdContexts;

        public IGraphicsCardGeometry Create()
        {
            var context = new TestGraphicsCardGeometry();
            _createdContexts.Add(context);
            return context;
        }
    }

    public class TestGraphicsCardGeometry : IGraphicsCardGeometry
    {
        private readonly List<PartialUpload> _partialUploads = [];

        public IndexBuffer IndexBuffer { get; }
        public VertexBuffer VertexBuffer { get; }
        public int IndexBufferRebuildCount { get; private set; }
        public int VertexBufferRebuildCount { get; private set; }
        public int PartialVertexBufferRebuildCount { get; private set; }
        public VertexPositionNormalTextureCustom[] UploadedVertexArray
        {
            get;
            private set;
        }
        public IReadOnlyList<PartialUpload> PartialUploads =>
            _partialUploads;

        public void RebuildIndexBuffer(ushort[] indexList) =>
            IndexBufferRebuildCount++;

        public void RebuildVertexBuffer(
            VertexPositionNormalTextureCustom[] vertexArray,
            VertexDeclaration vertexDeclaration)
        {
            VertexBufferRebuildCount++;
            UploadedVertexArray = vertexArray.ToArray();
        }

        public void RebuildVertexBufferPartial(
            VertexPositionNormalTextureCustom[] vertexArray,
            int startIndex,
            int count,
            VertexDeclaration vertexDeclaration,
            int vertexStride)
        {
            PartialVertexBufferRebuildCount++;
            _partialUploads.Add(new PartialUpload(startIndex, count));
            UploadedVertexArray ??= vertexArray.ToArray();
            Array.Copy(
                vertexArray,
                startIndex,
                UploadedVertexArray,
                startIndex,
                count);
        }

        public void ResetRebuildCounts()
        {
            IndexBufferRebuildCount = 0;
            VertexBufferRebuildCount = 0;
            PartialVertexBufferRebuildCount = 0;
            _partialUploads.Clear();
        }

        public IGraphicsCardGeometry Clone() => this;

        public void Dispose()
        {
        }
    }
}
