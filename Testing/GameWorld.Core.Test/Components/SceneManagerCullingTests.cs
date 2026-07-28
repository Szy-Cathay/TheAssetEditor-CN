using System.Reflection;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Moq;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace Testing.GameWorld.Core.Components
{
    [TestFixture]
    public class SceneManagerCullingTests
    {
        [Test]
        public void AnimatedSkinnedMesh_OutsideBindPoseBounds_IsRenderedConservatively()
        {
            var meshNode = CreateMeshNode(
                UiVertexFormat.Weighted,
                animationEnabled: true);

            var isVisible = IsVisibleInFrustum(meshNode);

            Assert.That(isVisible, Is.True);
        }

        [Test]
        public void StaticMesh_OutsideFrustum_RemainsCulled()
        {
            var meshNode = CreateMeshNode(
                UiVertexFormat.Static,
                animationEnabled: false);

            var isVisible = IsVisibleInFrustum(meshNode);

            Assert.That(isVisible, Is.False);
        }

        [Test]
        public void DisabledSkinnedMesh_OutsideFrustum_RemainsCulled()
        {
            var meshNode = CreateMeshNode(
                UiVertexFormat.Weighted,
                animationEnabled: false);

            var isVisible = IsVisibleInFrustum(meshNode);

            Assert.That(isVisible, Is.False);
        }

        [Test]
        public void AnimatedStaticMeshWithoutAttachment_OutsideFrustum_RemainsCulled()
        {
            var meshNode = CreateMeshNode(
                UiVertexFormat.Static,
                animationEnabled: true);

            var isVisible = IsVisibleInFrustum(meshNode);

            Assert.That(isVisible, Is.False);
        }

        [Test]
        public void AnimatedBoneAttachment_OutsideBindPoseBounds_IsRenderedConservatively()
        {
            var meshNode = CreateMeshNode(
                UiVertexFormat.Static,
                animationEnabled: true);
            meshNode.AttachmentBoneResolver =
                new SkeletonBoneAnimationResolver(
                    Mock.Of<ISkeletonProvider>(),
                    0);

            var isVisible = IsVisibleInFrustum(meshNode);

            Assert.That(isVisible, Is.True);
        }

        static Rmv2MeshNode CreateMeshNode(
            UiVertexFormat vertexFormat,
            bool animationEnabled)
        {
            var geometry = new MeshObject(
                Mock.Of<IGraphicsCardGeometry>(),
                "test_skeleton")
            {
                VertexArray = [],
                IndexArray = []
            };
            geometry.ChangeVertexType(vertexFormat, updateMesh: false);
            geometry.SetBoundingBox(new BoundingBox(
                new Vector3(100, -1, -1),
                new Vector3(102, 1, 1)));

            var material = new Mock<IRmvMaterial>();
            material
                .SetupGet(x => x.ModelName)
                .Returns("test_mesh");

            var animationPlayer = new AnimationPlayer
            {
                IsEnabled = animationEnabled
            };

            return new Rmv2MeshNode(
                geometry,
                material.Object,
                null!,
                animationPlayer);
        }

        static bool IsVisibleInFrustum(Rmv2MeshNode meshNode)
        {
            var view = Matrix.CreateLookAt(
                new Vector3(0, 0, 10),
                Vector3.Zero,
                Vector3.Up);
            var projection = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.PiOver4,
                1,
                0.1f,
                100);
            var frustum = new BoundingFrustum(view * projection);
            var method = typeof(SceneManager).GetMethod(
                "IsVisibleInFrustum",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            return (bool)method.Invoke(
                null,
                [meshNode, Matrix.Identity, frustum])!;
        }
    }
}
