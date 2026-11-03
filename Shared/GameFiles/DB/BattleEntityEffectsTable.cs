using System.Buffers.Binary;
using System.Text;

namespace Shared.GameFormats.DB;

public sealed record BattleEntityEffectRow(string Name, IReadOnlyList<string?> ParticleKeys)
{
    public string? GetEffect(int canonicalTerrainType) => canonicalTerrainType is >= 0 and <= 10
        ? ParticleKeys[canonicalTerrainType]
        : null;
}

public static class BattleEntityEffectsTable
{
    public static IReadOnlyList<BattleEntityEffectRow> Read(ReadOnlySpan<byte> data)
    {
        var reader = new EffectDbTableReader(data);
        var count = reader.ReadHeader(2, 18);
        var rows = new List<BattleEntityEffectRow>();
        for (var i = 0; i < count; i++)
        {
            // WH3 v2 serializes these fields in schema order, not terrain enum order.
            var deepWater = reader.ReadOptionalString();
            var forest = reader.ReadOptionalString();
            var grass = reader.ReadOptionalString();
            var mud = reader.ReadOptionalString();
            var name = reader.ReadString();
            var rock = reader.ReadOptionalString();
            var road = reader.ReadOptionalString();
            var sand = reader.ReadOptionalString();
            var scrub = reader.ReadOptionalString();
            var shallowWater = reader.ReadOptionalString();
            var snow = reader.ReadOptionalString();
            var woodenFloor = reader.ReadOptionalString();
            reader.Skip(4); // effect_frequency is not part of META resource selection.
            reader.ReadOptionalString(); // footstep_camera_shake
            string?[] keys = [forest, grass, mud, sand, scrub, rock, deepWater, shallowWater, road, woodenFloor, snow];
            rows.Add(new BattleEntityEffectRow(name, Array.AsReadOnly(keys)));
        }

        reader.RequireEnd();
        return rows.AsReadOnly();
    }
}

internal ref struct EffectDbTableReader(ReadOnlySpan<byte> data)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly UnicodeEncoding Utf16 = new(false, false, true);
    private readonly ReadOnlySpan<byte> _data = data;
    private int _offset;

    internal int ReadHeader(int expectedVersion, int minimumRowBytes)
    {
        if (_data.Length < 5)
            throw new InvalidDataException("Truncated effect DB header.");
        if (ConsumeMarker([0xfd, 0xfe, 0xfc, 0xff]))
        {
            var codeUnits = ReadUInt16();
            Decode(Utf16, Take(codeUnits * 2));
        }

        var version = ConsumeMarker([0xfc, 0xfd, 0xfe, 0xff]) ? ReadInt32() : 0;
        if (version != expectedVersion)
            throw new NotSupportedException($"Unsupported effect DB table version {version}; expected {expectedVersion}.");

        ReadBoolean();
        var rowCount = BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        if (rowCount > (uint)((_data.Length - _offset) / minimumRowBytes))
            throw new InvalidDataException("Effect DB row count exceeds the available data.");
        return (int)rowCount;
    }

    internal string? ReadOptionalString() => ReadBoolean() ? ReadString() : null;

    internal string ReadString() => Decode(Utf8, Take(ReadUInt16()));

    internal void Skip(int length) => Take(length);

    internal void RequireEnd()
    {
        if (_offset != _data.Length)
            throw new InvalidDataException($"Unexpected effect DB data at offset {_offset}.");
    }

    private bool ConsumeMarker(ReadOnlySpan<byte> marker)
    {
        if (!_data[_offset..].StartsWith(marker))
            return false;
        _offset += marker.Length;
        return true;
    }

    private bool ReadBoolean() => Take(1)[0] switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("Invalid effect DB Boolean.")
    };

    private ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    private int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    private ReadOnlySpan<byte> Take(int length)
    {
        if (length > _data.Length - _offset)
            throw new InvalidDataException($"Truncated effect DB data at offset {_offset}.");
        var value = _data.Slice(_offset, length);
        _offset += length;
        return value;
    }

    private static string Decode(Encoding encoding, ReadOnlySpan<byte> value)
    {
        try
        {
            return encoding.GetString(value);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Invalid effect DB string encoding.", ex);
        }
    }
}
