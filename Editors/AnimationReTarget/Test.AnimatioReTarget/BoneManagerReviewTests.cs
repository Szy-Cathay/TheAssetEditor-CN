using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.AnimatioReTarget.Editor.BoneHandling.Presentation;
using Editors.Shared.Core.Common;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.GameFormats.Animation;

namespace Test.AnimatioReTarget
{
    public class BoneManagerReviewTests
    {
        [SetUp]
        public void SetUp()
        {
            new LocalizationManager().LoadLanguage();
        }

        [Test]
        public void ReviewActions_ConfirmCandidateAndSkipAccessory_UpdateGateImmediately()
        {
            var source = CreateSkeleton(
                ("root", -1),
                ("socket", 0),
                ("socket", 0));
            var target = CreateSkeleton(
                ("root", -1),
                ("socket", 0),
                ("cape_back_0", 0));
            var manager = CreateManager(source, target);

            manager.AutoMapBones();

            Assert.Multiple(() =>
            {
                Assert.That(manager.CanBatchRetarget, Is.False);
                Assert.That(manager.ReviewItems, Has.Count.EqualTo(2));
            });

            var socket = manager.ReviewItems.Single(item => item.TargetBoneName == "socket");
            manager.ConfirmCandidate(socket.Candidates[0]);

            Assert.Multiple(() =>
            {
                Assert.That(manager.CanBatchRetarget, Is.False);
                Assert.That(manager.ReviewItems, Has.Count.EqualTo(1));
                Assert.That(manager.ReviewItems.Single().TargetBoneName, Is.EqualTo("cape_back_0"));
            });

            manager.MarkIntentionalUnmapped(manager.ReviewItems.Single());

            Assert.Multiple(() =>
            {
                Assert.That(manager.IsMappingStructurallyReady, Is.True);
                Assert.That(manager.CanBatchRetarget, Is.False);
                Assert.That(manager.ReviewItems, Is.Empty);
                Assert.That(manager.LastAutoMappingSummary!.IntentionalUnmappedCount, Is.EqualTo(1));
                Assert.That(manager.BatchRetargetGateText, Does.Contain("预览"));
            });
        }

