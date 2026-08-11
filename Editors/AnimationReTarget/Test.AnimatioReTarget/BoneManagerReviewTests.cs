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
                Assert.That(manager.CanBatchRetarget, Is.True);
                Assert.That(manager.ReviewItems, Is.Empty);
                Assert.That(manager.LastAutoMappingSummary!.IntentionalUnmappedCount, Is.EqualTo(1));
                Assert.That(manager.BatchRetargetGateText, Does.Contain("可以"));
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
                Assert.That(manager.CanBatchRetarget, Is.True);
                Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.Confirmed));
                Assert.That(item.SourceBoneName, Is.EqualTo("manual_control"));
                Assert.That(item.Evidence, Is.EqualTo(BoneAutoMappingEvidence.ExistingMapping));
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
            skeletonLookup.Setup(service => service.GetSkeletonFileFromName("target")).Returns(target);

            var manager = new BoneManager(
                Mock.Of<IStandardDialogs>(),
                Mock.Of<IAbstractFormFactory<BoneMappingWindow>>(),
                skeletonLookup.Object);
            manager.UpdateSourceSkeleton("source");
            manager.UpdateTargetSkeleton("target");
            return manager;
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
