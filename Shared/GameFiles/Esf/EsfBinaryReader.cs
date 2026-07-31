using System.IO;
using System.Text;

namespace Shared.GameFormats.Esf;

/// <summary>
/// Read helpers for primitives ESF uses that BinaryReader doesn't provide natively:
/// 24-bit integers, CA's own take on ULEB128, and length-prefixed strings in the
/// various width combinations the format mixes and matches.
///
/// All ported from RPFM's rpfm_lib/src/binary/reader.rs (MIT licensed).
/// </summary>
public static class EsfBinaryReader
{
    public static uint ReadUInt24(this BinaryReader r)
    {
        var b0 = r.ReadByte();
        var b1 = r.ReadByte();
        var b2 = r.ReadByte();
        return (uint)(b0 | (b1 << 8) | (b2 << 16));
    }

    public static int ReadInt24(this BinaryReader r)
    {
        var value = r.ReadUInt24();
        // Sign-extend bit 23 into the top byte.
        if ((value & 0x0080_0000) != 0)
            value |= 0xFF00_0000;
        return unchecked((int)value);
    }

    public static bool ReadBoolStrict(this BinaryReader r)
    {
        var value = r.ReadByte();
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException($"Expected a boolean byte (0 or 1), got 0x{value:X2}."),
        };
    }

    /// <summary>
    /// CA's variant of ULEB128: 7-bit groups, most-significant group first, with the
    /// high bit of each byte (except the last) set to signal "more bytes follow".
    /// This is the reverse group order of standard LEB128.
    /// </summary>
    public static uint ReadCauleb128(this BinaryReader r)
    {
        uint value = 0;
        var b = r.ReadByte();
        while ((b & 0x80) != 0)
        {
            value = (value << 7) | (uint)(b & 0x7F);
            b = r.ReadByte();
        }
        value = (value << 7) | (uint)(b & 0x7F);
        return value;
    }

    /// <summary>UTF-8 string with a u16 byte-length prefix. Used for CAAB record names and CAAB ASCII strings.</summary>
    public static string ReadSizedStringU8(this BinaryReader r)
    {
        var size = r.ReadUInt16();
        return Encoding.UTF8.GetString(r.ReadBytes(size));
    }

    /// <summary>UTF-8 string with a u32 byte-length prefix. Used for CBAB ASCII strings.</summary>
    public static string ReadSizedStringU8U32(this BinaryReader r)
    {
        var size = r.ReadUInt32();
        return Encoding.UTF8.GetString(r.ReadBytes(checked((int)size)));
    }

    /// <summary>UTF-16LE string with a u16 character-count prefix. Used for CAAB UTF-16 strings.</summary>
    public static string ReadSizedStringU16(this BinaryReader r)
    {
        var charCount = r.ReadUInt16();
        return Encoding.Unicode.GetString(r.ReadBytes(charCount * 2));
    }

    /// <summary>UTF-16LE string with a u32 character-count prefix. Used for CBAB UTF-16 strings.</summary>
    public static string ReadSizedStringU16U32(this BinaryReader r)
    {
        var charCount = r.ReadUInt32();
        return Encoding.Unicode.GetString(r.ReadBytes(checked((int)charCount * 2)));
    }
}
