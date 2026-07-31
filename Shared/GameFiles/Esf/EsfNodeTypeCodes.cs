namespace Shared.GameFormats.Esf;

/// <summary>
/// The type-marker byte that begins every non-record ESF node, plus the record-node
/// flag bits (which live in the high bits of the same byte position). Ported from
/// RPFM's rpfm_lib/src/files/esf/mod.rs, where they're attributed back to ESFEdit.
///
/// Ranges:
///  0x00        Invalid/reserved
///  0x01-0x10   Primitive types
///  0x12-0x1d   Optimized primitives (compact encodings for common values)
///  0x21-0x26   Unknown/undocumented types
///  0x41-0x50   Arrays of primitive types
///  0x52-0x5d   Arrays with optimized element encodings
///  0x80+       Record nodes (high bit set)
/// </summary>
internal static class EsfNodeTypeCodes
{
    public const byte Invalid = 0x00;

    // Primitives
    public const byte Bool = 0x01;
    public const byte I8 = 0x02;
    public const byte I16 = 0x03;
    public const byte I32 = 0x04;
    public const byte I64 = 0x05;
    public const byte U8 = 0x06;
    public const byte U16 = 0x07;
    public const byte U32 = 0x08;
    public const byte U64 = 0x09;
    public const byte F32 = 0x0a;
    public const byte F64 = 0x0b;
    public const byte Coord2D = 0x0c;
    public const byte Coord3D = 0x0d;
    public const byte Utf16 = 0x0e;
    public const byte Ascii = 0x0f;
    public const byte Angle = 0x10;

    // Optimized primitives - compact encodings for common values.
    public const byte BoolTrue = 0x12;
    public const byte BoolFalse = 0x13;
    public const byte U32Zero = 0x14;
    public const byte U32One = 0x15;
    public const byte U32Byte = 0x16;
    public const byte U32_16Bit = 0x17;
    public const byte U32_24Bit = 0x18;
    public const byte I32Zero = 0x19;
    public const byte I32Byte = 0x1a;
    public const byte I32_16Bit = 0x1b;
    public const byte I32_24Bit = 0x1c;
    public const byte F32Zero = 0x1d;

    // Undocumented types.
    public const byte Unknown21 = 0x21; // stores a u32
    public const byte Unknown23 = 0x23; // stores a u8
    public const byte Unknown24 = 0x24; // stores a u16
    public const byte Unknown25 = 0x25; // stores a u32
    public const byte Unknown26 = 0x26; // variable-length, special encoding (Three Kingdoms "Eight Princes" DLC)

    // Arrays - CAULEB128-encoded byte-length prefix, then elements packed back to back.
    public const byte BoolArray = 0x41;
    public const byte I8Array = 0x42;
    public const byte I16Array = 0x43;
    public const byte I32Array = 0x44;
    public const byte I64Array = 0x45;
    public const byte U8Array = 0x46;
    public const byte U16Array = 0x47;
    public const byte U32Array = 0x48;
    public const byte U64Array = 0x49;
    public const byte F32Array = 0x4a;
    public const byte F64Array = 0x4b;
    public const byte Coord2DArray = 0x4c;
    public const byte Coord3DArray = 0x4d;
    public const byte Utf16Array = 0x4e;
    public const byte AsciiArray = 0x4f;
    public const byte AngleArray = 0x50;

    // Optimized arrays - elements use a compact encoding. 0x52/0x53/0x54/0x55/0x59/0x5d
    // exist in the format but are never actually emitted (e.g. "array of all zeros"
    // makes no practical sense), so they're omitted here.
    public const byte U32ByteArray = 0x56;
    public const byte U32_16BitArray = 0x57;
    public const byte U32_24BitArray = 0x58;
    public const byte I32ByteArray = 0x5a;
    public const byte I32_16BitArray = 0x5b;
    public const byte I32_24BitArray = 0x5c;
}
