using System.Buffers.Binary;
using Editors.AnimationMeta.SuperView;
using Microsoft.Xna.Framework;
using Shared.ByteParsing;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public class EffectIntermediateVersionTests
{
    [TestCase(6, false)]
    [TestCase(6, true)]
    [TestCase(8, false)]
    [TestCase(8, true)]
    [TestCase(9, false)]
    [TestCase(9, true)]
    [TestCase(10, false)]
    [TestCase(10, true)]
    public void NativeIntermediateLayout_DecodesEveryFieldAndRoundtripsTheCompleteBinaryFile(int version, bool tracking)
    {
        var payload = DiagnosticPayload(version, tracking, out _);
        byte[] file = [.. BitConverter.GetBytes(2), .. BitConverter.GetBytes(1), .. ByteParsers.String.WriteCaString("EFFECT"), .. payload];
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var parsed = parser.ParseFile(file);
        var source = parsed.Attributes.Single();
        var expectedType = version switch { 6 => typeof(Effect_v6), 8 => typeof(Effect_v8), 9 => typeof(Effect_v9), _ => typeof(Effect_v10) };
        Assert.That(source.GetType(), Is.EqualTo(expectedType), "DIAG compatibility payloads must not fall back to unknown META.");
        var effect = (IEffectMeta)source;
        Assert.Multiple(() =>
        {
            Assert.That(effect.VfxName, Is.EqualTo("diagnostic_effect"));
            Assert.That(effect.Tracking, Is.EqualTo(tracking));
            Assert.That(effect.EffectStartTime, Is.EqualTo(.25f));
            Assert.That(effect.EffectEndTime, Is.EqualTo(2.5f));
            Assert.That(effect.Position, Is.EqualTo(new Vector3(1.5f, -2.25f, 3.75f)));
            Assert.That(effect.Orientation, Is.EqualTo(new Vector4(.25f, -.5f, .75f, 1.125f)));
            Assert.That(effect.NodeIndex, Is.EqualTo(7));
            Assert.That(source.GetType().GetProperty("Scale")!.GetValue(source), Is.EqualTo(-2.5f));
            Assert.That(source.GetType().GetProperty("TerrainMutable")!.GetValue(source), Is.True);
            Assert.That(source.GetType().GetProperty("EffectType"), Is.Null);
            Assert.That(source.GetType().GetProperty("ScalesWithParent"), Is.Null);
            Assert.That(parser.Serialize(source, out _), Is.EqualTo(payload));
            Assert.That(parser.GenerateBytes(2, parsed), Is.EqualTo(file));
        });
        if (version == 10)
            Assert.That(((Effect_v10)source).Id, Is.EqualTo(0x12345678));
        else
            Assert.That(source.GetType().GetProperty("Id"), Is.Null);
        if (version == 6)
            Assert.That(source.GetType().GetProperty("DistanceCulled"), Is.Null);
        else
            Assert.That(source.GetType().GetProperty("DistanceCulled")!.GetValue(source), Is.True);
    }

    [TestCase(6)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    public void IntermediateTimeEdit_ChangesOnlyTheExistingNativeTimeSlots(int version)
    {
        var payload = DiagnosticPayload(version, true, out _);
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var source = DecodeDiagnostic(parser, version, payload);
        Assert.That(ParsedMetadataTimeRange.TryCreate(source, out var original), Is.True);
        Assert.That((original.StartTime, original.EndTime), Is.EqualTo((.25f, 2.5f)));
        Assert.That(original.ZeroRangeBehavior, Is.EqualTo(ParsedMetadataZeroRangeBehavior.Instant));
        ParsedMetadataTimeRange.Set(source, -.5f, 1.75f);
        var expected = payload.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(4), -.5f);
        BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(8), 1.75f);
        Assert.Multiple(() =>
        {
            Assert.That(parser.Serialize(source, out _), Is.EqualTo(expected));
            Assert.That(source.Data, Is.SameAs(payload));
            Assert.That(ParsedMetadataTimeRange.TryCreate(source, out var edited) && edited.StartTime == -.5f && edited.EndTime == 1.75f, Is.True);
        });
    }

    [TestCase(6)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    public void IntermediateSpatialBinding_EditsOnlyAuthoredPositionAndRawQuaternion(int version)
    {
        var payload = DiagnosticPayload(version, true, out var positionOffset);
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var source = DecodeDiagnostic(parser, version, payload);
        Assert.That(SpatialMetaDataCatalog.TryCreate(source, out var binding), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(binding.Source, Is.SameAs(source));
            Assert.That(binding.Kind, Is.EqualTo(SpatialMetaDataKind.Effect));
            Assert.That(binding.CanRotate, Is.True);
            Assert.That(binding.AttachToBone, Is.True);
            Assert.That(binding.BoneIndex, Is.EqualTo(7));
            Assert.That(binding.Orientation, Is.EqualTo(new Quaternion(.25f, -.5f, .75f, 1.125f)));
        });
        binding.Position = new(-4, 5, -6);
        binding.Orientation = new Quaternion(-.2f, .3f, -.4f, .6f);
        var expected = payload.ToArray();
        var values = new[] { -4f, 5f, -6f, -.2f, .3f, -.4f, .6f };
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(positionOffset + i * 4), values[i]);
        Assert.Multiple(() =>
        {
            Assert.That(parser.Serialize(source, out _), Is.EqualTo(expected));
            Assert.That(source.Data, Is.SameAs(payload));
            Assert.That(((IEffectMeta)source).NodeIndex, Is.EqualTo(7));
        });
    }

    internal static ParsedMetadataAttribute DecodeDiagnostic(MetaDataFileParser parser, int version, byte[] payload)
    {
        var source = parser.DeSerialize(new ParsedUnknownMetadataAttribute { Name = "EFFECT", Version = version, Data = payload }, out var error);
        Assert.That(error, Is.Null);
        Assert.That(source, Is.InstanceOf<IEffectMeta>());
        return source!;
    }

    internal static byte[] DiagnosticPayload(int version, bool tracking, out int positionOffset)
    {
        // DIAG only: independently encode the native version gates, not a claimed official legacy sample.
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(version));
        bytes.AddRange(BitConverter.GetBytes(.25f));
        bytes.AddRange(BitConverter.GetBytes(2.5f));
        bytes.AddRange(ByteParsers.String.WriteCaString("diagnostic_filter"));
        if (version >= 10) bytes.AddRange(BitConverter.GetBytes(0x12345678));
        bytes.AddRange(ByteParsers.String.WriteCaString("diagnostic_effect"));
        bytes.Add(tracking ? (byte)1 : (byte)0);
        bytes.Add(1); // TerrainMutable is present after v5.
        if (version > 6) bytes.Add(1); // DistanceCulled starts at v7.
        positionOffset = bytes.Count;
        foreach (var value in new[] { 1.5f, -2.25f, 3.75f, .25f, -.5f, .75f, 1.125f }) bytes.AddRange(BitConverter.GetBytes(value));
        bytes.AddRange(BitConverter.GetBytes(7));
        bytes.AddRange(BitConverter.GetBytes(-2.5f));
        return bytes.ToArray();
    }
}
