using Shared.GameFormats.Esf;


namespace Shared.GameFormats.Csc;

/// <summary>
/// Detects the named-channel keyframe-count manifest grammar found in COMPOSITE_SCENE:
///
///   Group     := U32(marker: 0 or 1) + I32(channelCount) + channelCount x Channel
///   Channel   := [Ascii(name), only if marker == 1] + I32(subComponentCount) + subComponentCount x SubComponent
///   SubComponent := Bool(flag) + I32(keyframeCount) + keyframeCount x Bool
///
/// marker == 1 means channels carry a name (e.g. "position", "orientation", "scale", "weight",
/// or the 11 camera_* parameters for a camera object); marker == 0 means the same shape without
/// the name (positional only) - both forms are common in the real corpus.
///
/// Confirmed against the full 1,462-file corpus: 1,191/1,191 COMPOSITE_SCENE records parse this
/// leading section with zero leftover fields. Crucially, SubComponent's <see cref="SubComponent.KeyframeCount"/>
/// was cross-checked byte-for-byte against the corresponding ELEMENT record's real keyframe count
/// (read via <see cref="CurveDetector"/>, already independently validated) for
/// battle_props/frg_cage_lower_raise_01.csc and matched exactly, channel by channel, element by
/// element - COMPOSITE_SCENE is a manifest that restates how many keyframes each element's
/// channels have, without repeating the keyframes themselves.
///
/// **Manifest group order is attach-tree DFS order** (roots in attach-group order, then each
/// element's children in attach-child order) - NOT plain ROOT encounter order, though the two
/// often coincide. Verified corpus-wide two ways: the group count equals the file's total ELEMENT
/// count (nested included) in 1,191/1,191 files, and in all 238 files that contain a
/// uniquely-typed group (a camera's camera_* channels, a light's spot_light_*/point_light_*
/// channels), that group sits at exactly the element's DFS position, zero exceptions.
///
/// **Named channels carry CA's own literal parameter names** for the owning element's parameter
/// groups, in record-group order - this is how SPOT_LIGHT_ELEMENT's/POINT_LIGHT_ELEMENT's/
/// ROOT_REF_ELEMENT's/CAMERA_ELEMENT's group meanings were identified (see KnownFields). Corpus
/// vocabulary: position/orientation/scale/weight (every element), camera_fov/near/far/focal_*/
/// kernal_*/roll/pivot_distance, spot_light_colour/intensity/length/inner_angle/outer_angle/
/// scatter_diameter, point_light_colour/intensity/range/scatter_diameter, and
/// root_ref_game_time_multiplier/root_ref_scene_time_multiplier.
///
/// What the per-keyframe Bool values mean isn't confirmed - in the corpus they're True 99.07% of
/// the time (97/10,422 False), so there's real but rare variation, not pure padding.
///
/// After the leading groups, a second grammar (the "entry section") is also confirmed:
///
///   EntrySection := I32(entryCount) + entryCount x Entry
///   Entry        := Ascii(name) + Bool(flag) + I32×3(reserved) + I32(elementIdCount)
///                   + elementIdCount x I32(elementId)
///
/// Confirmed corpus-wide (19,504 entries): name is empty 97.9% of the time, but genuinely
/// carries a real label the rest of the time (e.g. "Animation Track" - 338 occurrences,
/// "tentacle_props", "wingflap down", "mom_altar" - suggesting this identifies a named
/// animation track or attachment grouping rather than being decorative); the 3 reserved I32s
/// are 0 in 19,503/19,504 entries (one real exception: frg_gate_mid_drop.csc has (0,170,0), so
/// not an absolute rule); elementIdCount is 1 in 97.7% of entries but genuinely ranges up to 20;
/// every one of the 20,800 trailing element ids matches a real ELEMENT id elsewhere in the same
/// file, 100% of the time - these are element references, not incidental numbers; flag is False
/// in all but 1 of 19,504 entries.
///
/// After the entry section, there is a further footer, also now confirmed:
///
///   Footer := entryCount x Unit                           (same entryCount as the entry section above)
///   Unit   := I32(valueCount) + valueCount x I32(value)
///
/// Confirmed corpus-wide: 1,304/1,306 files with an entry section have a footer that parses as exactly entryCount units
/// with zero leftover fields. The 2 exceptions are both otherwise-empty scenes (0 elements, 0
/// entries) that still carry one extra trailing I32(0) - a real, rare deviation, not disproof of
/// the rule.
///
/// **The unit values are solved too**: unit i's values are the ENTRY INDEXES of entry i's
/// attachment children - the footer restates ROOT's parent/child attach tree as an adjacency
/// list over entry indexes. Verified corpus-wide against RootStructureDetector's attach tree:
/// 1,011/1,017 single-id-entry files match exactly; the 6 exceptions are all porthole scenes
/// whose variant models carry *nested* ELEMENTs (a parenting layer the simple top-level check
/// doesn't model), not counter-examples to the adjacency reading itself. In this reading, a
/// unit with a long sequential run of values is just a hub element with many children, and
/// alternating (1, N), (0) pairs are chains of one-child elements ending in leaves.
/// </summary>
public static class CompositeSceneDetector
{
    public sealed record SubComponentInfo(int StartIndex, int FieldCount, bool Flag, int KeyframeCount);

