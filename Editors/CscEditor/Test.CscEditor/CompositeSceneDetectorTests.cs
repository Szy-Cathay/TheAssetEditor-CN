using Shared.GameFormats.Csc;
using Shared.GameFormats.Esf;

namespace Test.CscEditor
{
    // Focused, isolated tests of CompositeSceneDetector's own grammar parsing (Group*, EntrySection,
    // Footer), independent of the full CscScene/CscSceneWriter pipeline - CscSceneRoundTripTests
    // already covers the writer producing a manifest the detector can re-parse after a save+reload;
    // these cover the detector's own "reading" side directly against hand-built field lists,
    // including the shapes it's supposed to reject.
    public class CompositeSceneDetectorTests
    {
        [Test]
        public void DetectGroups_reads_named_and_unnamed_groups_with_multiple_channels_and_subcomponents()
        {
            var fields = new List<EsfNode>();

            // Group 0: unnamed (marker 0), one channel, two sub-components.
            fields.Add(EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.I32, 2, optimized: true)); // sub-component count
            fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true)); // keyframe count
            fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true)); // keyframe count

            // Group 1: named (marker 1), two channels, each with one empty sub-component.
            fields.Add(EsfNode.Leaf(EsfNodeKind.U32, 1u, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.I32, 2, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Ascii, "camera_fov"));
            fields.Add(EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Ascii, "camera_roll"));
            fields.Add(EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.I32, 3, optimized: true)); // keyframe count
            fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));

            var groups = CompositeSceneDetector.DetectGroups(fields);

            Assert.That(groups, Has.Count.EqualTo(2));

            Assert.That(groups[0].Named, Is.False);
            Assert.That(groups[0].Channels, Has.Count.EqualTo(1));
            Assert.That(groups[0].Channels[0].Name, Is.Null);
            Assert.That(groups[0].Channels[0].SubComponents, Has.Count.EqualTo(2));
            Assert.That(groups[0].Channels[0].SubComponents[0].Flag, Is.True);
            Assert.That(groups[0].Channels[0].SubComponents[0].KeyframeCount, Is.EqualTo(1));
            Assert.That(groups[0].Channels[0].SubComponents[1].Flag, Is.False);
            Assert.That(groups[0].Channels[0].SubComponents[1].KeyframeCount, Is.EqualTo(0));

            Assert.That(groups[1].Named, Is.True);
            Assert.That(groups[1].Channels, Has.Count.EqualTo(2));
            Assert.That(groups[1].Channels[0].Name, Is.EqualTo("camera_fov"));
            Assert.That(groups[1].Channels[0].SubComponents[0].KeyframeCount, Is.EqualTo(0));
            Assert.That(groups[1].Channels[1].Name, Is.EqualTo("camera_roll"));
            Assert.That(groups[1].Channels[1].SubComponents[0].KeyframeCount, Is.EqualTo(3));

            // Groups are contiguous and non-overlapping: the second starts exactly where the first ends.
            Assert.That(groups[1].StartIndex, Is.EqualTo(groups[0].StartIndex + groups[0].FieldCount));
        }

        [Test]
        public void DetectGroups_skips_a_marker_field_that_is_not_0_or_1()
        {
            // marker == 2 is not a valid group leader - TryReadGroup should reject it and DetectGroups
            // should just step past it rather than misinterpreting unrelated fields as a group.
            var fields = new List<EsfNode>
            {
                EsfNode.Leaf(EsfNodeKind.U32, 2u, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
            };

            var groups = CompositeSceneDetector.DetectGroups(fields);

            Assert.That(groups, Is.Empty);
        }

        [Test]
        public void DetectGroups_does_not_throw_when_a_group_claims_more_fields_than_exist()
        {
            // channelCount says 5 but there's only one channel's worth of fields present - a
            // truncated/corrupt manifest should fail to parse that group, not crash or read garbage.
            var fields = new List<EsfNode>
            {
                EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 5, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
            };

            List<CompositeSceneDetector.GroupInfo> groups = null!;
            Assert.DoesNotThrow(() => groups = CompositeSceneDetector.DetectGroups(fields));
            Assert.That(groups, Is.Empty);
        }

        [Test]
        public void CurveDetector_does_not_index_past_a_truncated_keyframe()
        {
            var fields = new List<EsfNode>
            {
                EsfNode.Leaf(EsfNodeKind.U32, 1u, optimized: true),
                EsfNode.Leaf(EsfNodeKind.F32, 0f, optimized: true),
                EsfNode.Leaf(EsfNodeKind.U32, 1u, optimized: true),
                EsfNode.Leaf(EsfNodeKind.F32, 0f, optimized: true),
            };

            List<CurveDetector.GroupRun> groups = null!;
            Assert.DoesNotThrow(() => groups = CurveDetector.DetectGroups(fields));
            Assert.That(groups, Is.Empty);
        }

        [Test]
        public void DetectEntrySection_reads_multiple_entries_with_their_own_element_ids()
        {
            var fields = new List<EsfNode>
            {
                EsfNode.Leaf(EsfNodeKind.I32, 2, optimized: true), // entry count

                // Entry 0: named, references two elements.
                EsfNode.Leaf(EsfNodeKind.Ascii, "Animation Track"),
                EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 2, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 10, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 11, optimized: true),

                // Entry 1: unnamed, no element ids.
                EsfNode.Leaf(EsfNodeKind.Ascii, ""),
                EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
            };

            var section = CompositeSceneDetector.DetectEntrySection(fields, 0);

            Assert.That(section, Is.Not.Null);
            Assert.That(section!.Entries, Has.Count.EqualTo(2));
            Assert.That(section.Entries[0].Name, Is.EqualTo("Animation Track"));
            Assert.That(section.Entries[0].ElementIds, Is.EqualTo(new[] { 10, 11 }));
            Assert.That(section.Entries[1].Name, Is.EqualTo(""));
            Assert.That(section.Entries[1].ElementIds, Is.Empty);
            Assert.That(section.FieldCount, Is.EqualTo(fields.Count));
        }

        [Test]
        public void DetectEntrySection_returns_null_when_the_leading_field_is_not_a_plausible_count()
        {
            var fields = new List<EsfNode> { EsfNode.Leaf(EsfNodeKind.Ascii, "not a count") };

            Assert.That(CompositeSceneDetector.DetectEntrySection(fields, 0), Is.Null);
        }

        [Test]
        public void DetectFooter_reads_units_of_varying_length_as_an_adjacency_list()
        {
            var fields = new List<EsfNode>
            {
                EsfNode.Leaf(EsfNodeKind.I32, 2, optimized: true), // unit 0: two children
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true), // unit 1: leaf, no children
            };

            var footer = CompositeSceneDetector.DetectFooter(fields, 0, unitCount: 2);

            Assert.That(footer, Is.Not.Null);
            Assert.That(footer!.Units, Has.Count.EqualTo(2));
            Assert.That(footer.Units[0].Values, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(footer.Units[1].Values, Is.Empty);
        }

        [Test]
        public void DetectFooter_returns_null_when_declared_unit_count_does_not_fit_available_fields()
        {
            var fields = new List<EsfNode> { EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true) };

            // Two units claimed but only one fits in the field list.
            Assert.That(CompositeSceneDetector.DetectFooter(fields, 0, unitCount: 2), Is.Null);
        }

        [Test]
        public void Full_grammar_groups_then_entry_section_then_footer_parses_end_to_end()
        {
            var fields = new List<EsfNode>
            {
                // One unnamed group, one channel, one sub-component, one keyframe.
                EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true),
                EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true),
                EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true),

                // Entry section: one entry pointing at element id 42.
                EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true),
                EsfNode.Leaf(EsfNodeKind.Ascii, ""),
                EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true),
                EsfNode.Leaf(EsfNodeKind.I32, 42, optimized: true),

                // Footer: exactly one unit (matching the one entry above), no children.
                EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true),
            };

            var groups = CompositeSceneDetector.DetectGroups(fields);
            Assert.That(groups, Has.Count.EqualTo(1));
            var afterGroups = groups[0].StartIndex + groups[0].FieldCount;

            var entrySection = CompositeSceneDetector.DetectEntrySection(fields, afterGroups);
            Assert.That(entrySection, Is.Not.Null);
            Assert.That(entrySection!.Entries[0].ElementIds, Is.EqualTo(new[] { 42 }));
            var afterEntries = entrySection.StartIndex + entrySection.FieldCount;

            var footer = CompositeSceneDetector.DetectFooter(fields, afterEntries, entrySection.Entries.Count);
            Assert.That(footer, Is.Not.Null);
            Assert.That(footer!.Units, Has.Count.EqualTo(1));
            Assert.That(footer.Units[0].Values, Is.Empty);

            // The whole field list is accounted for - nothing left over.
            Assert.That(footer.StartIndex + footer.FieldCount, Is.EqualTo(fields.Count));
        }
    }
}
