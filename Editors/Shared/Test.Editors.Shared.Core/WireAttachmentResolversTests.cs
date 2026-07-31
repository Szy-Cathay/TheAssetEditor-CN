using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Moq;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.Editors.Shared.Core
{
    // Covers SceneObjectEditor.WireAttachmentResolvers: the mechanism that points variantmesh
    // attach_point props and RMV2 rigid-bone pieces at a bone on the owning model's own skeleton.
    // Attachments are a recurring source of bugs (wrong bone, stale resolver after a skeleton
    // swap, silent no-op when the name/index doesn't resolve), so each of those failure modes is
    // asserted directly against the resolved bone's own world transform rather than just checking
    // "a resolver got created".
    [TestFixture]
    public class WireAttachmentResolversTests
    {
        [Test]
        public void AttachmentPointName_MatchingBone_ResolvesToThatBonesTransform()
        {
            var sceneObject = new SceneObject("test");
            sceneObject.Skeleton = CreateSkeleton(("root", Vector3.Zero), ("hand_l", new Vector3(1, 2, 3)), ("hand_r", new Vector3(4, 5, 6)));
            var mesh = CreateMeshNode(attachmentPointName: "hand_r");
            sceneObject.ModelNode = mesh;

            SceneObjectEditor.WireAttachmentResolvers(sceneObject);

            Assert.That(mesh.AttachmentBoneResolver, Is.Not.Null);
            Assert.That(mesh.AttachmentBoneResolver!.GetWorldTransformIfAnimating().Translation, Is.EqualTo(new Vector3(4, 5, 6)));
        }

        [Test]
        public void AttachmentPointName_NoMatchingBone_ResolverIsNull()
        {
            var sceneObject = new SceneObject("test");
            sceneObject.Skeleton = CreateSkeleton(("root", Vector3.Zero), ("hand_l", new Vector3(1, 2, 3)));
            var mesh = CreateMeshNode(attachmentPointName: "does_not_exist");
            sceneObject.ModelNode = mesh;

            SceneObjectEditor.WireAttachmentResolvers(sceneObject);

            Assert.That(mesh.AttachmentBoneResolver, Is.Null);
        }

        [Test]
        public void AttachmentPointName_NoSkeleton_ResolverIsNull()
        {
            var sceneObject = new SceneObject("test");
            sceneObject.Skeleton = null!;
            var mesh = CreateMeshNode(attachmentPointName: "hand_l");
            sceneObject.ModelNode = mesh;

            SceneObjectEditor.WireAttachmentResolvers(sceneObject);

            Assert.That(mesh.AttachmentBoneResolver, Is.Null);
        }

        [Test]
        public void AnimationMatrixOverride_InRange_ResolvesToThatBonesTransform()
        {
            var sceneObject = new SceneObject("test");
            sceneObject.Skeleton = CreateSkeleton(("root", Vector3.Zero), ("piece_01", new Vector3(7, 8, 9)));
            var mesh = CreateMeshNode(animationMatrixOverride: 1);
            sceneObject.ModelNode = mesh;

            SceneObjectEditor.WireAttachmentResolvers(sceneObject);

            Assert.That(mesh.AttachmentBoneResolver, Is.Not.Null);
            Assert.That(mesh.AttachmentBoneResolver!.GetWorldTransformIfAnimating().Translation, Is.EqualTo(new Vector3(7, 8, 9)));
        }

        [Test]
        public void AnimationMatrixOverride_OutOfRange_ResolverIsNull()
        {
            var sceneObject = new SceneObject("test");
            sceneObject.Skeleton = CreateSkeleton(("root", Vector3.Zero));
            var mesh = CreateMeshNode(animationMatrixOverride: 5);
            sceneObject.ModelNode = mesh;

            SceneObjectEditor.WireAttachmentResolvers(sceneObject);

            Assert.That(mesh.AttachmentBoneResolver, Is.Null);
        }

        [Test]
        public void AnimationMatrixOverride_NoSkeleton_ResolverIsNull()
        {
            var sceneObject = new SceneObject("test");
            sceneObject.Skeleton = null!;
            var mesh = CreateMeshNode(animationMatrixOverride: 0);
            sceneObject.ModelNode = mesh;

            SceneObjectEditor.WireAttachmentResolvers(sceneObject);

            Assert.That(mesh.AttachmentBoneResolver, Is.Null);
        }

        [Test]
        public void PlainMesh_NoAttachmentPointOrMatrixOverride_ResolverStaysNull()
        {
            var sceneObject = new SceneObject("test");
            sceneObject.Skeleton = CreateSkeleton(("root", Vector3.Zero));
            var mesh = CreateMeshNode();
            sceneObject.ModelNode = mesh;

            SceneObjectEditor.WireAttachmentResolvers(sceneObject);

            Assert.That(mesh.AttachmentBoneResolver, Is.Null);
        }

        [Test]
        public void NoModelNode_DoesNotThrow()
        {
            var sceneObject = new SceneObject("test");
            sceneObject.Skeleton = CreateSkeleton(("root", Vector3.Zero));
            sceneObject.ModelNode = null!;

            Assert.DoesNotThrow(() => SceneObjectEditor.WireAttachmentResolvers(sceneObject));
        }

        [Test]
        public void NestedMeshesUnderGroupNode_AreBothWiredToTheirOwnBone()
        {
            var sceneObject = new SceneObject("test");
            sceneObject.Skeleton = CreateSkeleton(("root", Vector3.Zero), ("bone_a", new Vector3(1, 0, 0)), ("bone_b", new Vector3(0, 1, 0)));

            var group = new GroupNode("group");
            var meshA = CreateMeshNode(attachmentPointName: "bone_a");
            var meshB = CreateMeshNode(attachmentPointName: "bone_b");
            group.AddObject(meshA);
            group.AddObject(meshB);
            sceneObject.ModelNode = group;

            SceneObjectEditor.WireAttachmentResolvers(sceneObject);

            Assert.That(meshA.AttachmentBoneResolver!.GetWorldTransformIfAnimating().Translation, Is.EqualTo(new Vector3(1, 0, 0)));
            Assert.That(meshB.AttachmentBoneResolver!.GetWorldTransformIfAnimating().Translation, Is.EqualTo(new Vector3(0, 1, 0)));
        }

        [Test]
        public void CalledAgainAfterSkeletonSwap_ReResolvesAgainstTheNewSkeleton()
        {
            var sceneObject = new SceneObject("test");
            var mesh = CreateMeshNode(attachmentPointName: "hand_l");
            sceneObject.ModelNode = mesh;

            sceneObject.Skeleton = CreateSkeleton(("root", Vector3.Zero), ("hand_l", new Vector3(1, 0, 0)));
            SceneObjectEditor.WireAttachmentResolvers(sceneObject);
            Assert.That(mesh.AttachmentBoneResolver!.GetWorldTransformIfAnimating().Translation, Is.EqualTo(new Vector3(1, 0, 0)));

            // Same attach-point name, but now at a different index with a different position -
            // e.g. an ad-hoc "building" skeleton created lazily after the mesh already loaded.
            sceneObject.Skeleton = CreateSkeleton(("root", Vector3.Zero), ("other", Vector3.Zero), ("hand_l", new Vector3(9, 9, 9)));
            SceneObjectEditor.WireAttachmentResolvers(sceneObject);
            Assert.That(mesh.AttachmentBoneResolver!.GetWorldTransformIfAnimating().Translation, Is.EqualTo(new Vector3(9, 9, 9)));
        }

        private static Rmv2MeshNode CreateMeshNode(string attachmentPointName = "", int animationMatrixOverride = -1)
        {
            var materialMock = new Mock<IRmvMaterial>();
            materialMock.Setup(m => m.ModelName).Returns("test_mesh");
            materialMock.Setup(m => m.PivotPoint).Returns(Vector3.Zero);

            var mesh = new Rmv2MeshNode(null!, materialMock.Object, null!, null!)
            {
                AttachmentPointName = attachmentPointName,
                AnimationMatrixOverride = animationMatrixOverride
            };
            return mesh;
        }

        private static GameSkeleton CreateSkeleton(params (string Name, Vector3 Translation)[] bones)
        {
            var skeletonFile = new AnimationFile();
            skeletonFile.Header.SkeletonName = "test_skeleton";
            skeletonFile.Bones = bones
                .Select((b, i) => new AnimationFile.BoneInfo { Id = i, Name = b.Name, ParentId = i == 0 ? AnimationFile.BoneIndexNoParent : 0 })
                .ToArray();

            var frame = new AnimationFile.Frame();
            foreach (var bone in bones)
            {
                frame.Transforms.Add(new RmvVector3(bone.Translation));
                frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }

            var animationPart = new AnimationFile.AnimationPart();
            animationPart.DynamicFrames.Add(frame);
            skeletonFile.AnimationParts.Add(animationPart);

            return new GameSkeleton(skeletonFile, null!);
        }
    }
}
