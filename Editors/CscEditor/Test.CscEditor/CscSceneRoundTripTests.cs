using Editors.CscEditor.Data;
using Shared.GameFormats.Esf;

namespace Test.CscEditor
{
    public class CscSceneRoundTripTests
    {
        /// <summary>A ROOT v5 document with no elements, no attach entries and no tail.</summary>
        static byte[] BuildEmptySceneBytes()
        {
            var fields = new List<EsfNode>
            {
                // v5 header: F32, Coord3d, F32, Ascii + F32, Ascii, U32 + Coord3d, Coord3d, F32
                EsfNode.Leaf(EsfNodeKind.F32, 20f),
                EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)),
                EsfNode.Leaf(EsfNodeKind.F32, 0f),
                EsfNode.Leaf(EsfNodeKind.Ascii, ""),
                EsfNode.Leaf(EsfNodeKind.F32, 0f),
                EsfNode.Leaf(EsfNodeKind.Ascii, ""),
                EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
                EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)),
                EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)),
                EsfNode.Leaf(EsfNodeKind.F32, 1f),
                // element group count + attach group count
                EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
                EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
            };

            var doc = new EsfDocument
            {
                Signature = EsfSignature.Caab,
                Unknown1 = 0,
                CreationDate = 123456,
                Root = EsfNode.NewRecord("ROOT", 5, EsfRecordFlags.IsRecordNode, [fields]),
            };

            using var stream = new MemoryStream();
            EsfWriter.Write(doc, stream);
            return stream.ToArray();
        }

        [Test]
        public void Loads_an_empty_scene()
        {
            var scene = CscScene.Load(BuildEmptySceneBytes());

            Assert.That(scene.Elements, Is.Empty);
            Assert.That(scene.RootVersion, Is.EqualTo(5));
            Assert.That(scene.Duration, Is.EqualTo(20f));
            Assert.That(scene.ManifestKind, Is.EqualTo(CscManifestKind.None));
        }

        [Test]
        public void Edited_scene_duration_survives_save_and_reload()
        {
            var scene = CscScene.Load(BuildEmptySceneBytes());
            scene.Duration = 6.25f;

            var reloaded =
                CscScene.Load(CscSceneWriter.Write(scene));

            Assert.That(reloaded.Duration, Is.EqualTo(6.25f));
        }

        [Test]
        public void Inserted_elements_round_trip_with_hierarchy_channels_and_keyframes()
        {
            var scene = CscScene.Load(BuildEmptySceneBytes());

            var model = CscElementFactory.Create(scene, CscElementKind.Model, @"battle_props\thing.rigid_model_v2");
            scene.AddElement(model);

            var vfx = CscElementFactory.Create(scene, CscElementKind.Vfx, "my_effect");
            scene.AddElement(vfx, parent: model);
            vfx.AttachBoneIndex = 7;

            var camera = CscElementFactory.Create(scene, CscElementKind.Camera);
            scene.AddElement(camera);

            var light = CscElementFactory.Create(scene, CscElementKind.SpotLight);
            scene.AddElement(light);

            // Animate: a position curve on the model and an fov curve on the camera.
            model.Position!.Channels[1].AddKeyframe(0, 0);
            model.Position.Channels[1].AddKeyframe(5, 3.5f);
            camera.TypeGroups[0].Channels[0].AddKeyframe(0, 45);
            camera.TypeGroups[0].Channels[0].AddKeyframe(2, 90);

            var bytes = CscSceneWriter.Write(scene);
            var reloaded = CscScene.Load(bytes);

            Assert.That(reloaded.Elements, Has.Count.EqualTo(4));
            Assert.That(reloaded.RootElements, Has.Count.EqualTo(3));

            var reModel = reloaded.FindElement(model.Id)!;
            Assert.That(reModel.Kind, Is.EqualTo(CscElementKind.Model));
            Assert.That(reModel.AssetPath, Is.EqualTo(@"battle_props\thing.rigid_model_v2"));
            Assert.That(reModel.Children, Has.Count.EqualTo(1));
            Assert.That(reModel.Position!.Channels[1].Keyframes, Has.Count.EqualTo(2));
            Assert.That(reModel.Position.Channels[1].Evaluate(5), Is.EqualTo(3.5f).Within(0.001));

            var reVfx = reModel.Children[0];
            Assert.That(reVfx.Kind, Is.EqualTo(CscElementKind.Vfx));
            Assert.That(reVfx.AssetPath, Is.EqualTo("my_effect"));
            Assert.That(reVfx.Parent, Is.SameAs(reModel));
            Assert.That(reVfx.AttachBoneIndex, Is.EqualTo(7));

            var reCamera = reloaded.FindElement(camera.Id)!;
            Assert.That(reCamera.Kind, Is.EqualTo(CscElementKind.Camera));
            Assert.That(reCamera.TypeGroups, Has.Count.EqualTo(13));
            Assert.That(reCamera.TypeGroups[0].Channels[0].Keyframes, Has.Count.EqualTo(2));
            Assert.That(reCamera.TypeGroups[0].Channels[0].Evaluate(2), Is.EqualTo(90).Within(0.001));

            var reLight = reloaded.FindElement(light.Id)!;
            Assert.That(reLight.Kind, Is.EqualTo(CscElementKind.SpotLight));
            Assert.That(reLight.TypeGroups, Has.Count.EqualTo(6));
        }

        [Test]
        public void New_sfx_uses_the_input_as_the_first_start_event()
        {
            var scene = CscScene.Load(BuildEmptySceneBytes());
            var sfx = CscElementFactory.Create(scene, CscElementKind.Sfx, "Play_Battle_Horn");
            scene.AddElement(sfx);

            var reloaded = CscScene.Load(CscSceneWriter.Write(scene));
            var reSfx = reloaded.FindElement(sfx.Id)!;

            Assert.Multiple(() =>
            {
                Assert.That(reSfx.AssetPath, Is.EqualTo("Play_Battle_Horn"));
                Assert.That(reSfx.SfxStopEvent, Is.Empty);
                Assert.That(reSfx.SfxHasSecondEventPair, Is.True);
                Assert.That(reSfx.SfxEvent2Start, Is.Empty);
                Assert.That(reSfx.SfxEvent2Stop, Is.Empty);
            });
        }

        [Test]
        public void Reparenting_and_deleting_survive_a_round_trip()
        {
            var scene = CscScene.Load(BuildEmptySceneBytes());
            var a = CscElementFactory.Create(scene, CscElementKind.Locator);
            scene.AddElement(a);
            var b = CscElementFactory.Create(scene, CscElementKind.Locator);
            scene.AddElement(b);
            var c = CscElementFactory.Create(scene, CscElementKind.Locator);
            scene.AddElement(c, parent: b);

            // Move c under a; delete b.
            Assert.That(scene.Reparent(c, a), Is.True);
            scene.RemoveElementSubtree(b);

            // Cycles are rejected.
            Assert.That(scene.Reparent(a, c), Is.False);

            var reloaded = CscScene.Load(CscSceneWriter.Write(scene));
            Assert.That(reloaded.Elements, Has.Count.EqualTo(2));
            Assert.That(reloaded.RootElements, Has.Count.EqualTo(1));
            Assert.That(reloaded.RootElements[0].Id, Is.EqualTo(a.Id));
            Assert.That(reloaded.RootElements[0].Children[0].Id, Is.EqualTo(c.Id));
        }

        [Test]
        public void Load_rejects_duplicate_element_ids()
        {
            var bytes = BuildRawStructureScene(
                [0, 0],
                AttachGroup(-1, AttachChild(0), AttachChild(0)));

            var error = Assert.Throws<InvalidDataException>(() => CscScene.Load(bytes));
            Assert.That(error!.Message, Does.Contain("duplicate").IgnoreCase);
        }

        [Test]
        public void Load_rejects_an_unknown_parent_id()
        {
            var bytes = BuildRawStructureScene(
                [0],
                AttachGroup(99, AttachChild(0)));

            var error = Assert.Throws<InvalidDataException>(() => CscScene.Load(bytes));
            Assert.That(error!.Message, Does.Contain("parent").IgnoreCase);
        }

        [Test]
        public void Load_rejects_an_unknown_child_id()
        {
            var bytes = BuildRawStructureScene(
                [0],
                AttachGroup(-1, AttachChild(99)));

            var error = Assert.Throws<InvalidDataException>(() => CscScene.Load(bytes));
            Assert.That(error!.Message, Does.Contain("child").IgnoreCase);
        }

        [Test]
        public void Load_rejects_duplicate_or_missing_attach_children()
        {
            var bytes = BuildRawStructureScene(
                [0, 1],
                AttachGroup(-1, AttachChild(0), AttachChild(0)));

            var error = Assert.Throws<InvalidDataException>(() => CscScene.Load(bytes));
            Assert.That(error!.Message, Does.Contain("child").IgnoreCase);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Load_rejects_attach_cycles(bool selfCycle)
        {
            var bytes = selfCycle
                ? BuildRawStructureScene([0], AttachGroup(0, AttachChild(0)))
                : BuildRawStructureScene(
                    [0, 1],
                    AttachGroup(0, AttachChild(1)),
                    AttachGroup(1, AttachChild(0)));

            var error = Assert.Throws<InvalidDataException>(() => CscScene.Load(bytes));
            Assert.That(error!.Message, Does.Contain("cycle").IgnoreCase);
        }

        [Test]
        public void Load_rejects_negative_non_sentinel_element_ids()
        {
            var bytes = BuildRawStructureScene(
                [-2],
                AttachGroup(-1, AttachChild(-2)));

            var error = Assert.Throws<InvalidDataException>(() => CscScene.Load(bytes));
            Assert.That(error!.Message, Does.Contain("negative").IgnoreCase);
        }

        [Test]
        public void NextFreeElementId_finds_a_gap_without_overflowing()
        {
            var scene = CscScene.Load(BuildEmptySceneBytes());
            var high = CscElementFactory.Create(scene, CscElementKind.Locator);
            high.Id = int.MaxValue;
            scene.AddElement(high);

            Assert.That(scene.NextFreeElementId(), Is.EqualTo(0));

            var zero = CscElementFactory.Create(scene, CscElementKind.Locator);
            scene.AddElement(zero);
            Assert.That(scene.NextFreeElementId(), Is.EqualTo(1));
        }

        [Test]
        public void Writer_rejects_duplicate_and_negative_ids_before_serializing()
        {
            var duplicateScene = CscScene.Load(BuildEmptySceneBytes());
            var first = CscElementFactory.Create(duplicateScene, CscElementKind.Locator);
            duplicateScene.AddElement(first);
            var second = CscElementFactory.Create(duplicateScene, CscElementKind.Locator);
            second.Id = first.Id;
            duplicateScene.AddElement(second);

            Assert.Throws<InvalidDataException>(() => CscSceneWriter.Write(duplicateScene));

            var negativeScene = CscScene.Load(BuildEmptySceneBytes());
            var negative = CscElementFactory.Create(negativeScene, CscElementKind.Locator);
            negative.Id = -2;
            negativeScene.AddElement(negative);

            Assert.Throws<InvalidDataException>(() => CscSceneWriter.Write(negativeScene));
        }

        [Test]
        public void Attach_record_unknown_fields_groups_and_flags_survive_save()
        {
            var flags = EsfRecordFlags.IsRecordNode | EsfRecordFlags.HasNestedBlocks;
            var attachRecord = EsfNode.NewRecord(
                "ATTACH_TO_PARAMETERS",
                3,
                flags,
                [
                    [
                        EsfNode.Leaf(EsfNodeKind.U32, 7u, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.Ascii, "keep-first-group"),
                    ],
                    [
                        EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.I32, 42),
                    ],
                ]);

            var scene = CscScene.Load(BuildRawStructureScene(
                [0],
                AttachGroup(-1, AttachChild(0, attachRecord))));
            Assert.That(scene.Elements[0].AttachBoneIndex, Is.EqualTo(7));

            scene.Elements[0].AttachBoneIndex = 9;
            var reloaded = CscScene.Load(CscSceneWriter.Write(scene));
            var preserved = reloaded.Elements[0].AttachRecord!;

            Assert.That(preserved.Version, Is.EqualTo(3));
            Assert.That(preserved.RecordFlags, Is.EqualTo(flags));
            Assert.That(preserved.Groups, Has.Count.EqualTo(2));
            Assert.That(preserved.Groups![0][0].Value, Is.EqualTo(9u));
            Assert.That(preserved.Groups[0][1].Value, Is.EqualTo("keep-first-group"));
            Assert.That(preserved.Groups[1][0].Value, Is.EqualTo(true));
            Assert.That(preserved.Groups[1][1].Value, Is.EqualTo(42));
        }

        // -----------------------------------------------------------------
        // COMPOSITE_SCENE manifest handling
        // -----------------------------------------------------------------

        /// <summary>Locator elements plus a wrapped COMPOSITE_SCENE manifest (unnamed form).</summary>
        internal static byte[] BuildSceneWithManifestBytes(
            int elementCount = 1,
            bool corruptGroupCount = false,
            string entryName = "Test Entry",
            bool entryFlag = false,
            int reservedValue = 0)
        {
            var scratch = CscScene.Load(BuildEmptySceneBytes());
            for (var i = 0; i < elementCount; i++)
                scratch.AddElement(CscElementFactory.Create(scratch, CscElementKind.Locator));

            var fields = new List<EsfNode>(scratch.HeaderFields)
            {
                EsfNode.Leaf(EsfNodeKind.U32, (uint)elementCount, optimized: true),
            };
            foreach (var element in scratch.Elements)
            {
                fields.Add(EsfNode.Leaf(EsfNodeKind.Ascii, element.GroupLabel));
                fields.AddRange(element.Records);
            }

            fields.Add(EsfNode.Leaf(EsfNodeKind.U32, 1u, optimized: true));                       // attach group count
            fields.Add(EsfNode.Leaf(EsfNodeKind.U32, unchecked((uint)-1), optimized: true));     // parent -1
            fields.Add(EsfNode.Leaf(EsfNodeKind.U32, (uint)elementCount, optimized: true));      // child count
            foreach (var element in scratch.Elements)
            {
                fields.Add(EsfNode.Leaf(EsfNodeKind.U32, (uint)element.Id, optimized: true));
                fields.Add(EsfNode.NewRecord("ATTACH_TO_PARAMETERS", 0, EsfRecordFlags.IsRecordNode,
                    [[EsfNode.Leaf(EsfNodeKind.U32, unchecked((uint)-1), optimized: true)]]));
            }

            // Each locator has one unnamed group with 4 channels (position 3, orientation 3,
            // scale 1, weight 1 sub-components), all zero keyframes.
            var manifest = new List<EsfNode>
            {
                EsfNode.Leaf(
                    EsfNodeKind.I32,
                    corruptGroupCount ? elementCount + 1 : elementCount,
                    optimized: true),
            };
            foreach (var element in scratch.Elements)
            {
                manifest.Add(EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true));  // marker: unnamed
                manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 4, optimized: true));   // channel count
                foreach (var subCount in new[] { 3, 3, 1, 1 })
                {
                    manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, subCount, optimized: true));
                    for (var i = 0; i < subCount; i++)
                    {
                        manifest.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
                        manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true)); // keyframe count
                    }
                }
            }
            manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 1, optimized: true));         // entry count
            manifest.Add(EsfNode.Leaf(EsfNodeKind.Ascii, entryName));
            manifest.Add(EsfNode.Leaf(EsfNodeKind.Bool, entryFlag, optimized: true));
            manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true));
            manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, reservedValue, optimized: true));
            manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true));
            manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, elementCount, optimized: true));
            foreach (var element in scratch.Elements)
                manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, element.Id, optimized: true));
            manifest.Add(EsfNode.Leaf(EsfNodeKind.I32, 0, optimized: true));         // footer unit: no children

            fields.Add(EsfNode.NewRecord("COMPOSITE_SCENE", 0, EsfRecordFlags.IsRecordNode, [manifest]));

            var doc = new EsfDocument
            {
                Signature = EsfSignature.Caab,
                Unknown1 = 0,
                CreationDate = 123456,
                Root = EsfNode.NewRecord("ROOT", 5, EsfRecordFlags.IsRecordNode, [fields]),
            };

            using var stream = new MemoryStream();
            EsfWriter.Write(doc, stream);
            return stream.ToArray();
        }

        [Test]
        public void Manifest_keyframe_counts_are_updated_when_curves_change()
        {
            var scene = CscScene.Load(BuildSceneWithManifestBytes());
            Assert.That(scene.ManifestParsed, Is.True);
            Assert.That(scene.ManifestEntries, Has.Count.EqualTo(1));

            var element = scene.Elements[0];
            Assert.That(element.ManifestGroupFields, Is.Not.Null);

            // Add two keyframes to position X, then save + reload.
            element.Position!.Channels[0].AddKeyframe(0, 0);
            element.Position.Channels[0].AddKeyframe(3, 5);

            var reloaded = CscScene.Load(CscSceneWriter.Write(scene));
            Assert.That(reloaded.ManifestParsed, Is.True);
            Assert.That(reloaded.Elements[0].Position!.Channels[0].Keyframes, Has.Count.EqualTo(2));

            // The reloaded manifest slice restates the new count: parse it back and check.
            var slice = reloaded.Elements[0].ManifestGroupFields!;
            var groups = Shared.GameFormats.Csc.CompositeSceneDetector.DetectGroups(slice);
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Channels[0].SubComponents[0].KeyframeCount, Is.EqualTo(2));

            // The named entry survived too.
            Assert.That(reloaded.ManifestEntries[0].Fields[0].Value, Is.EqualTo("Test Entry"));
        }

        [Test]
        public void New_elements_get_a_manifest_group_when_the_scene_has_a_manifest()
        {
            var scene = CscScene.Load(BuildSceneWithManifestBytes());
            var vfx = CscElementFactory.Create(scene, CscElementKind.Vfx, "sparkles");
            scene.AddElement(vfx, parent: scene.Elements[0]);

            var reloaded = CscScene.Load(CscSceneWriter.Write(scene));
            Assert.That(reloaded.ManifestParsed, Is.True);
            Assert.That(reloaded.Elements, Has.Count.EqualTo(2));

            var reVfx = reloaded.FindElement(vfx.Id)!;
            Assert.That(reVfx.ManifestGroupFields, Is.Not.Null,
                "the new element should have been given a manifest group on save");

            // Entry section: the original entry plus a generated one for the new element.
            Assert.That(reloaded.ManifestEntries, Has.Count.EqualTo(2));

            // Footer adjacency: the parent's entry should now list the child's entry index.
            // (Verified indirectly: the reloaded manifest parsed cleanly, which requires the
            // footer to be exactly entryCount units.)
        }

        [Test]
        public void Deleting_one_member_of_a_multi_id_entry_preserves_the_survivor_metadata()
        {
            var scene = CscScene.Load(BuildSceneWithManifestBytes(
                elementCount: 2,
                entryName: "Animation Track",
                entryFlag: true,
                reservedValue: 170));
            var survivorId = scene.Elements[1].Id;

            scene.RemoveElementSubtree(scene.Elements[0]);
            var reloaded = CscScene.Load(CscSceneWriter.Write(scene));

            Assert.That(reloaded.ManifestEntries, Has.Count.EqualTo(1));
            var entry = reloaded.ManifestEntries[0];
            Assert.Multiple(() =>
            {
                Assert.That(entry.Fields[0].Value, Is.EqualTo("Animation Track"));
                Assert.That(entry.Fields[1].Value, Is.EqualTo(true));
                Assert.That(entry.Fields[2].Value, Is.EqualTo(0));
                Assert.That(entry.Fields[3].Value, Is.EqualTo(170));
                Assert.That(entry.Fields[4].Value, Is.EqualTo(0));
                Assert.That(entry.Fields[5].Value, Is.EqualTo(1));
                Assert.That(entry.ElementIds, Is.EqualTo(new[] { survivorId }));
            });
        }

        [TestCase("add")]
        [TestCase("delete")]
        [TestCase("reparent")]
        public void Structural_edits_are_rejected_when_the_manifest_cannot_be_parsed(string operation)
        {
            var scene = CscScene.Load(BuildSceneWithManifestBytes(
                elementCount: 2,
                corruptGroupCount: true));
            Assert.That(scene.ManifestParsed, Is.False);

            TestDelegate edit = operation switch
            {
                "add" => () => scene.AddElement(CscElementFactory.Create(scene, CscElementKind.Locator)),
                "delete" => () => scene.RemoveElementSubtree(scene.Elements[0]),
                "reparent" => () => scene.Reparent(scene.Elements[1], scene.Elements[0]),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };

            Assert.Throws<InvalidOperationException>(edit);
            Assert.That(scene.Elements, Has.Count.EqualTo(2));
            Assert.That(scene.RootElements, Has.Count.EqualTo(2));
        }

        static (int ChildId, EsfNode Record) AttachChild(int childId, EsfNode? record = null) =>
            (childId, record ?? EsfNode.NewRecord(
                "ATTACH_TO_PARAMETERS",
                0,
                EsfRecordFlags.IsRecordNode,
                [[EsfNode.Leaf(EsfNodeKind.U32, unchecked((uint)-1), optimized: true)]]));

        static (int ParentId, IReadOnlyList<(int ChildId, EsfNode Record)> Children) AttachGroup(
            int parentId,
            params (int ChildId, EsfNode Record)[] children) =>
            (parentId, children);

        static byte[] BuildRawStructureScene(
            IReadOnlyList<int> elementIds,
            params (int ParentId, IReadOnlyList<(int ChildId, EsfNode Record)> Children)[] attachGroups)
        {
            var scratch = CscScene.Load(BuildEmptySceneBytes());
            var fields = new List<EsfNode>(scratch.HeaderFields)
            {
                EsfNode.Leaf(EsfNodeKind.U32, (uint)elementIds.Count, optimized: true),
            };

            foreach (var id in elementIds)
            {
                var element = CscElementFactory.Create(scratch, CscElementKind.Locator);
                fields.Add(EsfNode.Leaf(EsfNodeKind.Ascii, element.GroupLabel));
                foreach (var record in element.Records)
                {
                    if (record != element.ElementRecord)
                    {
                        fields.Add(record);
                        continue;
                    }

                    var groups = record.Groups!.Select(g => g.ToList()).ToList();
                    groups[0][0] = EsfNode.Leaf(EsfNodeKind.U32, unchecked((uint)id), optimized: true);
                    fields.Add(EsfNode.NewRecord(record.Name!, record.Version, record.RecordFlags, groups));
                }
            }

            fields.Add(EsfNode.Leaf(EsfNodeKind.U32, (uint)attachGroups.Length, optimized: true));
            foreach (var (parentId, children) in attachGroups)
            {
                fields.Add(EsfNode.Leaf(EsfNodeKind.U32, unchecked((uint)parentId), optimized: true));
                fields.Add(EsfNode.Leaf(EsfNodeKind.U32, (uint)children.Count, optimized: true));
                foreach (var (childId, record) in children)
                {
                    fields.Add(EsfNode.Leaf(EsfNodeKind.U32, unchecked((uint)childId), optimized: true));
                    fields.Add(record);
                }
            }

            var doc = new EsfDocument
            {
                Signature = EsfSignature.Caab,
                Unknown1 = 0,
                CreationDate = 123456,
                Root = EsfNode.NewRecord("ROOT", 5, EsfRecordFlags.IsRecordNode, [fields]),
            };

            using var stream = new MemoryStream();
            EsfWriter.Write(doc, stream);
            return stream.ToArray();
        }
    }
}
