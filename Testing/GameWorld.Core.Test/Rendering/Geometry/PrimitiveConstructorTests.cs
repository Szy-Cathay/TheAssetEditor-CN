using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Test.TestUtility;
using Microsoft.Xna.Framework;
using Shared.GameFormats.RigidModel;

namespace Test.GameWorld.Core.Rendering.Geometry
{
    [TestFixture]
    public class PrimitiveConstructorTests
    {
        [Test]
        public void CreateBox_DefaultSettings_CreatesValidOutwardGeometry()
        {
            var (constructor, factory) = CreateConstructor();

            var mesh = constructor.CreateBox(UiVertexFormat.Static, string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(mesh.VertexArray, Has.Length.EqualTo(726));
                Assert.That(mesh.IndexArray, Has.Length.EqualTo(3600));
                Assert.That(mesh.BoundingBox.Min, Is.EqualTo(new Vector3(-0.5f)));
                Assert.That(mesh.BoundingBox.Max, Is.EqualTo(new Vector3(0.5f)));
            });
            AssertValidGeometry(mesh, factory.CreatedContexts.Single());
            AssertBoxFacesOutward(mesh);
        }

        [Test]
        public void CreatePlane_DefaultSettings_CreatesValidUpwardGeometry()
        {
            var (constructor, factory) = CreateConstructor();

            var mesh = constructor.CreatePlane(UiVertexFormat.Static, string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(mesh.VertexArray, Has.Length.EqualTo(121));
                Assert.That(mesh.IndexArray, Has.Length.EqualTo(600));
                Assert.That(mesh.VertexArray.All(x => x.Position.Y == 0), Is.True);
                Assert.That(mesh.VertexArray.All(x => x.Normal == Vector3.Up), Is.True);
            });
            AssertValidGeometry(mesh, factory.CreatedContexts.Single());
        }

        [Test]
        public void CreateSphere_DefaultSettings_CreatesOnlyValidOutwardTriangles()
        {
            var (constructor, factory) = CreateConstructor();

            var mesh = constructor.CreateSphere(UiVertexFormat.Static, string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(mesh.VertexArray, Has.Length.EqualTo(231));
                Assert.That(mesh.IndexArray, Has.Length.EqualTo(1080));
                Assert.That(mesh.IndexArray.Length / 3, Is.EqualTo(360));
                Assert.That(mesh.BoundingBox.Min.X, Is.EqualTo(-0.5f).Within(0.00001f));
                Assert.That(mesh.BoundingBox.Min.Y, Is.EqualTo(-0.5f).Within(0.00001f));
                Assert.That(mesh.BoundingBox.Min.Z, Is.EqualTo(-0.5f).Within(0.00001f));
                Assert.That(mesh.BoundingBox.Max.X, Is.EqualTo(0.5f).Within(0.00001f));
                Assert.That(mesh.BoundingBox.Max.Y, Is.EqualTo(0.5f).Within(0.00001f));
                Assert.That(mesh.BoundingBox.Max.Z, Is.EqualTo(0.5f).Within(0.00001f));
            });
            AssertValidGeometry(mesh, factory.CreatedContexts.Single());
        }

        [TestCase(UiVertexFormat.Static, 0f)]
        [TestCase(UiVertexFormat.Weighted, 1f)]
        [TestCase(UiVertexFormat.Cinematic, 1f)]
        public void CreatePlane_AssignsExpectedRootBoneWeight(
            UiVertexFormat vertexFormat,
            float expectedRootWeight)
        {
            var (constructor, _) = CreateConstructor();

            var mesh = constructor.CreatePlane(vertexFormat, "test_skeleton", resolution: 1);

            Assert.Multiple(() =>
            {
                Assert.That(mesh.VertexFormat, Is.EqualTo(vertexFormat));
                Assert.That(mesh.VertexArray.All(x => x.BlendIndices == Vector4.Zero), Is.True);
                Assert.That(mesh.VertexArray.All(x => x.BlendWeights.X == expectedRootWeight), Is.True);
                Assert.That(mesh.VertexArray.All(x => x.BlendWeights.Y == 0), Is.True);
                Assert.That(mesh.VertexArray.All(x => x.BlendWeights.Z == 0), Is.True);
                Assert.That(mesh.VertexArray.All(x => x.BlendWeights.W == 0), Is.True);
            });
        }

        [Test]
        public void CreatePrimitives_ResolutionBeyondUShortCapacity_Throws()
        {
            var (constructor, _) = CreateConstructor();

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => constructor.CreateBox(UiVertexFormat.Static, string.Empty, resolution: 104));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => constructor.CreatePlane(UiVertexFormat.Static, string.Empty, resolution: 256));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => constructor.CreateSphere(UiVertexFormat.Static, string.Empty, resolution: 181));
            });
        }

        [Test]
        public void CreatePrimitives_InvalidDimensions_Throw()
        {
            var (constructor, _) = CreateConstructor();

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => constructor.CreateBox(UiVertexFormat.Static, string.Empty, size: 0));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => constructor.CreatePlane(UiVertexFormat.Static, string.Empty, size: -1));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => constructor.CreateSphere(UiVertexFormat.Static, string.Empty, radius: float.NaN));
            });
        }

        private static (
            PrimitiveConstructor Constructor,
            TestGeometryGraphicsContextFactory Factory) CreateConstructor()
        {
            var factory = new TestGeometryGraphicsContextFactory();
            return (new PrimitiveConstructor(factory), factory);
        }

        private static void AssertValidGeometry(
            MeshObject mesh,
            TestGraphicsCardGeometry graphicsContext)
        {
            Assert.Multiple(() =>
            {
                Assert.That(mesh.IndexArray.All(x => x < mesh.VertexArray.Length), Is.True);
                Assert.That(mesh.VertexArray.All(IsFinite), Is.True);
                Assert.That(mesh.VertexArray.All(x => IsNormalized(x.Normal)), Is.True);
                Assert.That(mesh.VertexArray.All(x => IsNormalized(x.Tangent)), Is.True);
                Assert.That(mesh.VertexArray.All(x => IsNormalized(x.BiNormal)), Is.True);
                Assert.That(
                    mesh.VertexArray.All(x =>
                        x.TextureCoordinate.X is >= 0 and <= 1 &&
                        x.TextureCoordinate.Y is >= 0 and <= 1),
                    Is.True);
                Assert.That(graphicsContext.VertexBufferRebuildCount, Is.EqualTo(1));
                Assert.That(graphicsContext.IndexBufferRebuildCount, Is.EqualTo(1));
                Assert.That(graphicsContext.UploadedVertexArray, Is.EqualTo(mesh.VertexArray));
            });

            for (var index = 0; index < mesh.IndexArray.Length; index += 3)
            {
                var first = mesh.VertexArray[mesh.IndexArray[index]];
                var second = mesh.VertexArray[mesh.IndexArray[index + 1]];
                var third = mesh.VertexArray[mesh.IndexArray[index + 2]];
                var edgeA = second.Position3() - first.Position3();
                var edgeB = third.Position3() - first.Position3();
                var triangleNormal = Vector3.Cross(edgeA, edgeB);
                var expectedNormal = first.Normal + second.Normal + third.Normal;

                Assert.Multiple(() =>
                {
                    Assert.That(
                        triangleNormal.LengthSquared(),
                        Is.GreaterThan(0.00000001f),
                        $"Triangle {index / 3} is degenerate.");
                    Assert.That(
                        Vector3.Dot(triangleNormal, expectedNormal),
                        Is.GreaterThan(0),
                        $"Triangle {index / 3} faces inward.");
                });
            }
        }

        private static void AssertBoxFacesOutward(MeshObject mesh)
        {
            Assert.That(
                mesh.VertexArray.All(x => Vector3.Dot(x.Position3(), x.Normal) > 0),
                Is.True);

            for (var index = 0; index < mesh.IndexArray.Length; index += 3)
            {
                var first = mesh.VertexArray[mesh.IndexArray[index]].Position3();
                var second = mesh.VertexArray[mesh.IndexArray[index + 1]].Position3();
                var third = mesh.VertexArray[mesh.IndexArray[index + 2]].Position3();
                var triangleNormal = Vector3.Cross(second - first, third - first);
                var triangleCenter = (first + second + third) / 3f;

                Assert.That(
                    Vector3.Dot(triangleNormal, triangleCenter),
                    Is.GreaterThan(0),
                    $"Box triangle {index / 3} faces inward.");
            }
        }

        private static bool IsFinite(VertexPositionNormalTextureCustom vertex)
        {
            return IsFinite(vertex.Position) &&
                   IsFinite(vertex.Normal) &&
                   IsFinite(vertex.Tangent) &&
                   IsFinite(vertex.BiNormal) &&
                   IsFinite(vertex.TextureCoordinate);
        }

        private static bool IsFinite(Vector2 value)
        {
            return float.IsFinite(value.X) && float.IsFinite(value.Y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.X) &&
                   float.IsFinite(value.Y) &&
                   float.IsFinite(value.Z);
        }

        private static bool IsFinite(Vector4 value)
        {
            return float.IsFinite(value.X) &&
                   float.IsFinite(value.Y) &&
                   float.IsFinite(value.Z) &&
                   float.IsFinite(value.W);
        }

        private static bool IsNormalized(Vector3 value)
        {
            return MathF.Abs(value.LengthSquared() - 1f) < 0.00001f;
        }
    }
}
