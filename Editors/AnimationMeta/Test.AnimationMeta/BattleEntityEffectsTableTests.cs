using System.IO;
using System.Text;
using Shared.GameFormats.DB;

namespace Test.AnimationMeta;

public class BattleEntityEffectsTableTests
{
    [Test]
    public void Wh3Version2_MapsSerializedColumnsToTerrainOrderAndConsumesExtraColumns()
    {
        var bytes = Table(2, writer => WriteRow(writer, "same", ["deep", "forest", "grass", "mud", "rock", "road", "sand", "scrub", "shallow", "snow", "wood"], "shake"));
        var rows = BattleEntityEffectsTable.Read(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Name, Is.EqualTo("same"));
            Assert.That(rows[0].ParticleKeys, Is.EqualTo(new[] { "forest", "grass", "mud", "sand", "scrub", "rock", "deep", "shallow", "road", "wood", "snow" }));
            Assert.That(rows[0].GetEffect(-1), Is.Null);
            Assert.That(rows[0].GetEffect(11), Is.Null);
        });
    }

    [Test]
    public void NullAndPresentEmptyFieldsRemainDistinctAndUtf8UsesByteLength()
    {
        var bytes = Table(2, writer => WriteRow(writer, "雪地", [null, "", "粒子_草地", null, null, null, null, null, null, null, null], null));
        var row = BattleEntityEffectsTable.Read(bytes)[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.Name, Is.EqualTo("雪地"));
            Assert.That(row.GetEffect(6), Is.Null);
            Assert.That(row.GetEffect(0), Is.Empty);
            Assert.That(row.GetEffect(1), Is.EqualTo("粒子_草地"));
        });
    }

    [Test]
    public void RepeatedKeysRemainInFileOrderForThePackLayerToResolve()
    {
        var bytes = Table(2, writer =>
        {
            WriteRow(writer, "same", [null, null, "first", null, null, null, null, null, null, null, null], null);
            WriteRow(writer, "same", [null, null, "second", null, null, null, null, null, null, null, null], null);
        }, 2);
        Assert.That(BattleEntityEffectsTable.Read(bytes).Select(row => row.GetEffect(1)), Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void EachTruncationOfAValidRowIsRejected()
    {
        var bytes = Table(2, writer => WriteRow(writer, "entry", ["deep", "forest", "grass", "mud", "rock", "road", "sand", "scrub", "shallow", "snow", "wood"], "shake"));
        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = bytes[..length];
            Assert.That(() => BattleEntityEffectsTable.Read(truncated), Throws.TypeOf<InvalidDataException>(), $"Length {length} must not yield a partial table.");
        }
    }

    [Test]
    public void InvalidBooleanImpossibleCountUnknownVersionAndTrailingDataAreRejected()
    {
        var valid = Table(2, writer => WriteRow(writer, "x", new string?[11], null));
        var badBoolean = (byte[])valid.Clone();
        badBoolean[13] = 2;
        var badCount = (byte[])valid.Clone();
        Array.Fill(badCount, (byte)0xff, 9, 4);
        Assert.Multiple(() =>
        {
            Assert.That(() => BattleEntityEffectsTable.Read(badBoolean), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => BattleEntityEffectsTable.Read(badCount), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => BattleEntityEffectsTable.Read(Table(1, _ => { }, 0)), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => BattleEntityEffectsTable.Read([.. valid, 0]), Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void MalformedUtf8CannotBecomeAResourceLookupKey()
    {
        var bytes = Table(2, writer => WriteRow(writer, "xx", new string?[11], null));
        bytes[19] = 0xc0;
        bytes[20] = 0xaf;
        Assert.That(() => BattleEntityEffectsTable.Read(bytes), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ParticleEffectsVersion0_ReadsOnlyExactKeysWithOptionalGuidHeader(bool guid)
    {
        var bytes = Table(0, writer =>
        {
            WriteString(writer, "actual_key");
            WriteString(writer, "actual_key.xml");
        }, 2, guid);
        Assert.That(ParticleEffectsTable.Read(bytes), Is.EqualTo(new[] { "actual_key", "actual_key.xml" }));
        Assert.That(() => ParticleEffectsTable.Read([.. bytes, 0]), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void OfficialWh3FirstRow_DecodesTheExactVersion2Bytes()
    {
        // Original db.pack data__ row; only the header row count is reduced to one.
        var bytes = Convert.FromHexString(
            "fdfefcff2400640035006500330063006400610039002d0062003300360032002d0034003000380066002d00380062003500" +
            "38002d00630032003200370064006500370063006200660032006600fcfdfeff02000000010100000001270077685f6d6169" +
            "6e5f7465727261696e5f656e746974795f6d6f76655f6c617267655f776174657201270077685f6d61696e5f746572726169" +
            "6e5f656e746974795f6d6f76655f6c617267655f677261737301270077685f6d61696e5f7465727261696e5f656e74697479" +
            "5f6d6f76655f6c617267655f677261737301250077685f6d61696e5f7465727261696e5f656e746974795f6d6f76655f6c61" +
            "7267655f6d75641900656e746974795f617274696c6c6572795f666f6f747374657001260077685f6d61696e5f7465727261" +
            "696e5f656e746974795f6d6f76655f6c617267655f726f636b01260077685f6d61696e5f7465727261696e5f656e74697479" +
            "5f6d6f76655f6c617267655f726f636b01260077685f6d61696e5f7465727261696e5f656e746974795f6d6f76655f6c6172" +
            "67655f73616e6401270077685f6d61696e5f7465727261696e5f656e746974795f6d6f76655f6c617267655f677261737301" +
            "260077685f6d61696e5f7465727261696e5f656e746974795f6d6f76655f6c617267655f77616b6501260077685f6d61696e" +
            "5f7465727261696e5f656e746974795f6d6f76655f6c617267655f736e6f7701260077685f6d61696e5f7465727261696e5f" +
            "656e746974795f6d6f76655f6c617267655f726f636b0000803f011f0077685f6d61696e5f756e69745f617274696c6c6572" +
            "795f666f6f7473746570");
        var row = BattleEntityEffectsTable.Read(bytes).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Name, Is.EqualTo("entity_artillery_footstep"));
            Assert.That(row.ParticleKeys, Is.EqualTo(new[]
            {
                "wh_main_terrain_entity_move_large_grass", "wh_main_terrain_entity_move_large_grass",
                "wh_main_terrain_entity_move_large_mud", "wh_main_terrain_entity_move_large_sand",
                "wh_main_terrain_entity_move_large_grass", "wh_main_terrain_entity_move_large_rock",
                "wh_main_terrain_entity_move_large_water", "wh_main_terrain_entity_move_large_wake",
                "wh_main_terrain_entity_move_large_rock", "wh_main_terrain_entity_move_large_rock",
                "wh_main_terrain_entity_move_large_snow"
            }));
        });
    }

    private static byte[] Table(int version, Action<BinaryWriter> body, int count = 1, bool guid = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        if (guid)
        {
            writer.Write(new byte[] { 0xfd, 0xfe, 0xfc, 0xff });
            const string value = "d5e3cda9-b362-408f-8b58-c227de7cbf2f";
            writer.Write((ushort)value.Length);
            writer.Write(Encoding.Unicode.GetBytes(value));
        }
        if (version != 0)
        {
            writer.Write(new byte[] { 0xfc, 0xfd, 0xfe, 0xff });
            writer.Write(version);
        }
        writer.Write(true);
        writer.Write(count);
        body(writer);
        return stream.ToArray();
    }

    private static void WriteRow(BinaryWriter writer, string name, IReadOnlyList<string?> serializedKeys, string? shake)
    {
        for (var i = 0; i < 11; i++)
        {
            if (i == 4)
                WriteString(writer, name);
            WriteOptionalString(writer, serializedKeys[i]);
        }
        writer.Write(0.25f);
        WriteOptionalString(writer, shake);
    }

    private static void WriteOptionalString(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
            WriteString(writer, value);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }
}
