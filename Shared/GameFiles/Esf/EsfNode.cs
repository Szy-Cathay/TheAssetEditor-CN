namespace Shared.GameFormats.Esf;

/// <summary>
/// A single node in an ESF tree. ESF is a tagged-union format - one class with a Kind
/// discriminator mirrors that more directly than a C# type hierarchy and keeps the
/// tree-building code in EsfReader simple.
///
/// Leaf nodes (primitives, arrays, coordinates, strings) carry their data in <see cref="Value"/>.
/// Record nodes instead use <see cref="Name"/>/<see cref="Version"/>/<see cref="RecordFlags"/>/<see cref="Groups"/>
/// and leave <see cref="Value"/> null.
/// </summary>
public sealed class EsfNode
{
    public required EsfNodeKind Kind { get; init; }

    /// <summary>
    /// The decoded value for leaf nodes. Actual CLR type depends on Kind:
    /// bool, sbyte, short, int, long, byte, ushort, uint, ulong, float, double,
    /// string, Coord2d, Coord3d, or one of the corresponding array/list types.
    /// Null for Record nodes (and for Invalid).
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// True if this leaf was encoded using one of ESF's compact "optimized" markers
    /// (e.g. U32_ZERO, I32_BYTE). Purely an encoding-fidelity detail - the decoded
    /// Value is identical either way.
    /// </summary>
    public bool Optimized { get; init; }

    // --- Record-only fields ---

    /// <summary>Record name, resolved from the record-names string table. Null for non-record nodes.</summary>
    public string? Name { get; init; }

    public byte Version { get; init; }

    public EsfRecordFlags RecordFlags { get; init; }

    /// <summary>
    /// Record children, organized into groups. A record without HasNestedBlocks has exactly
    /// one group holding every child; one with HasNestedBlocks has one group per repeated block
    /// (e.g. one group per instance of a repeated sub-entry). Null for non-record nodes.
    /// </summary>
    public List<List<EsfNode>>? Groups { get; init; }

    public bool IsRecord => Kind == EsfNodeKind.Record;

    /// <summary>All children across every group, flattened, in encounter order. Empty for non-record nodes.</summary>
    public IEnumerable<EsfNode> Children => Groups?.SelectMany(g => g) ?? Enumerable.Empty<EsfNode>();

    public static EsfNode Leaf(EsfNodeKind kind, object? value, bool optimized = false) =>
        new() { Kind = kind, Value = value, Optimized = optimized };

    public static EsfNode NewRecord(string name, byte version, EsfRecordFlags flags, List<List<EsfNode>> groups) =>
        new() { Kind = EsfNodeKind.Record, Name = name, Version = version, RecordFlags = flags, Groups = groups };
}
