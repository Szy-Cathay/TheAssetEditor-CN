namespace Shared.GameFormats.Esf;

/// <summary>
/// Every node shape the ESF format can contain. Mirrors RPFM's NodeType enum
/// (rpfm_lib/src/files/esf/mod.rs) so the C# model stays traceable back to the
/// binary layout documented in EsfNodeTypeCodes.
/// </summary>
public enum EsfNodeKind
{
    Invalid,

    // Primitives
    Bool,
    I8,
    I16,
    I32,
    I64,
    U8,
    U16,
    U32,
    U64,
    F32,
    F64,
    Coord2d,
    Coord3d,
    Utf16,
    Ascii,
    Angle,

    // Undocumented types, preserved for round-trip fidelity rather than understood.
    Unknown21,
    Unknown23,
    Unknown24,
    Unknown25,
    Unknown26,

    // Arrays
    BoolArray,
    I8Array,
    I16Array,
    I32Array,
    I64Array,
    U8Array,
    U16Array,
    U32Array,
    U64Array,
    F32Array,
    F64Array,
    Coord2dArray,
    Coord3dArray,
    Utf16Array,
    AsciiArray,
    AngleArray,

    /// <summary>A named container node holding child nodes - the tree's structural backbone.</summary>
    Record,
}