    public sealed record ChannelInfo(int StartIndex, int FieldCount, string? Name, IReadOnlyList<SubComponentInfo> SubComponents);

    public sealed record GroupInfo(int StartIndex, int FieldCount, bool Named, IReadOnlyList<ChannelInfo> Channels);

    public sealed record EntryInfo(int StartIndex, int FieldCount, string Name, bool Flag, IReadOnlyList<int> ElementIds);

    public sealed record EntrySectionInfo(int StartIndex, int FieldCount, IReadOnlyList<EntryInfo> Entries);

    public sealed record FooterUnitInfo(int StartIndex, int FieldCount, IReadOnlyList<int> Values);

    public sealed record FooterInfo(int StartIndex, int FieldCount, IReadOnlyList<FooterUnitInfo> Units);

    /// <summary>
    /// Attempts to read the entry section (see this class's remarks) starting at
    /// <paramref name="start"/> - normally right after the last group returned by
    /// <see cref="DetectGroups"/>. Returns null if the leading field isn't a plausible entry
    /// count or the fixed per-entry shape doesn't hold, so callers fall back to raw fields
    /// instead of asserting a wrong parse.
    /// </summary>
    public static EntrySectionInfo? DetectEntrySection(IReadOnlyList<EsfNode> siblings, int start)
    {
        if (start >= siblings.Count || siblings[start].Kind != EsfNodeKind.I32 || siblings[start].Value is not int entryCount || entryCount < 0)
            return null;

        var pos = start + 1;
        var entries = new List<EntryInfo>(entryCount);
        for (var e = 0; e < entryCount; e++)
        {
            if (TryReadEntry(siblings, pos) is not { } entry)
                return null;

            entries.Add(entry);
            pos += entry.FieldCount;
        }

        return new EntrySectionInfo(start, pos - start, entries);
    }

    private static EntryInfo? TryReadEntry(IReadOnlyList<EsfNode> s, int start)
    {
        if (start + 5 >= s.Count ||
            s[start].Kind != EsfNodeKind.Ascii || s[start + 1].Kind != EsfNodeKind.Bool ||
            s[start + 2].Kind != EsfNodeKind.I32 || s[start + 3].Kind != EsfNodeKind.I32 ||
            s[start + 4].Kind != EsfNodeKind.I32 || s[start + 5].Kind != EsfNodeKind.I32)
            return null;

        if (s[start].Value is not string name || s[start + 1].Value is not bool flag || s[start + 5].Value is not int elementIdCount || elementIdCount < 0)
            return null;

        var idsStart = start + 6;
        if (idsStart + elementIdCount > s.Count)
            return null;

        var ids = new List<int>(elementIdCount);
        for (var k = 0; k < elementIdCount; k++)
        {
            if (s[idsStart + k].Kind != EsfNodeKind.I32 || s[idsStart + k].Value is not int id)
                return null;
            ids.Add(id);
        }

        return new EntryInfo(start, 6 + elementIdCount, name, flag, ids);
    }

    /// <summary>
    /// Attempts to read the footer (see this class's remarks) starting at <paramref name="start"/>
    /// - normally right after <see cref="DetectEntrySection"/>'s result - as exactly
    /// <paramref name="unitCount"/> units (the entry section's own entry count). Returns null if
    /// that doesn't parse cleanly, so callers fall back to raw fields instead of asserting a wrong
    /// shape - real corpus data has two known exceptions (both otherwise-empty scenes) where this
    /// correctly returns null and the leftover field is shown raw.
    /// </summary>
    public static FooterInfo? DetectFooter(IReadOnlyList<EsfNode> siblings, int start, int unitCount)
    {
        if (unitCount < 0)
            return null;

        var pos = start;
        var units = new List<FooterUnitInfo>(unitCount);
        for (var u = 0; u < unitCount; u++)
        {
            if (TryReadFooterUnit(siblings, pos) is not { } unit)
                return null;

            units.Add(unit);
            pos += unit.FieldCount;
        }

        return new FooterInfo(start, pos - start, units);
    }

