using Editors.AnimationMeta.SuperView.Inspection;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta
{
    [TestFixture]
    public class MetaDataInspectionIndexTests
    {
        [Test]
        public void Create_SameLookingSourcesInDifferentOwners_RemainDistinct()
        {
            var persistentSource = CreateFirePosition(1, 2);
            var animationSource = CreateFirePosition(1, 2);
            var preview = new CombatMetaDataInstance(
                persistentSource,
                CombatMetaDataPreviewCategory.Fire,
                new Vector3(4, 5, 6),
                new GroupNode("preview"),
                false,
                _ => { },
                new AnimationPlayer(),
                new MetaDataTimeRange(1, 2));
            var diagnostic = new MetaDataBuildDiagnostic(
                animationSource,
                MetaDataDocumentOwner.Animation,
                MetaDataDiagnosticSeverity.Warning,
                "SuperView.Diagnostics.PreviewUnavailable");

            var index = MetaDataInspectionIndex.Create(
                [
                    new MetaDataInspectionSource(
                        persistentSource,
                        MetaDataDocumentOwner.Persistent,
                        true),
                    new MetaDataInspectionSource(
                        animationSource,
                        MetaDataDocumentOwner.Animation,
                        false),
                ],
                [preview],
                [diagnostic],
                5);

            Assert.That(index.Items, Has.Count.EqualTo(2));
            var persistentItem = index.Items[0];
            var animationItem = index.Items[1];
            Assert.Multiple(() =>
            {
                Assert.That(
                    persistentItem.Source,
                    Is.SameAs(persistentSource));
                Assert.That(
                    persistentItem.Owner,
                    Is.EqualTo(MetaDataDocumentOwner.Persistent));
                Assert.That(persistentItem.AreFieldsValid, Is.True);
                Assert.That(
                    persistentItem.AuthoredTimeRange,
                    Is.EqualTo(new MetaDataTimeRange(1, 2)));
                Assert.That(
                    persistentItem.PreviewCapability,
                    Is.EqualTo(MetaDataPreviewCapability.Available));
                Assert.That(
                    persistentItem.PreviewCategory,
                    Is.EqualTo(CombatMetaDataPreviewCategory.Fire));
                Assert.That(
                    persistentItem.FocusPosition,
                    Is.EqualTo(new Vector3(4, 5, 6)));

                Assert.That(
                    animationItem.Source,
                    Is.SameAs(animationSource));
                Assert.That(
                    animationItem.Owner,
                    Is.EqualTo(MetaDataDocumentOwner.Animation));
                Assert.That(animationItem.AreFieldsValid, Is.False);
                Assert.That(
                    animationItem.PreviewCapability,
                    Is.EqualTo(MetaDataPreviewCapability.Unavailable));
                Assert.That(
                    animationItem.PreviewCategory,
                    Is.EqualTo(CombatMetaDataPreviewCategory.Fire));
                Assert.That(
                    animationItem.Diagnostics.Single(),
                    Is.SameAs(diagnostic));
            });
        }

        [Test]
        public void Create_TimedSources_ClassifiesMarkersAndRejectsInvalidRanges()
        {
            var instant = CreateFirePosition(1, 1);
            var range = CreateFirePosition(1, 2);
            var wholeAnimation = new Prop_v10
            {
                Name = "PROP",
                Version = 10,
                StartTime = 0,
                EndTime = 0,
            };
            var zeroRangeNotAllowlisted = CreateFirePosition(0, 0);
            var negative = CreateFirePosition(-1, 0);
            var reversed = CreateFirePosition(2, 1);
            var outsideClip = CreateFirePosition(0, 6);
            var untimed = new FirePos_v0
            {
                Name = "FIRE_POS",
                Version = 0,
            };
            ParsedMetadataAttribute[] sources =
            [
                instant,
                range,
                wholeAnimation,
                zeroRangeNotAllowlisted,
                negative,
                reversed,
                outsideClip,
                untimed,
            ];

            var index = MetaDataInspectionIndex.Create(
                sources.Select(source => new MetaDataInspectionSource(
                    source,
                    MetaDataDocumentOwner.Animation,
                    true)),
                [],
                [],
                5);

            Assert.Multiple(() =>
            {
                AssertItem(
                    index,
                    instant,
                    MetaDataAuthoredTimeStatus.Valid,
                    MetaDataTimelineMarkerKind.Instant);
                AssertItem(
                    index,
                    range,
                    MetaDataAuthoredTimeStatus.Valid,
                    MetaDataTimelineMarkerKind.Range);
                AssertItem(
                    index,
                    wholeAnimation,
                    MetaDataAuthoredTimeStatus.Valid,
                    MetaDataTimelineMarkerKind.WholeAnimation);
                AssertItem(
                    index,
                    zeroRangeNotAllowlisted,
                    MetaDataAuthoredTimeStatus.Valid,
                    MetaDataTimelineMarkerKind.Instant);
                AssertItem(
                    index,
                    negative,
                    MetaDataAuthoredTimeStatus.Negative,
                    null);
                AssertItem(
                    index,
                    reversed,
                    MetaDataAuthoredTimeStatus.Reversed,
                    null);
                AssertItem(
                    index,
                    outsideClip,
                    MetaDataAuthoredTimeStatus.OutsideClip,
                    null);
                AssertItem(
                    index,
                    untimed,
                    MetaDataAuthoredTimeStatus.NotApplicable,
                    null);
            });
        }

        [Test]
        public void Create_SpatialFocusUnavailable_PreservesInspectionItem()
        {
            var source = CreateFirePosition(1, 2);
            var preview = new ThrowingSpatialPreview(source);

            MetaDataInspectionIndex? index = null;
            Assert.DoesNotThrow(() => index = MetaDataInspectionIndex.Create(
                [new MetaDataInspectionSource(
                    source,
                    MetaDataDocumentOwner.Animation,
                    true)],
                [preview],
                [],
                5));

            Assert.Multiple(() =>
            {
                Assert.That(index!.Items, Has.Count.EqualTo(1));
                Assert.That(index.Items.Single().FocusPosition, Is.Null);
                Assert.That(
                    index.Items.Single().PreviewCapability,
                    Is.EqualTo(MetaDataPreviewCapability.Available));
            });
        }

        [Test]
        public void Create_ProjectsFieldTimeAndPreviewProblemsWithSharedIdentity()
        {
            var invalidField = CreateFirePosition(1, 2);
            var negativeTime = CreateFirePosition(-1, 0);
            var reversedTime = CreateFirePosition(2, 1);
            var outsideClip = CreateFirePosition(0, 6);
            var previewFailure = CreateFirePosition(1, 2);
            var diagnosticPosition = new Vector3(7, 8, 9);
            var diagnosticReasons = new[]
            {
                "SuperView.Diagnostics.MissingBone",
                "SuperView.Diagnostics.MissingModel",
                "SuperView.Diagnostics.MissingAnimation",
                "SuperView.Diagnostics.MissingEffect",
                "SuperView.Diagnostics.RuleUnavailable",
                "SuperView.Diagnostics.PreviewUnavailable",
            };
            var diagnostics = diagnosticReasons.Select(reason =>
                new MetaDataBuildDiagnostic(
                    previewFailure,
                    MetaDataDocumentOwner.Persistent,
                    MetaDataDiagnosticSeverity.Warning,
                    reason,
                    new MetaDataTimeRange(1, 2),
                    diagnosticPosition,
                    "resource/path",
                    "weapon_bone")).ToArray();

            var index = MetaDataInspectionIndex.Create(
                [
                    new MetaDataInspectionSource(
                        invalidField,
                        MetaDataDocumentOwner.Animation,
                        false,
                        ["开始时间"]),
                    new MetaDataInspectionSource(
                        negativeTime,
                        MetaDataDocumentOwner.Animation,
                        true),
                    new MetaDataInspectionSource(
                        reversedTime,
                        MetaDataDocumentOwner.Animation,
                        true),
                    new MetaDataInspectionSource(
                        outsideClip,
                        MetaDataDocumentOwner.Animation,
                        true),
                    new MetaDataInspectionSource(
                        previewFailure,
                        MetaDataDocumentOwner.Persistent,
                        true),
                ],
                [],
                diagnostics,
                5);

            var invalidFieldProblem = index.Problems.Single(problem =>
                ReferenceEquals(problem.Source, invalidField));
            var previewProblems = index.Problems.Where(problem =>
                ReferenceEquals(problem.Source, previewFailure)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(index.Problems, Has.Count.EqualTo(10));
                Assert.That(
                    invalidFieldProblem.Owner,
                    Is.EqualTo(MetaDataDocumentOwner.Animation));
                Assert.That(
                    invalidFieldProblem.Severity,
                    Is.EqualTo(MetaDataDiagnosticSeverity.Error));
                Assert.That(
                    invalidFieldProblem.ReasonKey,
                    Is.EqualTo("SuperView.Problems.InvalidField"));
                Assert.That(invalidFieldProblem.FieldName, Is.EqualTo("开始时间"));
                Assert.That(
                    invalidFieldProblem.Item,
                    Is.SameAs(index.Items.Single(item =>
                        ReferenceEquals(item.Source, invalidField))));
                Assert.That(
                    index.Problems.Single(problem =>
                        ReferenceEquals(problem.Source, negativeTime)).ReasonKey,
                    Is.EqualTo("SuperView.Problems.Time.Negative"));
                Assert.That(
                    index.Problems.Single(problem =>
                        ReferenceEquals(problem.Source, reversedTime)).ReasonKey,
                    Is.EqualTo("SuperView.Problems.Time.Reversed"));
                Assert.That(
                    index.Problems.Single(problem =>
                        ReferenceEquals(problem.Source, outsideClip)).ReasonKey,
                    Is.EqualTo("SuperView.Problems.Time.OutsideClip"));
                Assert.That(
                    index.Problems.Where(problem => problem.Severity ==
                        MetaDataDiagnosticSeverity.Warning).ToArray(),
                    Has.Length.EqualTo(9));
                Assert.That(
                    previewProblems.Select(problem => problem.ReasonKey),
                    Is.EqualTo(diagnosticReasons));
                Assert.That(
                    previewProblems,
                    Has.All.Property(nameof(MetaDataInspectionProblem.Owner))
                        .EqualTo(MetaDataDocumentOwner.Persistent));
                Assert.That(
                    previewProblems,
                    Has.All.Property(nameof(MetaDataInspectionProblem.TimeRange))
                        .EqualTo(new MetaDataTimeRange(1, 2)));
                Assert.That(
                    previewProblems,
                    Has.All.Property(nameof(MetaDataInspectionProblem.Position))
                        .EqualTo(diagnosticPosition));
                Assert.That(
                    previewProblems,
                    Has.All.Property(nameof(MetaDataInspectionProblem.ResourcePath))
                        .EqualTo("resource/path"));
                Assert.That(
                    previewProblems,
                    Has.All.Property(nameof(MetaDataInspectionProblem.BoneName))
                        .EqualTo("weapon_bone"));
            });

            var resolved = MetaDataInspectionIndex.Create(
                [new MetaDataInspectionSource(
                    invalidField,
                    MetaDataDocumentOwner.Animation,
                    true)],
                [],
                [],
                5);
            Assert.That(resolved.Problems, Is.Empty);
        }

        private static FirePos_v10 CreateFirePosition(
            float startTime,
            float endTime) => new()
            {
                Name = "FIRE_POS",
                Version = 10,
                StartTime = startTime,
                EndTime = endTime,
                Position = new Vector3(1, 2, 3),
            };

        private static void AssertItem(
            MetaDataInspectionIndex index,
            ParsedMetadataAttribute source,
            MetaDataAuthoredTimeStatus expectedStatus,
            MetaDataTimelineMarkerKind? expectedMarkerKind)
        {
            var item = index.Items.Single(candidate =>
                ReferenceEquals(candidate.Source, source));
            Assert.That(item.AuthoredTimeStatus, Is.EqualTo(expectedStatus));
            Assert.That(item.TimelineMarkerKind, Is.EqualTo(expectedMarkerKind));
        }

        private sealed class ThrowingSpatialPreview(
            ParsedMetadataAttribute source) :
            IMetaDataInstance,
            ISpatialMetaDataPreview
        {
            public ParsedMetadataAttribute Source { get; } = source;
            public bool IsEnabled { get; set; }
            public bool IsSelected { get; set; }
            public bool ShowForEntireAnimation { get; set; }
            public Vector3 FocusPosition => throw new NullReferenceException();
            public Matrix ReferenceWorldTransform => Matrix.Identity;
            public Matrix WorldTransform => Matrix.Identity;
            public int? HighlightedBoneIndex => null;
            public AnimationPlayer Player { get; } = new();

            public void CleanUp()
            {
            }

            public void Update(float currentTime)
            {
            }
        }
    }
}
