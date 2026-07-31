namespace Shared.GameFormats.Esf;

/// <summary>
/// Flag bits packed into a record node's first byte. See EsfReader for how these
/// change the header encoding.
/// </summary>
[Flags]
public enum EsfRecordFlags : byte
{
    None = 0,

    /// <summary>Always set for record nodes; distinguishes them from primitive/array nodes.</summary>
    IsRecordNode = 0b1000_0000,

    /// <summary>Children are organized into multiple groups (each with its own size prefix) instead of one flat list.</summary>
    HasNestedBlocks = 0b0100_0000,

    /// <summary>Header uses the full 3-byte form (u16 name index + u8 version) instead of the packed 2-byte form.</summary>
    HasNonOptimizedInfo = 0b0010_0000,
}
