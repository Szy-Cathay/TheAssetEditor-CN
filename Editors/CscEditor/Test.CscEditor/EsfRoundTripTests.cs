using Shared.GameFormats.Esf;

namespace Test.CscEditor
{
    public class EsfRoundTripTests
    {
        /// <summary>
        /// Exact bytes of a real, minimal Warhammer 3 .csc sample
        /// (composite_scene/spells_and_abilities/empty.csc, 74 bytes - CA's own placeholder for
        /// an "empty" composite scene), decoded by hand byte-by-byte against RPFM's algorithm.
        /// Embedded directly rather than shipped as a file so the test doesn't depend on
        /// redistributing Creative Assembly's game assets.
        /// </summary>
        static readonly byte[] EmptyCscBytes =
        [
            0xCA, 0xAB, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xB8, 0x42, 0x8A, 0x5D, 0x34, 0x00, 0x00, 0x00,
            0x80, 0x00, 0x00, 0x03, 0x1F, 0x0A, 0x00, 0x00, 0x20, 0x41, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x80, 0x3F, 0x0F, 0x00, 0x00, 0x00,
            0x00, 0x14, 0x14, 0x19, 0x01, 0x00, 0x04, 0x00, 0x52, 0x4F, 0x4F, 0x54, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];

        [Test]
        public void Reads_header_and_root_record()
        {
            var doc = EsfReader.Read(new MemoryStream(EmptyCscBytes));

            Assert.That(doc.Signature, Is.EqualTo(EsfSignature.Caab));
            Assert.That(doc.Unknown1, Is.EqualTo(0u));
            Assert.That(doc.CreationDate, Is.EqualTo(0x5D8A42B8u));
            Assert.That(doc.Root.IsRecord, Is.True);
            Assert.That(doc.Root.Name, Is.EqualTo("ROOT"));
            Assert.That(doc.Root.Version, Is.EqualTo(3));
            Assert.That(doc.Root.Groups, Has.Count.EqualTo(1));
            Assert.That(doc.Root.Groups![0], Has.Count.EqualTo(7));
        }

        [Test]
        public void Reads_every_child_field_with_correct_kind_value_and_optimization_flag()
        {
            var doc = EsfReader.Read(new MemoryStream(EmptyCscBytes));
            var children = doc.Root.Groups![0];

            Assert.That(children[0].Kind, Is.EqualTo(EsfNodeKind.F32));
            Assert.That(children[0].Value, Is.EqualTo(10.0f));
            Assert.That(children[0].Optimized, Is.False);

            Assert.That(children[1].Kind, Is.EqualTo(EsfNodeKind.Coord3d));
            Assert.That(children[1].Value, Is.EqualTo(new Coord3d(0, 0, 0)));

            Assert.That(children[2].Value, Is.EqualTo(1.0f));
            Assert.That(children[3].Kind, Is.EqualTo(EsfNodeKind.Ascii));
            Assert.That(children[3].Value, Is.EqualTo(""));

            Assert.That(children[4].Kind, Is.EqualTo(EsfNodeKind.U32));
            Assert.That(children[4].Value, Is.EqualTo(0u));
            Assert.That(children[4].Optimized, Is.True);

            Assert.That(children[6].Kind, Is.EqualTo(EsfNodeKind.I32));
            Assert.That(children[6].Optimized, Is.True);
        }

        [Test]
        public void Round_trips_the_real_file_byte_for_byte()
        {
            var doc = EsfReader.Read(new MemoryStream(EmptyCscBytes));

            using var output = new MemoryStream();
            EsfWriter.Write(doc, output);

            Assert.That(output.ToArray(), Is.EqualTo(EmptyCscBytes));
        }

        [Test]
        public void Rejects_files_with_an_unrecognized_signature()
        {
            var bytes = (byte[])EmptyCscBytes.Clone();
            bytes[0] = 0xFF;

            Assert.Throws<InvalidDataException>(() => EsfReader.Read(new MemoryStream(bytes)));
        }

        [Test]
        public void Round_trips_a_cbab_document_with_both_string_kinds()
        {
            // CBAB differs from CAAB only in string-table length-prefix widths, so the document
            // deliberately carries both an Ascii and a Utf16 string.
            var original = new EsfDocument
            {
                Signature = EsfSignature.Cbab,
                Unknown1 = 0,
                CreationDate = 1_569_309_368,
                Root = EsfNode.NewRecord("ROOT", 1, EsfRecordFlags.IsRecordNode, [[
                    EsfNode.Leaf(EsfNodeKind.Ascii, "battle_props/example.wsmodel"),
                    EsfNode.Leaf(EsfNodeKind.Utf16, "wide-prefix string"),
                    EsfNode.Leaf(EsfNodeKind.U32, 3u, optimized: true),
                ]]),
            };

            using var output = new MemoryStream();
            EsfWriter.Write(original, output);
            Assert.That(output.ToArray()[0], Is.EqualTo(0xCB));

            output.Position = 0;
            var reDecoded = EsfReader.Read(output);

            Assert.That(reDecoded.Signature, Is.EqualTo(EsfSignature.Cbab));
            var fields = reDecoded.Root.Groups![0];
            Assert.That(fields[0].Value, Is.EqualTo("battle_props/example.wsmodel"));
            Assert.That(fields[1].Value, Is.EqualTo("wide-prefix string"));
            Assert.That(fields[2].Value, Is.EqualTo(3u));
        }

        [Test]
        public void Unknown26_keeps_its_trailing_9c_byte_when_round_tripped()
        {
            var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 0x9C };
            var original = new EsfDocument
            {
                Signature = EsfSignature.Caab,
                Unknown1 = 0,
                CreationDate = 1,
                Root = EsfNode.NewRecord("ROOT", 1, EsfRecordFlags.IsRecordNode,
                    [[EsfNode.Leaf(EsfNodeKind.Unknown26, payload)]]),
            };

            using var firstWrite = new MemoryStream();
            EsfWriter.Write(original, firstWrite);
            var originalBytes = firstWrite.ToArray();

            firstWrite.Position = 0;
            var decoded = EsfReader.Read(firstWrite);
            Assert.That((byte[])decoded.Root.Groups![0][0].Value!, Is.EqualTo(payload));

            using var secondWrite = new MemoryStream();
            EsfWriter.Write(decoded, secondWrite);
            Assert.That(secondWrite.ToArray(), Is.EqualTo(originalBytes));
        }

        [TestCase(new byte[] { 0x0A }, 10u)]
        [TestCase(new byte[] { 0x80, 0x0A }, 10u)]
        public void Cauleb128_decodes_CAs_big_endian_style_varint(byte[] bytes, uint expected)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            Assert.That(reader.ReadCauleb128(), Is.EqualTo(expected));
        }
    }
}
