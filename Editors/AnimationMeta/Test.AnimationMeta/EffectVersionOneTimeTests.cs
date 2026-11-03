using System.Buffers.Binary;
using Shared.ByteParsing;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public class EffectVersionOneTimeTests
{
    [TestCase(.25f, 1.75f)]
    [TestCase(-.5f, 2.25f)]
    [TestCase(0f, 0f)]
    public void NativeV1FloatTimeSlots_AreExposedWithoutChangingTheirSerializedBytes(float start, float end)
    {
        var payload = Payload(start, end);
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var source = parser.DeSerialize(new ParsedUnknownMetadataAttribute { Name = "EFFECT", Version = 1, Data = payload }, out var error);
        Assert.That(error, Is.Null);
        var effect = (Effect_v1)source!;
        Assert.Multiple(() =>
        {
            Assert.That(effect.EffectStartTime, Is.EqualTo(start));
            Assert.That(effect.EffectEndTime, Is.EqualTo(end));
            Assert.That(effect.UnknownIntBase_v0, Is.EqualTo(BitConverter.SingleToInt32Bits(start)));
            Assert.That(effect.UnknownIntBase_v1, Is.EqualTo(BitConverter.SingleToInt32Bits(end)));
            Assert.That(ParsedMetadataTimeRange.TryCreate(effect, out var range), Is.True);
            Assert.That(range.StartTime, Is.EqualTo(start));
            Assert.That(range.EndTime, Is.EqualTo(end));
            Assert.That(range.ZeroRangeBehavior, Is.EqualTo(ParsedMetadataZeroRangeBehavior.Instant));
            Assert.That(parser.Serialize(effect, out _), Is.EqualTo(payload));
        });
    }

    [Test]
    public void V1TimeRangeEdit_WritesOnlyItsTwoExistingFloatSlots()
    {
        var payload = Payload(.25f, 1.75f);
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var effect = (Effect_v1)parser.DeSerialize(new ParsedUnknownMetadataAttribute { Name = "EFFECT", Version = 1, Data = payload }, out _)!;
        ParsedMetadataTimeRange.Set(effect, .5f, 2.5f);
        var expected = payload.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(4), .5f);
        BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(8), 2.5f);
        Assert.That(parser.Serialize(effect, out _), Is.EqualTo(expected));
        Assert.That(effect.Data, Is.SameAs(payload), "Editing the decoded time fields must not rewrite the original recovery payload.");
    }

    [Test]
    public void OtherLegacyTagIntegerFields_AreNotReinterpretedAsEffectTimes()
    {
        var other = new OtherLegacyTag { UnknownIntBase_v0 = 123, UnknownIntBase_v1 = 456 };
        Assert.That(ParsedMetadataTimeRange.TryCreate(other, out _), Is.False);
        Assert.Throws<ArgumentException>(() => ParsedMetadataTimeRange.Set(other, .5f, 2.5f));
        Assert.That((other.UnknownIntBase_v0, other.UnknownIntBase_v1), Is.EqualTo((123, 456)));
    }

    private static byte[] Payload(float start, float end)
    {
        // DIAG: version and native Start/End float32 slots, followed by the complete v1 Effect record.
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(1)); bytes.AddRange(BitConverter.GetBytes(start)); bytes.AddRange(BitConverter.GetBytes(end));
        bytes.AddRange(ByteParsers.String.WriteCaString("diagnostic_effect"));
        bytes.AddRange(ByteParsers.String.WriteCaString("true"));
        foreach (var value in new[] { 1f, 2f, 3f, 0f, 0f, 0f, 1f }) bytes.AddRange(BitConverter.GetBytes(value));
        bytes.AddRange(BitConverter.GetBytes(-1));
        return bytes.ToArray();
    }

    private sealed class OtherLegacyTag : DecodedMetaEntryBase_v1 { }
}