        [Test]
        public void ApplyManualMapping_SubsequentAutoMapPreservesManualChoice()
        {
            var source = CreateSkeleton(("root", -1), ("manual_control", 0));
            var target = CreateSkeleton(("root", -1), ("head", 0));
            var manager = CreateManager(source, target);
            manager.AutoMapBones();

            Assert.Multiple(() =>
            {
                Assert.That(manager.CanBatchRetarget, Is.False);
                Assert.That(manager.LastAutoMappingSummary!.CoreBlockingCount, Is.EqualTo(1));
                Assert.That(manager.BatchRetargetGateText, Does.Contain("核心动作骨骼"));
            });

            var applied = manager.ApplyManualMapping(targetBoneIndex: 1, sourceBoneIndex: 1);
            manager.AutoMapBones();

            var item = manager.LastAutoMappingSummary!.Items.Single(x => x.TargetBoneIndex == 1);
            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.True);
                Assert.That(manager.IsMappingStructurallyReady, Is.True);
                Assert.That(manager.CanBatchRetarget, Is.False);
                Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.Confirmed));
                Assert.That(item.SourceBoneName, Is.EqualTo("manual_control"));
                Assert.That(item.Evidence, Is.EqualTo(BoneAutoMappingEvidence.ExistingMapping));
            });
        }

        [Test]
        public void MappingConfirmation_RequiresCurrentPreviewAndExplicitConfirmation()
        {
            var skeleton = CreateSkeleton(("root", -1), ("spine", 0));
            var manager = CreateManager(skeleton, skeleton);

            manager.AutoMapBones();

            Assert.Multiple(() =>
            {
                Assert.That(manager.IsMappingStructurallyReady, Is.True);
                Assert.That(manager.HasPreviewedCurrentMapping, Is.False);
                Assert.That(manager.IsMappingConfirmed, Is.False);
                Assert.That(manager.CanBatchRetarget, Is.False);
                Assert.That(manager.ConfirmMappingCommand.CanExecute(null), Is.False);
                Assert.That(manager.BatchRetargetGateText, Does.Contain("预览"));
            });

            var previewRevision = manager.BeginMappingPreview();

            Assert.Multiple(() =>
            {
                Assert.That(previewRevision, Is.Not.Null);
                Assert.That(manager.IsPreviewingCurrentMapping, Is.True);
                Assert.That(manager.HasPreviewedCurrentMapping, Is.False);
                Assert.That(manager.ConfirmMappingCommand.CanExecute(null), Is.False);
                Assert.That(manager.BatchRetargetGateText, Does.Contain("播放"));
            });

            manager.CompleteMappingPreview(previewRevision!.Value);

            Assert.Multiple(() =>
            {
                Assert.That(manager.HasPreviewedCurrentMapping, Is.True);
                Assert.That(manager.IsMappingConfirmed, Is.False);
                Assert.That(manager.CanBatchRetarget, Is.False);
                Assert.That(manager.ConfirmMappingCommand.CanExecute(null), Is.True);
                Assert.That(manager.BatchRetargetGateText, Does.Contain("确认"));
            });

            manager.ConfirmMapping();

            Assert.Multiple(() =>
            {
                Assert.That(manager.IsMappingConfirmed, Is.True);
                Assert.That(manager.CanBatchRetarget, Is.True);
                Assert.That(manager.ConfirmMappingCommand.CanExecute(null), Is.False);
                Assert.That(manager.BatchRetargetGateText, Does.Contain("已确认"));
            });
        }

        [Test]
        public void MappingConfirmation_CoreBlockerPreventsConfirmationAfterPreview()
        {
            var source = CreateSkeleton(("manual_control", -1));
            var target = CreateSkeleton(("head", -1));
            var manager = CreateManager(source, target);

            manager.AutoMapBones();
            CompletePreview(manager);
            manager.ConfirmMapping();

            Assert.Multiple(() =>
            {
                Assert.That(manager.IsMappingStructurallyReady, Is.False);
                Assert.That(manager.HasPreviewedCurrentMapping, Is.True);
                Assert.That(manager.IsMappingConfirmed, Is.False);
                Assert.That(manager.CanBatchRetarget, Is.False);
                Assert.That(manager.ConfirmMappingCommand.CanExecute(null), Is.False);
                Assert.That(manager.BatchRetargetGateText, Does.Contain("核心动作骨骼"));
            });
        }

        [Test]
        public void MappingConfirmation_UnchangedAutoMapPreservesConfirmation()
        {
            var skeleton = CreateSkeleton(("root", -1), ("spine", 0));
            var manager = CreateManager(skeleton, skeleton);
            manager.AutoMapBones();
            CompletePreview(manager);
            manager.ConfirmMapping();

            manager.AutoMapBones();

            Assert.Multiple(() =>
            {
                Assert.That(manager.HasPreviewedCurrentMapping, Is.True);
                Assert.That(manager.IsMappingConfirmed, Is.True);
                Assert.That(manager.CanBatchRetarget, Is.True);
            });
        }

        [Test]
        public void MappingConfirmation_MappingOrSkeletonChangeInvalidatesPreviewAndConfirmation()
        {
            var skeleton = CreateSkeleton(("root", -1), ("spine", 0));
            var manager = CreateManager(skeleton, skeleton);
            manager.AutoMapBones();
            CompletePreview(manager);
            manager.ConfirmMapping();

            manager.ApplyManualMapping(targetBoneIndex: 1, sourceBoneIndex: 0);

            Assert.Multiple(() =>
            {
                Assert.That(manager.HasPreviewedCurrentMapping, Is.False);
                Assert.That(manager.IsMappingConfirmed, Is.False);
                Assert.That(manager.CanBatchRetarget, Is.False);
            });

            CompletePreview(manager);
            manager.ConfirmMapping();
            manager.UpdateTargetSkeleton("target2");

            Assert.Multiple(() =>
            {
                Assert.That(manager.LastAutoMappingSummary, Is.Null);
                Assert.That(manager.HasPreviewedCurrentMapping, Is.False);
                Assert.That(manager.IsMappingConfirmed, Is.False);
                Assert.That(manager.CanBatchRetarget, Is.False);
            });

            manager.AutoMapBones();
            CompletePreview(manager);
            manager.ConfirmMapping();
            manager.UpdateSourceSkeleton("source2");

            Assert.Multiple(() =>
            {
                Assert.That(manager.LastAutoMappingSummary, Is.Null);
                Assert.That(manager.HasPreviewedCurrentMapping, Is.False);
                Assert.That(manager.IsMappingConfirmed, Is.False);
                Assert.That(manager.CanBatchRetarget, Is.False);
            });
        }

        [Test]
        public void MappingConfirmation_StalePreviewCompletionCannotApproveChangedMapping()
        {
            var skeleton = CreateSkeleton(("root", -1), ("spine", 0));
            var manager = CreateManager(skeleton, skeleton);
            manager.AutoMapBones();

            var staleRevision = manager.BeginMappingPreview();
            manager.ApplyManualMapping(targetBoneIndex: 1, sourceBoneIndex: 0);
            manager.CompleteMappingPreview(staleRevision!.Value);

            Assert.Multiple(() =>
            {
                Assert.That(manager.IsPreviewingCurrentMapping, Is.False);
                Assert.That(manager.HasPreviewedCurrentMapping, Is.False);
                Assert.That(manager.IsMappingConfirmed, Is.False);
                Assert.That(manager.CanBatchRetarget, Is.False);
            });
        }

        [Test]
        public void MappingConfirmation_ReviewDecisionsInvalidateCurrentPreview()
        {
            var source = CreateSkeleton(
                ("root", -1),
                ("socket", 0),
                ("socket", 0));
            var target = CreateSkeleton(
                ("root", -1),
                ("socket", 0),
                ("cape_back_0", 0));
            var manager = CreateManager(source, target);
            manager.AutoMapBones();
            CompletePreview(manager);

            var socket = manager.ReviewItems.Single(item => item.TargetBoneName == "socket");
            manager.ConfirmCandidate(socket.Candidates[0]);

            Assert.That(manager.HasPreviewedCurrentMapping, Is.False);

            CompletePreview(manager);
            manager.MarkIntentionalUnmapped(manager.ReviewItems.Single());

            Assert.Multiple(() =>
            {
                Assert.That(manager.HasPreviewedCurrentMapping, Is.False);
                Assert.That(manager.IsMappingConfirmed, Is.False);
                Assert.That(manager.CanBatchRetarget, Is.False);
            });
        }

        [Test]
        public void AutoMapBones_AmbiguousMatch_OnlyShowsFiveReviewCandidates()
        {
            var source = CreateSkeleton(
                ("socket", -1),
                ("socket", -1),
                ("socket", -1),
                ("socket", -1),
                ("socket", -1),
                ("socket", -1),
                ("socket", -1));
            var target = CreateSkeleton(("socket", -1));
            var manager = CreateManager(source, target);

            manager.AutoMapBones();

            Assert.Multiple(() =>
            {
                Assert.That(manager.LastAutoMappingSummary!.Items.Single().Candidates, Has.Count.EqualTo(7));
                Assert.That(manager.ReviewItems.Single().Candidates, Has.Count.EqualTo(5));
            });
        }

        private static BoneManager CreateManager(AnimationFile source, AnimationFile target)
        {
            var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
            skeletonLookup.Setup(service => service.GetSkeletonFileFromName("source")).Returns(source);
            skeletonLookup.Setup(service => service.GetSkeletonFileFromName("source2")).Returns(source);
            skeletonLookup.Setup(service => service.GetSkeletonFileFromName("target")).Returns(target);
            skeletonLookup.Setup(service => service.GetSkeletonFileFromName("target2")).Returns(target);

            var manager = new BoneManager(
                Mock.Of<IStandardDialogs>(),
                Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
                skeletonLookup.Object);
            manager.UpdateSourceSkeleton("source");
            manager.UpdateTargetSkeleton("target");
            return manager;
        }

        private static void CompletePreview(BoneManager manager)
        {
            var previewRevision = manager.BeginMappingPreview();
            Assert.That(previewRevision, Is.Not.Null);
            manager.CompleteMappingPreview(previewRevision!.Value);
        }

        private static AnimationFile CreateSkeleton(params (string Name, int ParentId)[] bones)
        {
            return new AnimationFile
            {
                Bones = bones
                    .Select((bone, index) => new AnimationFile.BoneInfo
                    {
                        Id = index,
                        Name = bone.Name,
                        ParentId = bone.ParentId
                    })
                    .ToArray()
            };
        }
    }
}
