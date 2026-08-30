using GameWorld.Core.Animation;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Moq;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;

namespace Testing.GameWorld.Core.Utility
{
    [TestFixture]
    internal class SkeletonBoneAnimationResolverTests
    {
        #region Test helpers

        /// <summary>
        /// Creates a minimal GameSkeleton with known bone positions for testing.
        /// Root bone (index 0) at origin, child bones offset on X axis.
        /// </summary>
        static GameSkeleton CreateTestSkeleton(AnimationPlayer player, int boneCount = 2)
        {
            var animFile = new AnimationFile();
            animFile.Header = new AnimationFile.AnimationHeader { SkeletonName = "TestSkeleton" };
            animFile.Bones = new AnimationFile.BoneInfo[boneCount];

            for (var i = 0; i < boneCount; i++)
            {
                animFile.Bones[i] = new AnimationFile.BoneInfo
                {
                    Name = $"bone_{i}",
                    ParentId = i == 0 ? -1 : 0
                };
            }

            var frame = new AnimationFile.Frame();
            for (var i = 0; i < boneCount; i++)
            {
                frame.Transforms.Add(new RmvVector3 { X = i * 10.0f, Y = 0, Z = 0 });
                frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }

            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            animFile.AnimationParts.Add(part);

            return new GameSkeleton(animFile, player);
        }

        class TestSkeletonProvider : ISkeletonProvider
        {
            public GameSkeleton Skeleton { get; }
            public TestSkeletonProvider(GameSkeleton skeleton) => Skeleton = skeleton;
        }

        static GameSkeleton CreateParentedSkeleton(
            AnimationPlayer player,
            Vector3 rootTranslation,
            Quaternion rootRotation,
            Vector3 childTranslation,
            Quaternion childRotation)
        {
            var animFile = new AnimationFile
            {
                Header = new AnimationFile.AnimationHeader
                {
                    SkeletonName = "ParentedTestSkeleton"
                },
                Bones =
                [
                    new AnimationFile.BoneInfo
                    {
                        Name = "root",
                        ParentId = -1
                    },
                    new AnimationFile.BoneInfo
                    {
                        Name = "attachment",
                        ParentId = 0
                    }
                ]
            };

            var frame = new AnimationFile.Frame();
            frame.Transforms.Add(new RmvVector3
            {
                X = rootTranslation.X,
                Y = rootTranslation.Y,
                Z = rootTranslation.Z
            });
            frame.Transforms.Add(new RmvVector3
            {
                X = childTranslation.X,
                Y = childTranslation.Y,
                Z = childTranslation.Z
            });
            frame.Quaternion.Add(new RmvVector4(
                rootRotation.X,
                rootRotation.Y,
                rootRotation.Z,
                rootRotation.W));
            frame.Quaternion.Add(new RmvVector4(
                childRotation.X,
                childRotation.Y,
                childRotation.Z,
                childRotation.W));

            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            animFile.AnimationParts.Add(part);

            return new GameSkeleton(animFile, player);
        }

        static void AssertMatrixEqual(
            Matrix expected,
            Matrix actual,
            string context)
        {
            var expectedElements = new[]
            {
                expected.M11, expected.M12, expected.M13, expected.M14,
                expected.M21, expected.M22, expected.M23, expected.M24,
                expected.M31, expected.M32, expected.M33, expected.M34,
                expected.M41, expected.M42, expected.M43, expected.M44
            };
            var actualElements = new[]
            {
                actual.M11, actual.M12, actual.M13, actual.M14,
                actual.M21, actual.M22, actual.M23, actual.M24,
                actual.M31, actual.M32, actual.M33, actual.M34,
                actual.M41, actual.M42, actual.M43, actual.M44
            };

            Assert.Multiple(() =>
            {
                for (var i = 0; i < expectedElements.Length; i++)
                {
                    Assert.That(
                        actualElements[i],
                        Is.EqualTo(expectedElements[i]).Within(0.001f),
                        $"{context}, matrix element {i}");
                }
            });
        }

        #endregion

        #region Attachment transform contract

