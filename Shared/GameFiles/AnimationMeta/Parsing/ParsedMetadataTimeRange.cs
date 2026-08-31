namespace Shared.GameFormats.AnimationMeta.Parsing;

public enum ParsedMetadataZeroRangeBehavior
{
    Instant,
    WholeAnimation,
}

public readonly record struct ParsedMetadataTimeRange(
    float StartTime,
    float EndTime,
    ParsedMetadataZeroRangeBehavior ZeroRangeBehavior)
{
    private const float BoundaryToleranceSeconds = 0.000001f;
    private static readonly HashSet<string> s_wholeAnimationZeroRangeTags =
    [
        "ALLOWED_DELTA_SCALE",
        "ALPHA",
        "ANIMATED_PROP",
        "BEARING",
        "BLEND_OVERRIDE",
        "BOUNDING_VOLUME_OVERRIDE",
        "CANNOT_DISMEMBER",
        "CREW_LOCATION",
        "DISABLE_FACIAL",
        "DISABLE_HEAD_TRACKING",
        "DISABLE_MODEL",
        "DISABLE_PERSISTENT",
        "DISABLE_PERSISTENT_ID",
        "DISABLE_PERSISTENT_VFX",
        "DISTANCE",
        "DOCK_EQPT_BACK",
        "DOCK_EQPT_LHAND",
        "DOCK_EQPT_LHAND_2",
        "DOCK_EQPT_LWAIST",
        "DOCK_EQPT_RHAND",
        "DOCK_EQPT_RHAND_2",
        "DOCK_EQPT_RWAIST",
        "FACE_POSE",
        "FULL_BODY",
        "IGNORE_FOOT_SLIDING",
        "IMPACT_SPEED",
        "LHAND_POSE",
        "MAX_TARGET_SIZE",
        "MIN_TARGET_SIZE",
        "NOT_BUILDING",
        "PARENT_CONSTRAINT",
        "POSITION",
        "PROP",
        "RESCALE",
        "RHAND_POSE",
        "RIDER_IDLE_SPEED_SCALE",
        "SC_HEIGHT",
        "SC_RADIUS",
        "SC_RATIO",
        "SHADER_PARAMETER",
        "SPLICE",
        "SPLICE_OVERRIDE",
        "TRANSFORM",
        "USE_BASE_METADATA",
        "WEAPON_HIP",
        "WEAPON_LHAND",
        "WEAPON_ON",
        "WEAPON_RHAND",
        "WOUNDED_POSE",
    ];

    public bool IsZeroRange =>
        Math.Abs(StartTime) <= BoundaryToleranceSeconds &&
        Math.Abs(EndTime) <= BoundaryToleranceSeconds;

    public bool IsWholeAnimationRange =>
        IsZeroRange &&
        ZeroRangeBehavior ==
            ParsedMetadataZeroRangeBehavior.WholeAnimation;

    public static bool TryCreate(
        ParsedMetadataAttribute attribute,
        out ParsedMetadataTimeRange timeRange)
    {
        if (attribute is DecodedMetaEntryBase_v2 version2)
        {
            timeRange = new(
                version2.StartTime,
                version2.EndTime,
                GetZeroRangeBehavior(attribute));
            return true;
        }

        if (attribute is DecodedMetaEntryBase version10)
        {
            timeRange = new(
                version10.StartTime,
                version10.EndTime,
                GetZeroRangeBehavior(attribute));
            return true;
        }

        timeRange = default;
        return false;
    }

    public static void Set(
        ParsedMetadataAttribute attribute,
        float startTime,
        float endTime)
    {
        switch (attribute)
        {
            case DecodedMetaEntryBase_v2 version2:
                version2.StartTime = startTime;
                version2.EndTime = endTime;
                break;
            case DecodedMetaEntryBase version10:
                version10.StartTime = startTime;
                version10.EndTime = endTime;
                break;
            default:
                throw new ArgumentException(
                    "Metadata attribute has no editable time range.",
                    nameof(attribute));
        }
    }

    private static ParsedMetadataZeroRangeBehavior GetZeroRangeBehavior(
        ParsedMetadataAttribute attribute)
    {
        var tagName = attribute.GetType()
            .GetCustomAttributes(typeof(MetaDataAttribute), true)
            .OfType<MetaDataAttribute>()
            .FirstOrDefault()
            ?.Name;
        return tagName != null &&
            s_wholeAnimationZeroRangeTags.Contains(tagName)
                ? ParsedMetadataZeroRangeBehavior.WholeAnimation
                : ParsedMetadataZeroRangeBehavior.Instant;
    }
}
