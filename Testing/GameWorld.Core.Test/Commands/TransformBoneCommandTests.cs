using GameWorld.Core.Animation;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Testing.GameWorld.Core.Commands
{
    [TestFixture]
    public class TransformBoneCommandTests
    {
        private const float Epsilon = 0.0001f;

        [TestCase(GizmoMode.Translate)]
        [TestCase(GizmoMode.Rotate)]
        [TestCase(GizmoMode.UniformScale)]
        public void PreviewCommitUndoRedo_RestoresIsolatedFramesAndPublishesOneModifiedEvent(
            GizmoMode mode)
        {
            var context = CreateContext();
            var initialFrameReference = context.Animation.DynamicFrames[1];
            var transform = GetDelta(mode);

            context.Command.ApplyTransformation(transform);

            var committedFrameReference = context.Animation.DynamicFrames[1];
            AssertExpectedFrame(committedFrameReference, mode);
            Assert.Multiple(() =>
            {
                Assert.That(context.Command, Is.InstanceOf<IRedoableCommand>());
                Assert.That(committedFrameReference, Is.Not.SameAs(initialFrameReference));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(1));
            });

            context.Command.Execute();
            Assert.That(context.ModifiedEventCount, Is.EqualTo(1));

            initialFrameReference.Position[0] = new Vector3(91, 92, 93);
            committedFrameReference.Position[0] = new Vector3(81, 82, 83);
            committedFrameReference.Rotation[0] = Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                0.7f);
            committedFrameReference.Scale[0] = new Vector3(71, 72, 73);
            context.Selection.SelectedBones.Clear();
            context.Selection.CurrentFrame = 0;
            context.Selection.CurrentAnimation = CreateAnimation();

            context.Command.Undo();

            AssertInitialFrame(context.Animation.DynamicFrames[1]);
            Assert.Multiple(() =>
            {
                Assert.That(context.Animation.DynamicFrames[1], Is.Not.SameAs(initialFrameReference));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(2));
                Assert.That(context.Selection.ModifiedBones, Is.EqualTo(new[] { 0 }));
                Assert.That(context.LastModifiedState.CurrentFrame, Is.EqualTo(1));
                Assert.That(
                    context.LastModifiedState.CurrentAnimation,
                    Is.SameAs(context.Animation));
            });

            ((IRedoableCommand)context.Command).Redo();

            AssertExpectedFrame(context.Animation.DynamicFrames[1], mode);
            Assert.Multiple(() =>
            {
                Assert.That(context.Animation.DynamicFrames[1], Is.Not.SameAs(committedFrameReference));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(3));
            });
        }

        [Test]
        public void Configure_CapturesSelectedBonesAnimationAndFrameBeforePreview()
        {
            var context = CreateContext();
            var replacementAnimation = CreateAnimation();
            context.Selection.SelectedBones.Clear();
            context.Selection.CurrentAnimation = replacementAnimation;
            context.Selection.CurrentFrame = 0;

            context.Command.ApplyTransformation(
                CreateTranslationDelta(new Vector3(4, -2, 1)));

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Animation.DynamicFrames[1].Position[0],
                    Is.EqualTo(new Vector3(5, 0, 4)));
                AssertInitialFrame(replacementAnimation.DynamicFrames[0]);
                Assert.That(context.Selection.ModifiedBones, Is.EqualTo(new[] { 0 }));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(1));
                Assert.That(context.LastModifiedState.CurrentFrame, Is.EqualTo(1));
                Assert.That(
                    context.LastModifiedState.CurrentAnimation,
                    Is.SameAs(context.Animation));
            });
        }

        [Test]
        public void ModifiedNotificationMutation_DoesNotChangeCapturedBones()
        {
            var context = CreateContext();
            context.Command.ApplyTransformation(
                CreateTranslationDelta(new Vector3(1, 0, 0)));
            context.LastModifiedState.ModifiedBones.Clear();

            context.Command.ApplyTransformation(
                CreateTranslationDelta(new Vector3(1, 0, 0)));

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Animation.DynamicFrames[1].Position[0],
                    Is.EqualTo(new Vector3(3, 2, 3)));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(2));
                Assert.That(context.LastModifiedState.ModifiedBones, Is.EqualTo(new[] { 0 }));
            });
        }

        [Test]
        public void Execute_CapturesFinalFrameExactlyOnce()
        {
            var context = CreateContext();
            context.Command.ApplyTransformation(
                CreateTranslationDelta(new Vector3(2, 0, 0)));
            context.Command.Execute();

            context.Animation.DynamicFrames[1].Position[0] = new Vector3(50, 0, 0);
            context.Command.Execute();
            context.Command.Undo();
            ((IRedoableCommand)context.Command).Redo();

            Assert.That(
                context.Animation.DynamicFrames[1].Position[0],
                Is.EqualTo(new Vector3(3, 2, 3)));
        }

        [Test]
        public void RestoreInitialFrame_UsesFreshCloneAndResetsPreviewDelta()
        {
            var context = CreateContext();
            var initialFrameReference = context.Animation.DynamicFrames[1];
            var translation = CreateTranslationDelta(new Vector3(2, 0, 0));
            context.Command.ApplyTransformation(translation);

            context.Command.RestoreInitialFrame();

            AssertInitialFrame(context.Animation.DynamicFrames[1]);
            Assert.Multiple(() =>
            {
                Assert.That(context.Animation.DynamicFrames[1], Is.Not.SameAs(initialFrameReference));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(2));
            });

            context.Command.ApplyTransformation(translation);

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Animation.DynamicFrames[1].Position[0],
                    Is.EqualTo(new Vector3(3, 2, 3)));
                Assert.That(context.ModifiedEventCount, Is.EqualTo(3));
            });
        }

        [Test]
        public void BoneSelectionClone_SharesNotificationLineageWithIsolatedPayload()
        {
            var original = new BoneSelectionState(null)
            {
                CurrentAnimation = CreateAnimation(),
                CurrentFrame = 1,
                SelectedBones = [0]
            };
            var clone = (BoneSelectionState)original.Clone();
            var eventState = (BoneSelectionState)original.Clone();
            BoneSelectionState receivedState = null;
            clone.BoneModifiedEvent += state => receivedState = state;

            original.TriggerModifiedBoneEvent(eventState, [0]);

            Assert.Multiple(() =>
            {
                Assert.That(receivedState, Is.SameAs(eventState));
                Assert.That(receivedState, Is.Not.SameAs(original));
                Assert.That(receivedState.ModifiedBones, Is.EqualTo(new[] { 0 }));
                Assert.That(original.ModifiedBones, Is.EqualTo(new[] { 0 }));
            });
            receivedState.ModifiedBones.Clear();
            Assert.That(original.ModifiedBones, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void BoneSelectionClone_UnsubscribeRemovesOnlyThatLineageHandler()
        {
            var original = new BoneSelectionState(null);
            var clone = (BoneSelectionState)original.Clone();
            var notificationCount = 0;
            BoneModifiedEvent handler = _ => notificationCount++;
            clone.BoneModifiedEvent += handler;
            original.TriggerModifiedBoneEvent([0]);

            clone.BoneModifiedEvent -= handler;
            original.TriggerModifiedBoneEvent([0]);

            Assert.That(notificationCount, Is.EqualTo(1));
        }

        [Test]
        public void BoneSelectionClone_SubscriberExceptionStillPropagates()
        {
            var original = new BoneSelectionState(null);
            var clone = (BoneSelectionState)original.Clone();
            var expected = new InvalidOperationException("subscriber failed");
            var laterSubscriberCount = 0;
            clone.BoneModifiedEvent += _ => throw expected;
            clone.BoneModifiedEvent += _ => laterSubscriberCount++;

            var actual = Assert.Throws<InvalidOperationException>(
                () => original.TriggerModifiedBoneEvent([0]));

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(original.ModifiedBones, Is.EqualTo(new[] { 0 }));
            Assert.That(laterSubscriberCount, Is.EqualTo(1));
        }

        private static TransformContext CreateContext()
        {
            var eventHub = new Mock<IEventHub>();
            var selectionManager = new SelectionManager(eventHub.Object);
            var animation = CreateAnimation();
            var selection = new BoneSelectionState(null)
            {
                CurrentAnimation = animation,
                CurrentFrame = 1,
                SelectedBones = [0],
                Skeleton = CreateSkeleton()
            };
            var modifiedEventCount = 0;
            BoneSelectionState lastModifiedState = null;
            selection.BoneModifiedEvent += state =>
            {
                modifiedEventCount++;
                lastModifiedState = state;
            };
            selectionManager.SetState(selection);

            var command = new TransformBoneCommand(selectionManager);
            command.Configure(selection.SelectedBones, selection);
            return new TransformContext(
                command,
                selection,
                animation,
                () => modifiedEventCount,
                () => lastModifiedState);
        }

        private static AnimationClip CreateAnimation()
        {
            var animation = new AnimationClip();
            animation.DynamicFrames.Add(CreateInitialFrame());
            animation.DynamicFrames.Add(CreateInitialFrame());
            return animation;
        }

        private static AnimationClip.KeyFrame CreateInitialFrame()
        {
            return new AnimationClip.KeyFrame
            {
                Position = [new Vector3(1, 2, 3)],
                Rotation = [new Quaternion(0.38268343f, 0, 0, 0.9238795f)],
                Scale = [new Vector3(2, 3, 4)]
            };
        }

        private static BoneTransformDelta GetDelta(GizmoMode mode)
        {
            var pivot = new Vector3(1, 2, 3);
            BoneTransformDelta delta;
            bool created;
            switch (mode)
            {
                case GizmoMode.Translate:
                    created = BoneTransformDelta.TryCreateTranslation(
                        new Vector3(4, -2, 1),
                        Vector3.Zero,
                        out delta);
                    break;
                case GizmoMode.Rotate:
                    created = BoneTransformDelta.TryCreateRotation(
                        Quaternion.CreateFromAxisAngle(
                            Vector3.UnitZ,
                            MathHelper.PiOver2),
                        pivot,
                        out delta);
                    break;
                case GizmoMode.UniformScale:
                    created = BoneTransformDelta.TryCreateScale(
                        new Vector3(1.5f),
                        pivot,
                        out delta);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }

            Assert.That(created, Is.True);
            return delta;
        }

        private static BoneTransformDelta CreateTranslationDelta(Vector3 translation)
        {
            Assert.That(
                BoneTransformDelta.TryCreateTranslation(
                    translation,
                    Vector3.Zero,
                    out var delta),
                Is.True);
            return delta;
        }

        private static GameSkeleton CreateSkeleton()
        {
            var skeletonFile = new AnimationFile
            {
                Header = new AnimationFile.AnimationHeader
                {
                    SkeletonName = "TestSkeleton"
                },
                Bones =
                [
                    new AnimationFile.BoneInfo
                    {
                        Name = "root",
                        ParentId = -1
                    }
                ]
            };
            var skeletonFrame = new AnimationFile.Frame();
            skeletonFrame.Transforms.Add(new RmvVector3());
            skeletonFrame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            var skeletonPart = new AnimationFile.AnimationPart();
            skeletonPart.DynamicFrames.Add(skeletonFrame);
            skeletonFile.AnimationParts.Add(skeletonPart);
            return new GameSkeleton(skeletonFile, null);
        }

        private static void AssertInitialFrame(AnimationClip.KeyFrame frame)
        {
            Assert.Multiple(() =>
            {
                Assert.That(frame.Position[0], Is.EqualTo(new Vector3(1, 2, 3)));
                AssertQuaternionEquivalent(
                    frame.Rotation[0],
                    new Quaternion(0.38268343f, 0, 0, 0.9238795f));
                Assert.That(frame.Scale[0], Is.EqualTo(new Vector3(2, 3, 4)));
            });
        }

        private static void AssertExpectedFrame(AnimationClip.KeyFrame frame, GizmoMode mode)
        {
            var expectedPosition = mode == GizmoMode.Translate
                ? new Vector3(5, 0, 4)
                : new Vector3(1, 2, 3);
            var expectedRotation = mode == GizmoMode.Rotate
                ? new Quaternion(
                    0.27059805f,
                    0.27059805f,
                    0.6532815f,
                    0.6532815f)
                : new Quaternion(0.38268343f, 0, 0, 0.9238795f);
            var expectedScale = mode == GizmoMode.UniformScale
                ? new Vector3(3, 4.5f, 6)
                : new Vector3(2, 3, 4);

            Assert.Multiple(() =>
            {
                Assert.That(frame.Position[0], Is.EqualTo(expectedPosition));
                AssertQuaternionEquivalent(frame.Rotation[0], expectedRotation);
                Assert.That(
                    Vector3.Distance(frame.Scale[0], expectedScale),
                    Is.LessThan(Epsilon));
            });
        }

        private static void AssertQuaternionEquivalent(Quaternion actual, Quaternion expected)
        {
            var dot = Math.Abs(Quaternion.Dot(actual, expected));
            Assert.That(dot, Is.EqualTo(1).Within(Epsilon));
        }

        private sealed record TransformContext(
            TransformBoneCommand Command,
            BoneSelectionState Selection,
            AnimationClip Animation,
            Func<int> GetModifiedEventCount,
            Func<BoneSelectionState> GetLastModifiedState)
        {
            public int ModifiedEventCount => GetModifiedEventCount();
            public BoneSelectionState LastModifiedState => GetLastModifiedState();
        }
    }
}
