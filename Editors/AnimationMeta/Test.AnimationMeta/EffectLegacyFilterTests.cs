using Shared.ByteParsing;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public class EffectLegacyFilterTests
{
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    [TestCase(11)]
    [TestCase(12)]
    public void LegacyFilter_IsRetainedVerbatimAndEditingItDoesNotAlterAnyOtherField(int version)
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var original = Payload(version, "  Mixed_Case/Filter:001  ");
        var source = parser.DeSerialize(new ParsedUnknownMetadataAttribute { Name = "EFFECT", Version = version, Data = original }, out var error)!;
        var filter = source.GetType().GetProperty("Filter")!;
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(source, Is.InstanceOf<IEffectMeta>());
            Assert.That(filter.GetValue(source), Is.EqualTo("  Mixed_Case/Filter:001  "));
            Assert.That(parser.Serialize(source, out _), Is.EqualTo(original));
        });
        filter.SetValue(source, "");
        Assert.Multiple(() =>
        {
            Assert.That(parser.Serialize(source, out _), Is.EqualTo(Payload(version, "")));
            Assert.That(source.Data, Is.SameAs(original));
        });
        filter.SetValue(source, "Different/CASE");
        Assert.That(parser.Serialize(source, out _), Is.EqualTo(Payload(version, "Different/CASE")));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    [TestCase(11)]
    [TestCase(12)]
    public void EffectTimeInterface_ReadsAndWritesTheSerializedNativeSlots(int version)
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var source = parser.DeSerialize(new ParsedUnknownMetadataAttribute { Name = "EFFECT", Version = version, Data = Payload(version, "retained") }, out var error)!;
        Assert.That(error, Is.Null);
        var effect = (IEffectMeta)source;
        Assert.That((effect.EffectStartTime, effect.EffectEndTime), Is.EqualTo((.25f, 2.5f)));
        effect.EffectStartTime = .75f;
        effect.EffectEndTime = 3.5f;
        var expected = Payload(version, "retained");
        BitConverter.GetBytes(.75f).CopyTo(expected, 4);
        BitConverter.GetBytes(3.5f).CopyTo(expected, 8);
        Assert.Multiple(() =>
        {
            Assert.That(parser.Serialize(source, out _), Is.EqualTo(expected));
            Assert.That(ParsedMetadataTimeRange.TryCreate(source, out var range), Is.True);
            Assert.That((range.StartTime, range.EndTime), Is.EqualTo((.75f, 3.5f)));
        });
    }

    private static byte[] Payload(int version, string filter)
    {
        // DIAG: independently encode every version gate from the native Effect reader.
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(version));
        bytes.AddRange(BitConverter.GetBytes(.25f)); bytes.AddRange(BitConverter.GetBytes(2.5f));
        if (version >= 2) bytes.AddRange(ByteParsers.String.WriteCaString(filter));
        if (version >= 10) bytes.AddRange(BitConverter.GetBytes(1100));
        bytes.AddRange(ByteParsers.String.WriteCaString("diagnostic_effect"));
        if (version > 10) bytes.AddRange(BitConverter.GetBytes(2));
        if (version <= 5) bytes.AddRange(ByteParsers.String.WriteCaString("true"));
        else bytes.Add(1);
        if (version > 5) bytes.Add(1);
        if (version > 6) bytes.Add(1);
        if (version > 11) bytes.Add(1);
        foreach (var value in new[] { 1f, 2f, 3f, .2f, -.3f, .4f, .7f }) bytes.AddRange(BitConverter.GetBytes(value));
        bytes.AddRange(BitConverter.GetBytes(-1));
        if (version == 3 || version > 4) bytes.AddRange(BitConverter.GetBytes(-2f));
        return bytes.ToArray();
    }
}