        [TestCase(
            false,
            TestName = "AttachmentResolver_ComposesCompleteAnimatedBoneMatrix_WhenPaused")]
        [TestCase(
            true,
            TestName = "AttachmentResolver_ComposesCompleteAnimatedBoneMatrix_WhenPlaying")]
        public void AttachmentResolver_ComposesCompleteAnimatedBoneMatrix(
            bool isPlaying)
        {
            var bindRootTranslation = new Vector3(2, -1, 3);
            var bindRootRotation = Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                MathHelper.PiOver4);
            var bindChildTranslation = new Vector3(-2, 4, 1);
            var bindChildRotation = Quaternion.CreateFromAxisAngle(
                Vector3.UnitX,
                -MathHelper.PiOver4);
            var player = new AnimationPlayer();
            var skeleton = CreateParentedSkeleton(
                player,
                bindRootTranslation,
                bindRootRotation,
                bindChildTranslation,
                bindChildRotation);

            var animatedRootTranslation = new Vector3(7, -3, 2);
            var animatedRootRotation = Quaternion.CreateFromAxisAngle(
                Vector3.UnitZ,
                MathHelper.PiOver4);
            var animatedRootScale = new Vector3(1.2f, 0.8f, 1.1f);
            var animatedChildTranslation = new Vector3(-1, 5, -2);
            var animatedChildRotation = Quaternion.CreateFromAxisAngle(
                Vector3.UnitX,
                MathHelper.PiOver2);
            var animatedChildScale = new Vector3(0.75f, 1.25f, 0.9f);
            var clip = new AnimationClip();
            clip.DynamicFrames.Add(new AnimationClip.KeyFrame
            {
                Position =
                [
                    animatedRootTranslation,
                    animatedChildTranslation
                ],
                Rotation =
                [
                    animatedRootRotation,
                    animatedChildRotation
                ],
                Scale =
                [
                    animatedRootScale,
                    animatedChildScale
                ]
            });
            clip.Duration = TimeSpan.FromSeconds(1);

            player.SetAnimation(clip, skeleton);
            if (isPlaying)
                player.Play();
            else
            {
                player.IsEnabled = true;
                player.Pause();
            }
            player.Refresh();

            var animatedRootMatrix =
                Matrix.CreateScale(animatedRootScale) *
                Matrix.CreateFromQuaternion(animatedRootRotation) *
                Matrix.CreateTranslation(animatedRootTranslation);
            var animatedChildMatrix =
                Matrix.CreateScale(animatedChildScale) *
                Matrix.CreateFromQuaternion(animatedChildRotation) *
                Matrix.CreateTranslation(animatedChildTranslation);
            var expectedAnimatedWorld =
                animatedChildMatrix *
                animatedRootMatrix;
            var attachmentIndex =
                skeleton.GetBoneIndexByName("attachment");
            var bindWorld =
                skeleton.GetWorldTransform(attachmentIndex);
            var expectedAnimationDelta =
                Matrix.Invert(bindWorld) *
                expectedAnimatedWorld;
            var resolver = new SkeletonBoneAnimationResolver(
                new TestSkeletonProvider(skeleton),
                attachmentIndex);

            Assert.That(player.GetCurrentAnimationFrame(), Is.Not.Null);
            Assert.That(expectedAnimatedWorld, Is.Not.EqualTo(bindWorld));
            AssertMatrixEqual(
                expectedAnimatedWorld,
                resolver.GetWorldTransformIfAnimating(),
                "Resolved attachment world transform");
            AssertMatrixEqual(
                expectedAnimationDelta,
                resolver.GetTransformIfAnimating(),
                "Resolved skinning transform");

            var pivot = new Vector3(0.5f, -0.25f, 1.5f);
            var nodeModel =
                Matrix.CreateScale(new Vector3(0.9f, 1.1f, 1.2f)) *
                Matrix.CreateRotationY(-MathHelper.PiOver4) *
                Matrix.CreateTranslation(3, 2, -4);
            var rootModel =
                Matrix.CreateRotationX(MathHelper.PiOver4) *
                Matrix.CreateTranslation(-2, 1, 3);
            var parentModel =
                Matrix.CreateScale(1.3f) *
                Matrix.CreateRotationZ(-MathHelper.PiOver4) *
                Matrix.CreateTranslation(4, -1, 2);
            var attachmentOuterWorld =
                Matrix.CreateRotationY(MathHelper.PiOver4) *
                Matrix.CreateTranslation(-3, 6, 2);
            var material = new Mock<IRmvMaterial>();
            material
                .SetupGet(value => value.ModelName)
                .Returns("attachment_test");
            material
                .SetupGet(value => value.PivotPoint)
                .Returns(pivot);
            var meshNode = new Rmv2MeshNode(
                new MeshObject(
                    Mock.Of<IGraphicsCardGeometry>(),
                    "ParentedTestSkeleton")
                {
                    VertexArray = [],
                    IndexArray = []
                },
                material.Object,
                null!,
                player)
            {
                AttachmentBoneResolver = resolver,
                AttachmentOuterWorld = attachmentOuterWorld,
                ModelMatrix = nodeModel,
                PivotPoint = pivot
            };
            var rootNode = new GroupNode("root")
            {
                ModelMatrix = rootModel
            };
            var parentNode = new GroupNode("parent")
            {
                ModelMatrix = parentModel
            };
            rootNode.AddObject(parentNode);
            parentNode.AddObject(meshNode);
            var expectedRenderWorld =
                Matrix.CreateTranslation(pivot) *
                nodeModel *
                expectedAnimatedWorld *
                attachmentOuterWorld *
                rootModel *
                parentModel;

