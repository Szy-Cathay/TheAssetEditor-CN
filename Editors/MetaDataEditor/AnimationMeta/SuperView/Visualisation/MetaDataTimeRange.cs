using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation
{
    public enum MetaDataZeroRangeBehavior
    {
        Instant,
        WholeAnimation,
    }

    public readonly record struct MetaDataTimeRange(
        float StartTime,
        float EndTime,
        MetaDataZeroRangeBehavior ZeroRangeBehavior =
            MetaDataZeroRangeBehavior.Instant)
    {
        private const float BoundaryToleranceSeconds = 0.000001f;
        public bool IsZeroRange =>
            Math.Abs(StartTime) <= BoundaryToleranceSeconds &&
            Math.Abs(EndTime) <= BoundaryToleranceSeconds;

        public bool IsWholeAnimationRange =>
            IsZeroRange &&
            ZeroRangeBehavior == MetaDataZeroRangeBehavior.WholeAnimation;

        public bool Contains(float currentTimeSeconds) =>
            IsWholeAnimationRange ||
            (currentTimeSeconds >= StartTime - BoundaryToleranceSeconds &&
                currentTimeSeconds <= EndTime + BoundaryToleranceSeconds);

        public bool Contains(
            float currentTimeSeconds,
            float minimumDurationSeconds)
        {
            if (IsWholeAnimationRange)
                return true;

            var effectiveEndTime =
                Math.Abs(EndTime - StartTime) <= BoundaryToleranceSeconds
                    ? StartTime + Math.Max(0, minimumDurationSeconds)
                    : EndTime;
            return currentTimeSeconds >= StartTime - BoundaryToleranceSeconds &&
                currentTimeSeconds <= effectiveEndTime + BoundaryToleranceSeconds;
        }

        public static bool TryCreate(
            ParsedMetadataAttribute attribute,
            out MetaDataTimeRange timeRange)
        {
            if (ParsedMetadataTimeRange.TryCreate(
                    attribute,
                    out var parsedTimeRange))
            {
                timeRange = new(
                    parsedTimeRange.StartTime,
                    parsedTimeRange.EndTime,
                    parsedTimeRange.ZeroRangeBehavior ==
                        ParsedMetadataZeroRangeBehavior.WholeAnimation
                        ? MetaDataZeroRangeBehavior.WholeAnimation
                        : MetaDataZeroRangeBehavior.Instant);
                return true;
            }

            timeRange = default;
            return false;
        }

    }
}
