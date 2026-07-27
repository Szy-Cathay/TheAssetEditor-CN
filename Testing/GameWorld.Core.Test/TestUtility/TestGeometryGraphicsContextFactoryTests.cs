using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Test.TestUtility;

public class TestGeometryGraphicsContextFactoryTests
{
    [Test]
    public void FactoryRetainsContextsAndRecordsPartialUploads()
    {
        var factory = new TestGeometryGraphicsContextFactory();
        var context = (TestGraphicsCardGeometry)factory.Create();
        var vertices = new VertexPositionNormalTextureCustom[4];
        context.RebuildVertexBuffer(
            vertices,
            VertexPositionNormalTextureCustom.VertexDeclaration);

        context.RebuildVertexBufferPartial(
            vertices,
            startIndex: 1,
            count: 2,
            VertexPositionNormalTextureCustom.VertexDeclaration,
            VertexPositionNormalTextureCustom.VertexDeclaration.VertexStride);

        Assert.Multiple(() =>
        {
            Assert.That(factory.CreatedContexts, Is.EqualTo(new[] { context }));
            Assert.That(
                context.PartialUploads,
                Is.EqualTo(new[] { new PartialUpload(1, 2) }));
        });

        context.ResetRebuildCounts();

        Assert.Multiple(() =>
        {
            Assert.That(context.PartialUploads, Is.Empty);
            Assert.That(context.UploadedVertexArray, Is.SameAs(vertices).Or.EqualTo(vertices));
        });
    }
}