            AssertMatrixEqual(
                expectedRenderWorld,
                meshNode.GetRenderWorldMatrix(),
                "Scene-node attachment composition");
        }

        #endregion

        #region Bug 3b: GetWorldTransformIfAnimating should work when paused

        [Test]
        public void GetWorldTransformIfAnimating_ReturnsBoneTransform_WhenEnabledButPaused()
        {
            // Arrange - Bug 3b: weapon must follow bone position even when animation is paused
            var player = new AnimationPlayer();
            var skeleton = CreateTestSkeleton(player, boneCount: 2);
            var provider = new TestSkeletonProvider(skeleton);

            player.IsEnabled = true;
            player.Pause(); // IsPlaying = false

            var boneIndex = skeleton.GetBoneIndexByName("bone_1");
            var resolver = new SkeletonBoneAnimationResolver(provider, boneIndex);

            // Act
            var result = resolver.GetWorldTransformIfAnimating();

            // Assert - bone_1 is at X=10, should NOT be Identity
            Assert.That(result, Is.Not.EqualTo(Matrix.Identity), "Paused animation should still return bone transform");
            Assert.That(result.Translation.X, Is.EqualTo(10.0f).Within(0.001f));
        }

        [Test]
        public void GetWorldTransformIfAnimating_ReturnsBoneTransform_WhenEnabledAndPlaying()
        {
            // Arrange - baseline: playing animation works
            var player = new AnimationPlayer();
            var skeleton = CreateTestSkeleton(player, boneCount: 2);
            var provider = new TestSkeletonProvider(skeleton);

            player.IsEnabled = true;
            // IsPlaying defaults to true

            var boneIndex = skeleton.GetBoneIndexByName("bone_1");
            var resolver = new SkeletonBoneAnimationResolver(provider, boneIndex);

            // Act
            var result = resolver.GetWorldTransformIfAnimating();

            // Assert
            Assert.That(result, Is.Not.EqualTo(Matrix.Identity));
            Assert.That(result.Translation.X, Is.EqualTo(10.0f).Within(0.001f));
        }

        #endregion

        #region Edge cases: conditions that should return Identity

        [Test]
        public void GetWorldTransformIfAnimating_ReturnsBindPose_WhenPlayerDisabled()
        {
            // Arrange
            var player = new AnimationPlayer();
            var skeleton = CreateTestSkeleton(player, boneCount: 2);
            var provider = new TestSkeletonProvider(skeleton);

            player.IsEnabled = false;

            var boneIndex = skeleton.GetBoneIndexByName("bone_1");
            var resolver = new SkeletonBoneAnimationResolver(provider, boneIndex);

            // Act
            var result = resolver.GetWorldTransformIfAnimating();

            // Assert
            Assert.That(result, Is.Not.EqualTo(Matrix.Identity));
            Assert.That(result.Translation.X, Is.EqualTo(10.0f).Within(0.001f));
        }

        [TestCase(-1)]
        [TestCase(-2)]
        public void GetWorldTransformIfAnimating_ReturnsIdentity_WhenBoneIndexIsNegative(int boneIndex)
        {
            // Arrange - bone not found in skeleton
            var player = new AnimationPlayer();
            var skeleton = CreateTestSkeleton(player, boneCount: 2);
            var provider = new TestSkeletonProvider(skeleton);

            player.IsEnabled = true;

            var resolver = new SkeletonBoneAnimationResolver(provider, boneIndex);

            // Act
            var result = resolver.GetWorldTransformIfAnimating();

            // Assert
            Assert.That(result, Is.EqualTo(Matrix.Identity), "Invalid bone index should return Identity");
        }

