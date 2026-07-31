using System.IO;
using System.Text;

namespace Shared.GameFormats.Esf;

/// <summary>Write-side counterpart to <see cref="EsfBinaryReader"/> - see that file for the format notes.</summary>
public static class EsfBinaryWriter
{
    public static void WriteUInt24(this BinaryWriter w, uint value)
    {
        w.Write((byte)(value & 0xFF));
        w.Write((byte)((value >> 8) & 0xFF));
        w.Write((byte)((value >> 16) & 0xFF));
    }

    public static void WriteInt24(this BinaryWriter w, int value) => w.WriteUInt24(unchecked((uint)value) & 0xFF_FFFF);

    /// <summary>
    /// Inverse of ReadCauleb128: emits the minimal (no leading all-zero groups) big-endian-style
    /// 7-bit-group encoding, continuation bit set on every byte but the last.
    /// </summary>
    public static void WriteCauleb128(this BinaryWriter w, uint value)
    {
        var groups = new List<byte>();
        do
        {
            groups.Add((byte)(value & 0x7F));
            value >>= 7;
        } while (value != 0);
        groups.Reverse();

        for (var i = 0; i < groups.Count; i++)
            w.Write(i < groups.Count - 1 ? (byte)(groups[i] | 0x80) : groups[i]);
    }

    public static void WriteSizedStringU8(this BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }

    public static void WriteSizedStringU8U32(this BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write((uint)bytes.Length);
        w.Write(bytes);
    }

    public static void WriteSizedStringU16(this BinaryWriter w, string s)
    {
        w.Write((ushort)s.Length);
        w.Write(Encoding.Unicode.GetBytes(s));
    }

    public static void WriteSizedStringU16U32(this BinaryWriter w, string s)
    {
        w.Write((uint)s.Length);
        w.Write(Encoding.Unicode.GetBytes(s));
    }
}
