using Editors.CscEditor.Data;
using Shared.GameFormats.Esf;

namespace Test.CscEditor
{
    public class CscNestedElementTests
    {
        const EsfRecordFlags V100Flags = EsfRecordFlags.IsRecordNode | EsfRecordFlags.HasNonOptimizedInfo;

        static EsfNode BuildElementRecord(int id)
        {
            var fields = new List<EsfNode>
            {
                EsfNode.Leaf(EsfNodeKind.U32, (uint)id, optimized: true),
                EsfNode.Leaf(EsfNodeKind.Ascii, ""),
                EsfNode.Leaf(EsfNodeKind.F32, 0f, optimized: true),
                EsfNode.Leaf(EsfNodeKind.F32, 10f),
                EsfNode.Leaf(EsfNodeKind.Ascii, "infinite"),
                EsfNode.Leaf(EsfNodeKind.Ascii, "free"),
            };

            // position(3), rotation(3), scale(1), weight(1) groups, all static.
            foreach (var headers in new[] { new float[] { 0, 0, 0 }, [0, 0, 0], [1], [100] })
                CscSceneWriter.AppendChannelGroup(fields, CscChannelGroup.CreateStatic(headers));

            fields.Add(EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
            return EsfNode.NewRecord("ELEMENT", 100, V100Flags, [fields]);
        }

        static List<EsfNode> BuildManifestGroupFields(params int[] subCounts)
        {
            var fields = new List<EsfNode>
            {
                EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),                  // marker: unnamed
                EsfNode.Leaf(EsfNodeKind.I32, subCounts.Length, optimized: true),    // channel count
            };
            foreach (var subCount in subCounts)
            {
                fields.Add(EsfNode.Leaf(EsfNodeKind.I32, subCount, optimized: true));
                for (var i = 0; i < subCount; i++)
                {
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true));   // keyframe count
                }
            }
            return fields;
        }

        /// <summary>
        /// One top-level variant-model element (id 0) whose type record carries a nested
        /// ELEMENT + ANIMATION_ELEMENT bundle (id 1) - the porthole-scene layout - plus a
        /// COMPOSITE_SCENE manifest with a group for BOTH elements.
        /// </summary>
        static byte[] BuildSceneBytes()
        {
            var variantModelRecord = EsfNode.NewRecord("VARIANT_MODEL_ELEMENT", 100, V100Flags,
            [[
                EsfNode.Leaf(EsfNodeKind.Ascii, @"variantmeshes\test.wsmodel"),
                EsfNode.Leaf(EsfNodeKind.U32, 3u, optimized: true),
                BuildElementRecord(1),
                EsfNode.NewRecord("ANIMATION_ELEMENT", 100, V100Flags,
                    [[EsfNode.Leaf(EsfNodeKind.Ascii, @"animations\battle\humanoid01\stand_idle.anim")]]),
                EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
            ]]);

            var fields = new List<EsfNode>
            {
                // ROOT v3 header: F32, Coord3d, F32, Ascii
                EsfNode.Leaf(EsfNodeKind.F32, 20f),
                EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)),
                EsfNode.Leaf(EsfNodeKind.F32, 0f),
                EsfNode.Leaf(EsfNodeKind.Ascii, ""),
                // one element group
                EsfNode.Leaf(EsfNodeKind.U32, 1u, optimized: true),
                EsfNode.Leaf(EsfNodeKind.Ascii, "model"),
                BuildElementRecord(0),
                variantModelRecord,
                // attach tree: only the top-level element (nested ones are not in it)
                EsfNode.Leaf(EsfNodeKind.U32, 1u, optimized: true),
                EsfNode.Leaf(EsfNodeKind.U32, unchecked((uint)-1), optimized: true),
                EsfNode.Leaf(EsfNodeKind.U32, 1u, optimized: true),
                EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
                EsfNode.NewRecord("ATTACH_TO_PARAMETERS", 0, EsfRecordFlags.IsRecordNode,
                    [[EsfNode.Leaf(EsfNodeKind.U32, unchecked((uint)-1), optimized: true)]]),
            };

            // Manifest: 2 groups (carrier + nested, both 3/3/1/1), 2 entries, empty footer units.
            var manifest = new List<EsfNode> { EsfNode.Leaf(EsfNodeKind.I32, 2, optimized: true) };
            manifest.AddRange(BuildManifestGroupFields(3, 3, 1, 1));
            manifest.AddRange(BuildManifestGroupFields(3, 3, 1, 1));
            manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 2, optimized: true));
            foreach (var id in new[] { 0, 1 })
            {
                manifest.Add(EsfNode.Leaf(EsfNodeKind.Ascii, ""));
                manifest.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
                manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true));
                manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true));
                manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true));
                manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true));
                manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, id, optimized: true));
            }
            manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true));
            manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true));

            fields.Add(EsfNode.NewRecord("COMPOSITE_SCENE", 0, EsfRecordFlags.IsRecordNode, [manifest]));

            var doc = new EsfDocument
            {
                Signature = EsfSignature.Caab,
                Unknown1 = 0,
                CreationDate = 1,
                Root = EsfNode.NewRecord("ROOT", 3, EsfRecordFlags.IsRecordNode, [fields]),
            };

            using var stream = new MemoryStream();
            EsfWriter.Write(doc, stream);
            return stream.ToArray();
        }

        [Test]
        public void Nested_elements_are_decoded_and_the_manifest_still_parses()
        {
            var scene = CscScene.Load(BuildSceneBytes());

            Assert.That(scene.Elements, Has.Count.EqualTo(1));
            var carrier = scene.Elements[0];
            Assert.That(carrier.Kind, Is.EqualTo(CscElementKind.VariantModel));

            var nested = carrier.Children.Single();
            Assert.That(nested.IsNested, Is.True);
            Assert.That(nested.Id, Is.EqualTo(1));
            Assert.That(nested.Kind, Is.EqualTo(CscElementKind.Animation));
            Assert.That(nested.AssetPath, Does.EndWith(".anim"));
            Assert.That(nested.Parent, Is.SameAs(carrier));

            Assert.That(scene.ManifestParsed, Is.True);
            Assert.That(nested.ManifestGroupFields, Is.Not.Null);
        }

        [Test]
        public void Nested_element_edits_round_trip_inside_the_carrier_record()
        {
            var scene = CscScene.Load(BuildSceneBytes());
            var nested = scene.Elements[0].Children.Single();

            nested.Position!.Channels[2].AddKeyframe(0, 1);
            nested.Position.Channels[2].AddKeyframe(4, 2.5f);
            nested.Begin = 1.5f;

            var reloaded = CscScene.Load(CscSceneWriter.Write(scene));
            Assert.That(reloaded.ManifestParsed, Is.True);

            var reNested = reloaded.Elements[0].Children.Single();
            Assert.That(reNested.IsNested, Is.True);
            Assert.That(reNested.Begin, Is.EqualTo(1.5f));
            Assert.That(reNested.Position!.Channels[2].Keyframes, Has.Count.EqualTo(2));
            Assert.That(reNested.Position.Channels[2].Evaluate(4), Is.EqualTo(2.5f).Within(0.001));

            // The manifest restated the new keyframe count for the nested element's group.
            var groups = Shared.GameFormats.Csc.CompositeSceneDetector.DetectGroups(reNested.ManifestGroupFields!);
            Assert.That(groups[0].Channels[0].SubComponents[2].KeyframeCount, Is.EqualTo(2));
        }

        [Test]
        public void Nested_elements_cannot_be_reparented_or_removed()
        {
            var scene = CscScene.Load(BuildSceneBytes());
            var carrier = scene.Elements[0];
            var nested = carrier.Children.Single();

            Assert.That(scene.Reparent(nested, null), Is.False);
            scene.RemoveElementSubtree(nested);
            Assert.That(carrier.Children, Has.Count.EqualTo(1), "nested element must survive removal attempts");
        }
    }
}