        [TestCase(2)]
        [TestCase(3)]
        public void GetWorldTransformIfAnimating_ReturnsIdentity_WhenBoneIndexIsOutsideSkeleton(int boneIndex)
        {
            var player = new AnimationPlayer
            {
                IsEnabled = true,
            };
            var skeleton = CreateTestSkeleton(player, boneCount: 2);
            var provider = new TestSkeletonProvider(skeleton);
            var resolver = new SkeletonBoneAnimationResolver(provider, boneIndex);

            Assert.That(
                resolver.GetWorldTransformIfAnimating,
                Is.EqualTo(Matrix.Identity));
        }

        [Test]
        public void GetWorldTransformIfAnimating_ReturnsIdentity_WhenSkeletonIsNull()
        {
            // Arrange
            var provider = new TestSkeletonProvider(null);
            var resolver = new SkeletonBoneAnimationResolver(provider, boneIndex: 0);

            // Act
            var result = resolver.GetWorldTransformIfAnimating();

            // Assert
            Assert.That(result, Is.EqualTo(Matrix.Identity), "Null skeleton should return Identity");
        }

        #endregion

        #region Bug 3b: GetTransformIfAnimating should also work when paused

        [Test]
        public void GetTransformIfAnimating_ReturnsBoneTransform_WhenEnabledButPaused()
        {
            // Arrange - same fix applies to GetTransformIfAnimating
            var player = new AnimationPlayer();
            var skeleton = CreateTestSkeleton(player, boneCount: 2);
            var provider = new TestSkeletonProvider(skeleton);

            player.IsEnabled = true;
            player.Pause();

            var boneIndex = skeleton.GetBoneIndexByName("bone_1");
            var resolver = new SkeletonBoneAnimationResolver(provider, boneIndex);

            // Act
            var result = resolver.GetTransformIfAnimating();

            // Assert
            Assert.That(result, Is.Not.EqualTo(Matrix.Identity), "GetTransformIfAnimating should also work when paused");
        }

        [Test]
        public void GetTransformIfAnimating_ReturnsBindPose_WhenPlayerDisabled()
        {
            var player = new AnimationPlayer
            {
                IsEnabled = false,
            };
            var skeleton = CreateTestSkeleton(player, boneCount: 2);
            var provider = new TestSkeletonProvider(skeleton);
            var resolver = new SkeletonBoneAnimationResolver(provider, boneIndex: 1);

            var result = resolver.GetTransformIfAnimating();

            Assert.That(result, Is.Not.EqualTo(Matrix.Identity));
            Assert.That(result.Translation.X, Is.EqualTo(10.0f).Within(0.001f));
        }

        [TestCase(-2)]
        [TestCase(2)]
        [TestCase(3)]
        public void GetTransformIfAnimating_ReturnsIdentity_WhenBoneIndexIsInvalid(int boneIndex)
        {
            var player = new AnimationPlayer
            {
                IsEnabled = true,
            };
            var skeleton = CreateTestSkeleton(player, boneCount: 2);
            var provider = new TestSkeletonProvider(skeleton);
            var resolver = new SkeletonBoneAnimationResolver(provider, boneIndex);

            Assert.That(
                resolver.GetTransformIfAnimating,
                Is.EqualTo(Matrix.Identity));
        }

        #endregion

        #region GetWorldTransform (unconditional) always returns transform

        [Test]
        public void GetWorldTransform_ReturnsBoneTransform_RegardlessOfPlayerState()
        {
            // Arrange - unconditional version ignores player state
            var player = new AnimationPlayer();
            var skeleton = CreateTestSkeleton(player, boneCount: 2);
            var provider = new TestSkeletonProvider(skeleton);

            player.IsEnabled = false;
            player.Pause();

            var boneIndex = skeleton.GetBoneIndexByName("bone_1");
            var resolver = new SkeletonBoneAnimationResolver(provider, boneIndex);

            // Act
            var result = resolver.GetWorldTransform();

            // Assert - unconditional version always returns bone transform
            Assert.That(result, Is.Not.EqualTo(Matrix.Identity));
            Assert.That(result.Translation.X, Is.EqualTo(10.0f).Within(0.001f));
        }

        #endregion
    }
}
