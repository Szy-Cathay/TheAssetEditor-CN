using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Test.TestUtility
{
    public class TestGeometryGraphicsContextFactory : IGeometryGraphicsContextFactory
    {
        public IGraphicsCardGeometry Create() => new TestGraphicsCardGeometry();
    }

    public class TestGraphicsCardGeometry : IGraphicsCardGeometry
    {
        public IndexBuffer IndexBuffer { get; }
        public VertexBuffer VertexBuffer { get; }
        public int IndexBufferRebuildCount { get; private set; }
        public int VertexBufferRebuildCount { get; private set; }
        public int PartialVertexBufferRebuildCount { get; private set; }
        public VertexPositionNormalTextureCustom[] UploadedVertexArray { get; private set; }

        public void RebuildIndexBuffer(ushort[] indexList)
        {
            IndexBufferRebuildCount++;
        }
        public void RebuildVertexBuffer(VertexPositionNormalTextureCustom[] vertArray, VertexDeclaration vertexDeclaration)
        {
            VertexBufferRebuildCount++;
            UploadedVertexArray = vertArray.ToArray();
        }
        public void RebuildVertexBufferPartial(VertexPositionNormalTextureCustom[] vertArray, int startIndex, int count, VertexDeclaration vertexDeclaration, int vertexStride)
        {
            PartialVertexBufferRebuildCount++;
            UploadedVertexArray ??= vertArray.ToArray();
            Array.Copy(vertArray, startIndex, UploadedVertexArray, startIndex, count);
        }

        public void ResetRebuildCounts()
        {
            IndexBufferRebuildCount = 0;
            VertexBufferRebuildCount = 0;
            PartialVertexBufferRebuildCount = 0;
        }

        public IGraphicsCardGeometry Clone() { return this; }
        public void Dispose()
        { }
    }
}