    private static FooterUnitInfo? TryReadFooterUnit(IReadOnlyList<EsfNode> s, int start)
    {
        if (start >= s.Count || s[start].Kind != EsfNodeKind.I32 || s[start].Value is not int valueCount || valueCount < 0)
            return null;

        var valuesStart = start + 1;
        if (valuesStart + valueCount > s.Count)
            return null;

        var values = new List<int>(valueCount);
        for (var k = 0; k < valueCount; k++)
        {
            if (s[valuesStart + k].Kind != EsfNodeKind.I32 || s[valuesStart + k].Value is not int value)
                return null;
            values.Add(value);
        }

        return new FooterUnitInfo(start, 1 + valueCount, values);
    }

    /// <summary>Scans a flat sibling list for U32(marker)-led groups, left to right, non-overlapping.</summary>
    public static List<GroupInfo> DetectGroups(IReadOnlyList<EsfNode> siblings)
    {
        var groups = new List<GroupInfo>();

        var i = 0;
        while (i < siblings.Count)
        {
            if (TryReadGroup(siblings, i) is { } group)
            {
                groups.Add(group);
                i += group.FieldCount;
                continue;
            }

            i++;
        }

        return groups;
    }

    private static GroupInfo? TryReadGroup(IReadOnlyList<EsfNode> s, int start)
    {
        if (start >= s.Count || s[start].Kind != EsfNodeKind.U32 || s[start].Value is not uint marker || marker > 1)
            return null;

        var named = marker == 1;
        var countIndex = start + 1;
        if (countIndex >= s.Count || s[countIndex].Kind != EsfNodeKind.I32)
            return null;

        var channelCount = (int)s[countIndex].Value!;
        if (channelCount < 0)
            return null;

        var pos = countIndex + 1;
        var channels = new List<ChannelInfo>(channelCount);
        for (var c = 0; c < channelCount; c++)
        {
            if (TryReadChannel(s, pos, named) is not { } channel)
                return null;

            channels.Add(channel);
            pos += channel.FieldCount;
        }

        return new GroupInfo(start, pos - start, named, channels);
    }

    private static ChannelInfo? TryReadChannel(IReadOnlyList<EsfNode> s, int start, bool named)
    {
        var pos = start;
        string? name = null;
        if (named)
        {
            if (pos >= s.Count || s[pos].Kind != EsfNodeKind.Ascii)
                return null;
            name = (string)s[pos].Value!;
            pos++;
        }

        if (pos >= s.Count || s[pos].Kind != EsfNodeKind.I32)
            return null;

        var subComponentCount = (int)s[pos].Value!;
        if (subComponentCount < 0)
            return null;
        pos++;

        var subComponents = new List<SubComponentInfo>(subComponentCount);
        for (var k = 0; k < subComponentCount; k++)
        {
            if (TryReadSubComponent(s, pos) is not { } sub)
                return null;

            subComponents.Add(sub);
            pos += sub.FieldCount;
        }

        return new ChannelInfo(start, pos - start, name, subComponents);
    }

    private static SubComponentInfo? TryReadSubComponent(IReadOnlyList<EsfNode> s, int start)
    {
        if (start + 1 >= s.Count || s[start].Kind != EsfNodeKind.Bool || s[start + 1].Kind != EsfNodeKind.I32)
            return null;

        var flag = (bool)s[start].Value!;
        var keyframeCount = (int)s[start + 1].Value!;

        // Bounds check guards against overflow/garbage the same way CurveDetector's does - a real
        // keyframe count can never exceed the number of fields left in the sibling list.
        if (keyframeCount < 0 || keyframeCount > s.Count - start - 2)
            return null;

        for (var b = 0; b < keyframeCount; b++)
            if (s[start + 2 + b].Kind != EsfNodeKind.Bool)
                return null;

        return new SubComponentInfo(start, 2 + keyframeCount, flag, keyframeCount);
    }
}
