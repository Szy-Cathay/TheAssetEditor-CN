using Editors.KitbasherEditor.ChildEditors.PinTool;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using Microsoft.Xna.Framework;
using Moq;
using Shared.GameFormats.RigidModel;

namespace Test.KitbashEditor
{
    [TestFixture]
    public class RegiggingHelperTests
    {
        [Test]
        public void FindClosestWeights_AggregatesWeightsByBoneAcrossTriangleVertices()
        {
            var mesh = CreateTriangleMesh();

            var result = RegiggingHelper.FindClosestUV(
                new Vector3(1f / 3f, 1f / 3f, 0),
                mesh,
                Vector3.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(result.Bones, Is.EqualTo(new Vector4(1, 2, 3, 0)));
                Assert.That(result.BlendWeights.X, Is.EqualTo(1f / 3f).Within(0.0001f));
                Assert.That(result.BlendWeights.Y, Is.EqualTo(1f / 3f).Within(0.0001f));
                Assert.That(result.BlendWeights.Z, Is.EqualTo(1f / 3f).Within(0.0001f));
                Assert.That(result.BlendWeights.W, Is.Zero);
            });
        }

        [Test]
        public void FindClosestWeights_UsesTheSameWorldSpaceForDistanceAndBarycentricCoordinates()
        {
            var mesh = CreateTriangleMesh();
            var sourcePosition = new Vector3(10, 3, -2);
            var targetPosition = sourcePosition + new Vector3(1f / 3f, 1f / 3f, 0);

            var result = RegiggingHelper.FindClosestUV(targetPosition, mesh, sourcePosition);

            Assert.Multiple(() =>
            {
                Assert.That(result.Position, Is.EqualTo(targetPosition));
                Assert.That(result.Bones, Is.EqualTo(new Vector4(1, 2, 3, 0)));
            });
        }

        static MeshObject CreateTriangleMesh()
        {
            var graphicsContext = new Mock<IGraphicsCardGeometry>();
            var mesh = new MeshObject(graphicsContext.Object, "skeleton")
            {
                VertexArray =
                [
                    CreateVertex(new Vector3(0, 0, 0), 1),
                    CreateVertex(new Vector3(1, 0, 0), 2),
                    CreateVertex(new Vector3(0, 1, 0), 3)
                ],
                IndexArray = [0, 1, 2]
            };
            mesh.ChangeVertexType(UiVertexFormat.Cinematic, false);
            return mesh;
        }

        static VertexPositionNormalTextureCustom CreateVertex(Vector3 position, int boneIndex)
        {
            return new VertexPositionNormalTextureCustom
            {
                Position = new Vector4(position, 1),
                BlendIndices = new Vector4(boneIndex, 0, 0, 0),
                BlendWeights = new Vector4(1, 0, 0, 0)
            };
        }
    }
}
