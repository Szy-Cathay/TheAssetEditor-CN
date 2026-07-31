using Shared.GameFormats.Esf;


namespace Shared.GameFormats.Csc;

/// <summary>
/// Detects the keyframed-parameter grammar found throughout ELEMENT and the light/camera
/// elements:
///
///   Group   := U32(marker) + Channel+                      (one or more channels)
///   Channel := F32(header), [Ascii(mode), Ascii(mode)], Curve
///   Curve   := U32(keyframeCount) + keyframeCount times:
///                F32(x), F32(y), Ascii(mode), Coord2d(tangentIn), Coord2d(tangentOut), Ascii(mode)
///
/// A channel's curve can have zero keyframes - that's the common case (e.g. an ELEMENT with a
/// static, non-animated scale) and just means "not animated". The two Ascii mode-tag fields in a
/// channel's header are themselves optional - see "Two channel header forms" below.
///
/// The Group's own marker is always 1 in the corpus - **confirmed in-game to be
/// load-bearing/structural, not an inert or simple enable flag**:
/// flipping it to 0 on either of ELEMENT's position or weight groups crashed the file on load
/// both times, on battle_props/frg_cog_01.csc. The engine reads a group differently based on
/// this value (much like MODEL_ELEMENT's own fixed fields elsewhere in this format), so it isn't
/// safe to write out anything but 1 here even though its exact alternate meaning is unconfirmed.
///
/// Confirmed against the full corpus: for ELEMENT v7/v100 this grammar plus the record's known
/// 6-field header and 2-3 trailing fields (Position, Rotation, an unknown Bool) accounts for the
/// ENTIRE record with zero leftover fields - ELEMENT collapses to exactly one shape per version
/// once this is accounted for:
///
///   header(6) + Group(3) [position x/y/z] + Group(3) [rotation x/y/z]
///             + Group(1) [scale] + Group(1) [weight] + Coord3d + Coord3d + Bool
///
/// Position/rotation are confirmed via real rotation-range math (a spinning cog's curve spans
/// exactly 0..2*pi; a winch-driven cage's spans -2*pi..+3*2*pi, matching its observed motion).
/// Scale and weight are inferred from real per-file behavior: the first trailing single-channel
/// group is ~1.0 nearly everywhere but grows smoothly in a portal-opening animation (values like
/// 0.14->1, 1.06->8); the second is ~1.0 everywhere except files literally named "*_fade", where
/// it animates 1->0 or 0->1. "Weight" is COMPOSITE_SCENE's own literal channel name for that
/// fourth slot (see CompositeSceneDetector) - it superseded an earlier "opacity" guess.
///
/// ### Two channel header forms
///
/// Most channels use the "modern" 3-field header (F32 header, Ascii mode, Ascii mode) before the
/// curve. But a real, older sub-format drops the two Ascii mode-tag fields entirely - just
/// F32(header) directly followed by the curve's own U32(count). Found in genuinely older data,
/// not noise: 862/3,678 of ELEMENT v6, 6/62 of POINT_LIGHT_ELEMENT v4, and *all* 69/69 of
/// CAMERA_ELEMENT v3 - the exact same Group/Channel/Curve grammar, just the older header. Every
/// real record checked uses one form or the other uniformly throughout - never a mix of both
/// within the same record.
/// </summary>
public static class CurveDetector
{
    public sealed record CurvePoint(float X, float Y, string ModeIn, Coord2d TangentIn, Coord2d TangentOut, string ModeOut);

    /// <summary>
    /// <see cref="Header"/> is the channel's own leading F32 field. **Confirmed in-game**
    /// (on battle_props/frg_cog_01.csc's
    /// ELEMENT): it's the channel's live static value **only when the channel has zero
    /// keyframes** - editing the weight channel's header (which has 0 keyframes) visibly changed
    /// the model's rendered visibility, while editing a position/scale channel's header (each of
    /// which has a real keyframe) did nothing; only editing that keyframe's own value moved/resized
    /// the model. So a channel with any keyframes ignores its header entirely in favor of the
    /// curve; a channel with none uses the header directly as its constant value.
    /// </summary>
    public sealed record CurveData(float Header, IReadOnlyList<CurvePoint> Points);

    /// <summary>
    /// One channel within a group: the header at <see cref="StartIndex"/> followed by its curve.
    /// <see cref="HeaderFieldCount"/> is 3 for the modern header form (F32, Ascii, Ascii) or 1 for
    /// the older form (just F32) - i.e. the curve's own U32 count field starts at
    /// <c>StartIndex + HeaderFieldCount</c>. <see cref="KeyframeFieldRanges"/> gives each
    /// keyframe's absolute field range (for nesting raw fields under a synthetic per-keyframe node
    /// in the tree).
    /// </summary>
    public sealed record ChannelRun(int StartIndex, int FieldCount, int HeaderFieldCount, CurveData Curve, IReadOnlyList<(int Start, int Length)> KeyframeFieldRanges);

