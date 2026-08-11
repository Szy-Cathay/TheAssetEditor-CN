using Editors.AnimatioReTarget.Editor.BoneHandling;
using Shared.GameFormats.Animation;

namespace Test.AnimatioReTarget
{
    public class HighConfidenceBoneMapperTests
    {
        [Test]
        public void CreateSummary_ExactName_ConfirmsUniqueMapping()
        {
            var source = CreateSkeleton(("root", -1));
            var target = CreateSkeleton(("root", -1));

            var summary = HighConfidenceBoneMapper.CreateSummary(source, target);

            Assert.Multiple(() =>
            {
                Assert.That(summary.ConfirmedCount, Is.EqualTo(1));
                Assert.That(summary.ReviewRequiredCount, Is.Zero);
                Assert.That(summary.UnmatchedCount, Is.Zero);
                Assert.That(summary.Items.Single().Status, Is.EqualTo(BoneAutoMappingStatus.Confirmed));
                Assert.That(summary.Items.Single().SourceBoneIndex, Is.EqualTo(0));
            });
        }

        [Test]
        public void CreateSummary_NameVariant_ConfirmsUniqueMapping()
        {
            var source = CreateSkeleton(("head", -1));
            var target = CreateSkeleton(("head_0", -1));

            var item = HighConfidenceBoneMapper.CreateSummary(source, target).Items.Single();

            Assert.Multiple(() =>
            {
                Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.Confirmed));
                Assert.That(item.SourceBoneIndex, Is.EqualTo(0));
                Assert.That(item.Evidence, Is.EqualTo(BoneAutoMappingEvidence.NormalizedName));
            });
        }

        [Test]
        public void CreateSummary_NumberEndingInZero_DoesNotDropPartOfBoneIndex()
        {
            var source = CreateSkeleton(("weapon_1", -1));
            var target = CreateSkeleton(("weapon_10", -1));

            var item = HighConfidenceBoneMapper.CreateSummary(source, target).Items.Single();

            Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.Unmatched));
        }

        [Test]
        public void CreateSummary_KnownAlias_ConfirmsUniqueMapping()
        {
            var source = CreateSkeleton(("be_prop_0", -1));
            var target = CreateSkeleton(("weapon_1", -1));

            var item = HighConfidenceBoneMapper.CreateSummary(source, target).Items.Single();

            Assert.Multiple(() =>
            {
                Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.Confirmed));
                Assert.That(item.SourceBoneName, Is.EqualTo("be_prop_0"));
                Assert.That(item.Evidence, Is.EqualTo(BoneAutoMappingEvidence.KnownAlias));
            });
        }

        [TestCase("be_prop_1", "weapon_2")]
        [TestCase("hand_left", "bn_lefthand")]
        [TestCase("hand_right", "bn_righthand")]
        [TestCase("upperarm_left", "bn_leftarm")]
        [TestCase("upperleg_left", "bn_leftupleg")]
        [TestCase("spine_0", "bn_spine")]
        [TestCase("eyebrow", "bn_eyebrows")]
        public void CreateSummary_ExistingHumanoidAlias_ConfirmsUniqueMapping(
            string sourceBoneName,
            string targetBoneName)
        {
            var source = CreateSkeleton((sourceBoneName, -1));
            var target = CreateSkeleton((targetBoneName, -1));

            var item = HighConfidenceBoneMapper.CreateSummary(source, target).Items.Single();

            Assert.Multiple(() =>
            {
                Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.Confirmed));
                Assert.That(item.SourceBoneName, Is.EqualTo(sourceBoneName));
                Assert.That(item.Evidence, Is.EqualTo(BoneAutoMappingEvidence.KnownAlias));
            });
        }

        [Test]
        public void CreateSummary_ExistingMapping_PreservesManualChoice()
        {
            var source = CreateSkeleton(("manual_choice", -1), ("hand_left", -1));
            var target = CreateSkeleton(("hand_left", -1));
            var existingMappings = new Dictionary<int, int> { [0] = 0 };

            var item = HighConfidenceBoneMapper
                .CreateSummary(source, target, existingMappings)
                .Items.Single();

            Assert.Multiple(() =>
            {
                Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.Confirmed));
                Assert.That(item.SourceBoneIndex, Is.EqualTo(0));
                Assert.That(item.SourceBoneName, Is.EqualTo("manual_choice"));
                Assert.That(item.Evidence, Is.EqualTo(BoneAutoMappingEvidence.ExistingMapping));
            });
        }

        [Test]
        public void CreateSummary_DuplicateNames_UsesConfirmedParentHierarchy()
        {
            var source = CreateSkeleton(
                ("left_parent", -1),
                ("right_parent", -1),
                ("socket", 0),
                ("socket", 1));
            var target = CreateSkeleton(("left_parent", -1), ("socket", 0));

            var item = HighConfidenceBoneMapper.CreateSummary(source, target).Items.Single(x => x.TargetBoneIndex == 1);

            Assert.Multiple(() =>
            {
                Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.Confirmed));
                Assert.That(item.SourceBoneIndex, Is.EqualTo(2));
                Assert.That(item.Evidence, Is.EqualTo(BoneAutoMappingEvidence.Hierarchy));
            });
        }

        [Test]
        public void CreateSummary_UniqueNameWithConflictingParent_RequiresReview()
        {
            var source = CreateSkeleton(
                ("left_parent", -1),
                ("right_parent", -1),
                ("socket", 1));
            var target = CreateSkeleton(("left_parent", -1), ("socket", 0));

            var item = HighConfidenceBoneMapper.CreateSummary(source, target).Items.Single(x => x.TargetBoneIndex == 1);

            Assert.Multiple(() =>
            {
                Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.ReviewRequired));
                Assert.That(item.SourceBoneIndex, Is.Null);
                Assert.That(item.Candidates, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void CreateSummary_OppositeSideOnly_DoesNotCrossMap()
        {
            var source = CreateSkeleton(("hand_right", -1));
            var target = CreateSkeleton(("hand_left", -1));

            var item = HighConfidenceBoneMapper.CreateSummary(source, target).Items.Single();

            Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.Unmatched));
        }

        [Test]
        public void CreateSummary_MultipleCandidates_RequiresReview()
        {
            var source = CreateSkeleton(("socket", -1), ("socket", -1));
            var target = CreateSkeleton(("socket", -1));

            var item = HighConfidenceBoneMapper.CreateSummary(source, target).Items.Single();

            Assert.Multiple(() =>
            {
                Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.ReviewRequired));
                Assert.That(item.SourceBoneIndex, Is.Null);
                Assert.That(item.Candidates.Select(candidate => candidate.SourceBoneIndex), Is.EqualTo(new[] { 0, 1 }));
            });
        }

        [Test]
        public void CreateSummary_NoCandidate_IsUnmatched()
        {
            var source = CreateSkeleton(("root", -1));
            var target = CreateSkeleton(("cape", -1));

            var item = HighConfidenceBoneMapper.CreateSummary(source, target).Items.Single();

            Assert.Multiple(() =>
            {
                Assert.That(item.Status, Is.EqualTo(BoneAutoMappingStatus.Unmatched));
                Assert.That(item.Candidates, Is.Empty);
            });
        }

        [Test]
        public void CreateSummary_SameInput_ProducesStableOrderedResult()
        {
            var source = CreateSkeleton(("root", -1), ("socket", 0), ("socket", 0));
            var target = CreateSkeleton(("root", -1), ("socket", 0), ("missing", 0));

            var first = HighConfidenceBoneMapper.CreateSummary(source, target);
            var second = HighConfidenceBoneMapper.CreateSummary(source, target);

            var firstShape = first.Items.Select(ToStableShape);
            var secondShape = second.Items.Select(ToStableShape);
            Assert.That(firstShape, Is.EqualTo(secondShape));
        }

        private static object ToStableShape(BoneAutoMappingItem item)
        {
            return new
            {
                item.TargetBoneIndex,
                item.Status,
                item.SourceBoneIndex,
                CandidateIndexes = string.Join(",", item.Candidates.Select(candidate => candidate.SourceBoneIndex))
            };
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
