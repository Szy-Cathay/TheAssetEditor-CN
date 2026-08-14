using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Inspection
{
    public enum MetaDataAuthoredTimeStatus
    {
        NotApplicable,
        Valid,
        NonFinite,
        Negative,
        Reversed,
        OutsideClip,
        ClipUnavailable,
    }

    public enum MetaDataTimelineMarkerKind
    {
        Instant,
        Range,
        WholeAnimation,
    }

    public enum MetaDataPreviewCapability
    {
        Unavailable,
        Available,
        AvailableWithDiagnostics,
    }

    public sealed record MetaDataInspectionSource(
        ParsedMetadataAttribute Source,
        MetaDataDocumentOwner Owner,
        bool AreFieldsValid,
        IReadOnlyList<string>? InvalidFieldNames = null);

    public sealed record MetaDataInspectionItem(
        ParsedMetadataAttribute Source,
        MetaDataDocumentOwner Owner,
        bool AreFieldsValid,
        MetaDataTimeRange? AuthoredTimeRange,
        MetaDataAuthoredTimeStatus AuthoredTimeStatus,
        MetaDataTimelineMarkerKind? TimelineMarkerKind,
        MetaDataPreviewCapability PreviewCapability,
        CombatMetaDataPreviewCategory? PreviewCategory,
        Vector3? FocusPosition,
        IReadOnlyList<MetaDataBuildDiagnostic> Diagnostics)
    {
        public IReadOnlyList<string> InvalidFieldNames { get; init; } = [];
    }

    public sealed record MetaDataInspectionProblem(
        MetaDataInspectionItem Item,
        MetaDataDiagnosticSeverity Severity,
        string ReasonKey,
        string? FieldName = null,
        MetaDataTimeRange? TimeRange = null,
        Vector3? Position = null,
        string? ResourcePath = null,
        string? BoneName = null)
    {
        public ParsedMetadataAttribute Source => Item.Source;
        public MetaDataDocumentOwner Owner => Item.Owner;
    }

    public sealed class MetaDataInspectionIndex
    {
        private const float BoundaryToleranceSeconds = 0.000001f;

        public IReadOnlyList<MetaDataInspectionItem> Items { get; }
        public IReadOnlyList<MetaDataInspectionProblem> Problems { get; }

        private MetaDataInspectionIndex(
            IReadOnlyList<MetaDataInspectionItem> items)
        {
            Items = items.ToArray();
            Problems = Items.SelectMany(CreateProblems).ToArray();
        }

        public static MetaDataInspectionIndex Create(
            IEnumerable<MetaDataInspectionSource> sources,
            IEnumerable<IMetaDataInstance> instances,
            IEnumerable<MetaDataBuildDiagnostic> diagnostics,
            float clipDurationSeconds)
        {
            var previews = instances.OfType<IMetaDataPreview>().ToArray();
            var diagnosticList = diagnostics.ToArray();
            var items = sources.Select(source => CreateItem(
                source,
                previews,
                diagnosticList,
                clipDurationSeconds)).ToArray();
            return new MetaDataInspectionIndex(items);
        }

        private static MetaDataInspectionItem CreateItem(
            MetaDataInspectionSource source,
            IReadOnlyList<IMetaDataPreview> previews,
            IReadOnlyList<MetaDataBuildDiagnostic> diagnostics,
            float clipDurationSeconds)
        {
            var preview = previews.FirstOrDefault(candidate =>
                ReferenceEquals(candidate.Source, source.Source));
            var itemDiagnostics = diagnostics.Where(diagnostic =>
                diagnostic.Owner == source.Owner &&
                ReferenceEquals(diagnostic.Source, source.Source)).ToArray();

            MetaDataTimeRange? authoredTimeRange = null;
            var authoredTimeStatus = MetaDataAuthoredTimeStatus.NotApplicable;
            MetaDataTimelineMarkerKind? markerKind = null;
            if (MetaDataTimeRange.TryCreate(source.Source, out var timeRange))
            {
                authoredTimeRange = timeRange;
                (authoredTimeStatus, markerKind) = ClassifyTimeRange(
                    timeRange,
                    clipDurationSeconds);
                if (!source.AreFieldsValid)
                    markerKind = null;
            }

            var previewCapability = preview == null
                ? MetaDataPreviewCapability.Unavailable
                : itemDiagnostics.Length == 0
                    ? MetaDataPreviewCapability.Available
                    : MetaDataPreviewCapability.AvailableWithDiagnostics;
            return new MetaDataInspectionItem(
                source.Source,
                source.Owner,
                source.AreFieldsValid,
                authoredTimeRange,
                authoredTimeStatus,
                markerKind,
                previewCapability,
                GetPreviewCategory(source.Source, preview),
                TryGetFocusPosition(preview),
                itemDiagnostics)
            {
                InvalidFieldNames = source.InvalidFieldNames?.ToArray() ?? [],
            };
        }

        private static IEnumerable<MetaDataInspectionProblem> CreateProblems(
            MetaDataInspectionItem item)
        {
            if (!item.AreFieldsValid)
            {
                if (item.InvalidFieldNames.Count == 0)
                {
                    yield return CreateProblem(
                        item,
                        MetaDataDiagnosticSeverity.Error,
                        "SuperView.Problems.InvalidFields");
                }
                else
                {
                    foreach (var fieldName in item.InvalidFieldNames)
                    {
                        yield return CreateProblem(
                            item,
                            MetaDataDiagnosticSeverity.Error,
                            "SuperView.Problems.InvalidField",
                            fieldName);
                    }
                }
            }
            else if (GetTimeProblemReasonKey(item.AuthoredTimeStatus) is
                     string timeReasonKey)
            {
                yield return CreateProblem(
                    item,
                    MetaDataDiagnosticSeverity.Warning,
                    timeReasonKey);
            }

            foreach (var diagnostic in item.Diagnostics)
            {
                yield return new MetaDataInspectionProblem(
                    item,
                    diagnostic.Severity,
                    diagnostic.ReasonKey,
                    TimeRange: diagnostic.TimeRange,
                    Position: diagnostic.Position,
                    ResourcePath: diagnostic.ResourcePath,
                    BoneName: diagnostic.BoneName);
            }
        }

        private static MetaDataInspectionProblem CreateProblem(
            MetaDataInspectionItem item,
            MetaDataDiagnosticSeverity severity,
            string reasonKey,
            string? fieldName = null) => new(
                item,
                severity,
                reasonKey,
                fieldName,
                item.AuthoredTimeRange,
                item.FocusPosition);

        private static string? GetTimeProblemReasonKey(
            MetaDataAuthoredTimeStatus status) => status switch
            {
                MetaDataAuthoredTimeStatus.NonFinite =>
                    "SuperView.Problems.Time.NonFinite",
                MetaDataAuthoredTimeStatus.Negative =>
                    "SuperView.Problems.Time.Negative",
                MetaDataAuthoredTimeStatus.Reversed =>
                    "SuperView.Problems.Time.Reversed",
                MetaDataAuthoredTimeStatus.OutsideClip =>
                    "SuperView.Problems.Time.OutsideClip",
                MetaDataAuthoredTimeStatus.ClipUnavailable =>
                    "SuperView.Problems.Time.ClipUnavailable",
                _ => null,
            };

        private static CombatMetaDataPreviewCategory? GetPreviewCategory(
            ParsedMetadataAttribute source,
            IMetaDataPreview? preview) => source switch
            {
                ImpactPosition_v2 or ImpactPosition_v10 =>
                    CombatMetaDataPreviewCategory.Impact,
                TargetPos_0 or TargetPos_10 =>
                    CombatMetaDataPreviewCategory.Target,
                FirePos_v0 or FirePos_v2 or FirePos_v10 =>
                    CombatMetaDataPreviewCategory.Fire,
                SplashAttack_v3 or SplashAttack_v10 =>
                    CombatMetaDataPreviewCategory.Splash,
                _ => (preview as ICombatMetaDataPreview)?.Category,
            };

        private static Vector3? TryGetFocusPosition(IMetaDataPreview? preview)
        {
            if (preview is not ISpatialMetaDataPreview spatialPreview)
                return null;

            try
            {
                return spatialPreview.FocusPosition;
            }
            catch (NullReferenceException)
            {
                return null;
            }
        }

        private static (
            MetaDataAuthoredTimeStatus Status,
            MetaDataTimelineMarkerKind? MarkerKind) ClassifyTimeRange(
                MetaDataTimeRange timeRange,
                float clipDurationSeconds)
        {
            if (!float.IsFinite(timeRange.StartTime) ||
                !float.IsFinite(timeRange.EndTime))
            {
                return (MetaDataAuthoredTimeStatus.NonFinite, null);
            }

            if (timeRange.StartTime < 0 || timeRange.EndTime < 0)
                return (MetaDataAuthoredTimeStatus.Negative, null);

            if (timeRange.StartTime > timeRange.EndTime)
                return (MetaDataAuthoredTimeStatus.Reversed, null);

            if (!float.IsFinite(clipDurationSeconds) ||
                clipDurationSeconds <= 0)
            {
                return (MetaDataAuthoredTimeStatus.ClipUnavailable, null);
            }

            if (timeRange.StartTime > clipDurationSeconds ||
                timeRange.EndTime > clipDurationSeconds)
            {
                return (MetaDataAuthoredTimeStatus.OutsideClip, null);
            }

            if (timeRange.IsWholeAnimationRange)
            {
                return (
                    MetaDataAuthoredTimeStatus.Valid,
                    MetaDataTimelineMarkerKind.WholeAnimation);
            }

            if (Math.Abs(timeRange.EndTime - timeRange.StartTime) <=
                BoundaryToleranceSeconds)
            {
                return (
                    MetaDataAuthoredTimeStatus.Valid,
                    MetaDataTimelineMarkerKind.Instant);
            }

            return (
                MetaDataAuthoredTimeStatus.Valid,
                MetaDataTimelineMarkerKind.Range);
        }
    }
}