    /// <summary>One detected group: the marker field at <see cref="StartIndex"/> followed by <see cref="Channels"/>.</summary>
    public sealed record GroupRun(int StartIndex, int FieldCount, IReadOnlyList<ChannelRun> Channels);

    /// <summary>Scans a flat sibling list for U32-marked runs of channels, left to right, non-overlapping.</summary>
    public static List<GroupRun> DetectGroups(IReadOnlyList<EsfNode> siblings)
    {
        var groups = new List<GroupRun>();

        var i = 0;
        while (i < siblings.Count)
        {
            if (siblings[i].Kind == EsfNodeKind.U32)
            {
                var channels = ReadChannels(siblings, i + 1);
                if (channels.Count > 0)
                {
                    var width = 1 + channels.Sum(c => c.FieldCount);
                    groups.Add(new GroupRun(i, width, channels));
                    i += width;
                    continue;
                }
            }

            i++;
        }

        return groups;
    }

    private static List<ChannelRun> ReadChannels(IReadOnlyList<EsfNode> s, int start)
    {
        var channels = new List<ChannelRun>();
        var pos = start;
        while (TryReadChannel(s, pos) is { } channel)
        {
            channels.Add(channel);
            pos += channel.FieldCount;
        }

        return channels;
    }

    private static ChannelRun? TryReadChannel(IReadOnlyList<EsfNode> s, int start)
    {
        if (start >= s.Count || s[start].Kind != EsfNodeKind.F32)
            return null;

        var header = (float)s[start].Value!;

        // Modern header form: F32(header), Ascii(mode), Ascii(mode) - tried first since it's the
        // more specific match (requires 2 more fields to line up, so it's less likely to
        // false-positive than the bare-header fallback below).
        if (start + 2 < s.Count && s[start + 1].Kind == EsfNodeKind.Ascii && s[start + 2].Kind == EsfNodeKind.Ascii &&
            TryReadCurve(s, start + 3) is { } modern)
            return new ChannelRun(start, 3 + modern.FieldsConsumed, 3, new CurveData(header, modern.Points), modern.Ranges);

        // Older header form: just F32(header) directly followed by the curve - see "Two channel
        // header forms" above.
        if (TryReadCurve(s, start + 1) is { } legacy)
            return new ChannelRun(start, 1 + legacy.FieldsConsumed, 1, new CurveData(header, legacy.Points), legacy.Ranges);

        return null;
    }

    private static (List<CurvePoint> Points, List<(int, int)> Ranges, int FieldsConsumed)? TryReadCurve(IReadOnlyList<EsfNode> s, int curveStart)
    {
        // The count bound also guards against overflow when casting to int below - a real
        // keyframe count needs six complete fields, so it can never exceed the number of full
        // keyframe slots left in the sibling list. This
        // safely rules out unrelated U32 values here that are actually huge sentinels (e.g.
        // 0xFFFFFFFF, seen elsewhere in the corpus as a "no parent" marker).
        const int keyframeWidth = 6;
        if (curveStart >= s.Count || s[curveStart].Kind != EsfNodeKind.U32 || s[curveStart].Value is not uint count ||
            count > (uint)((s.Count - curveStart - 1) / keyframeWidth))
            return null;

        var n = (int)count;
        var points = new List<CurvePoint>(n);
        var ranges = new List<(int, int)>(n);
        for (var k = 0; k < n; k++)
        {
            var b = curveStart + 1 + k * keyframeWidth;
            if (!IsAnimatedKeyframeAt(s, b))
                return null;

            points.Add(new CurvePoint(
                (float)s[b].Value!, (float)s[b + 1].Value!, (string)s[b + 2].Value!,
                (Coord2d)s[b + 3].Value!, (Coord2d)s[b + 4].Value!, (string)s[b + 5].Value!));
            ranges.Add((b, keyframeWidth));
        }

        return (points, ranges, 1 + n * keyframeWidth);
    }

    private static bool IsAnimatedKeyframeAt(IReadOnlyList<EsfNode> s, int b) =>
        s[b].Kind == EsfNodeKind.F32 &&
        s[b + 1].Kind == EsfNodeKind.F32 &&
        s[b + 2].Kind == EsfNodeKind.Ascii &&
        s[b + 3].Kind == EsfNodeKind.Coord2d &&
        s[b + 4].Kind == EsfNodeKind.Coord2d &&
        s[b + 5].Kind == EsfNodeKind.Ascii;
}
