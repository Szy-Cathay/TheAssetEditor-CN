using System.IO;

namespace Shared.GameFormats.Esf;

/// <summary>
/// Encodes an <see cref="EsfDocument"/> back to CAAB- or CBAB-format ESF bytes - the write-side
/// mirror of <see cref="EsfReader"/>, ported from the same RPFM source
/// (rpfm_lib/src/files/esf/{caab,utils}.rs). The two signatures share one layout; only the
/// string tables' length-prefix widths differ (see Write's wideStringTables). CBAB writing is
/// validated the same way CAAB was: every one of the corpus's 65 CBAB files round-trips
/// (decode → encode → decode again) to a structurally identical tree, zero failures. (Neither
/// signature generally re-encodes byte-identically - this writer's string-table ordering can
/// differ from CA's - which is why the validation criterion is structural, not byte equality.)
///
/// Doesn't touch LZMA compression (see EsfReader's doc comment) - nothing in the corpus this was
/// built against needed it, so there's nothing to preserve on the way back out either.
/// </summary>
public static class EsfWriter
{
    public static void WriteFile(EsfDocument document, string path)
    {
        using var stream = File.Create(path);
        Write(document, stream);
    }

    public static void Write(EsfDocument document, Stream stream)
    {
        if (document.Signature is not (EsfSignature.Caab or EsfSignature.Cbab))
            throw new NotSupportedException($"Only CAAB and CBAB can be encoded; document signature is {document.Signature}.");

        // CAAB and CBAB share one layout end to end; the only difference is the string tables'
        // length-prefix widths (u16 vs u32 - record names are u16-prefixed either way), exactly
        // mirroring EsfReader.ReadBody's wideStringTables split.
        var wideStringTables = document.Signature == EsfSignature.Cbab;

        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(document.Signature == EsfSignature.Cbab ? (byte)0xCB : (byte)0xCA);
        w.Write((byte)0xAB);
        w.Write((byte)0x00);
        w.Write((byte)0x00);
        w.Write(document.Unknown1);
        w.Write(document.CreationDate);

        // String tables have to be known before the nodes can be encoded (nodes reference them by
        // index), but the tables themselves are written *after* the node data, with their start
        // offset recorded in the header. So: collect strings, encode nodes to a buffer, encode
        // strings to a buffer, then stitch header + node buffer + string buffer together.
        var recordNames = new OrderedStringSet();
        var stringsUtf8 = new OrderedStringSet();
        var stringsUtf16 = new OrderedStringSet();
        CollectStrings(document.Root, recordNames, stringsUtf8, stringsUtf16);

        using var nodesBuffer = new MemoryStream();
        using (var nw = new BinaryWriter(nodesBuffer, System.Text.Encoding.UTF8, leaveOpen: true))
            WriteNode(nw, document.Root, isRoot: true, recordNames, stringsUtf8, stringsUtf16);

        using var stringsBuffer = new MemoryStream();
        using (var sw = new BinaryWriter(stringsBuffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            sw.Write((ushort)recordNames.Count);
            foreach (var name in recordNames.Items)
                sw.WriteSizedStringU8(name);

            sw.Write((uint)stringsUtf16.Count);
            foreach (var s in stringsUtf16.Items)
            {
                if (wideStringTables) sw.WriteSizedStringU16U32(s);
                else sw.WriteSizedStringU16(s);
                sw.Write((uint)stringsUtf16.IndexOf(s));
            }

            sw.Write((uint)stringsUtf8.Count);
            foreach (var s in stringsUtf8.Items)
            {
                if (wideStringTables) sw.WriteSizedStringU8U32(s);
                else sw.WriteSizedStringU8(s);
                sw.Write((uint)stringsUtf8.IndexOf(s));
            }
        }

        // Header layout: signature(4) + unknown1(4) + creation_date(4) + string_table_offset(4) = 16,
        // followed by the node data, so the string table starts at 16 + nodesBuffer.Length.
        var stringTableOffset = 16 + nodesBuffer.Length;
        w.Write((uint)stringTableOffset);
        w.Write(nodesBuffer.ToArray());
        w.Write(stringsBuffer.ToArray());
    }

    private static void CollectStrings(EsfNode node, OrderedStringSet recordNames, OrderedStringSet stringsUtf8, OrderedStringSet stringsUtf16)
    {
        switch (node.Kind)
        {
            case EsfNodeKind.Utf16:
                stringsUtf16.Add((string)node.Value!);
                break;
            case EsfNodeKind.Ascii:
                stringsUtf8.Add((string)node.Value!);
                break;
            case EsfNodeKind.Utf16Array:
                foreach (var s in (string[])node.Value!) stringsUtf16.Add(s);
                break;
            case EsfNodeKind.AsciiArray:
                foreach (var s in (string[])node.Value!) stringsUtf8.Add(s);
                break;
            case EsfNodeKind.Record:
                recordNames.Add(node.Name!);
                foreach (var child in node.Children)
                    CollectStrings(child, recordNames, stringsUtf8, stringsUtf16);
                break;
        }
    }

    private static void WriteNode(BinaryWriter w, EsfNode node, bool isRoot, OrderedStringSet recordNames, OrderedStringSet stringsUtf8, OrderedStringSet stringsUtf16)
    {
        if (node.IsRecord)
        {
            WriteRecord(w, node, isRoot, recordNames, stringsUtf8, stringsUtf16);
            return;
        }

        switch (node.Kind)
        {
            case EsfNodeKind.Invalid:
                throw new InvalidOperationException("Cannot encode an Invalid node.");

            case EsfNodeKind.Bool:
                if (node.Optimized) w.Write((bool)node.Value! ? EsfNodeTypeCodes.BoolTrue : EsfNodeTypeCodes.BoolFalse);
                else { w.Write(EsfNodeTypeCodes.Bool); w.Write((bool)node.Value!); }
                break;
            case EsfNodeKind.I8: w.Write(EsfNodeTypeCodes.I8); w.Write((sbyte)node.Value!); break;
            case EsfNodeKind.I16: w.Write(EsfNodeTypeCodes.I16); w.Write((short)node.Value!); break;
            case EsfNodeKind.I32: WriteI32(w, (int)node.Value!, node.Optimized); break;
            case EsfNodeKind.I64: w.Write(EsfNodeTypeCodes.I64); w.Write((long)node.Value!); break;
            case EsfNodeKind.U8: w.Write(EsfNodeTypeCodes.U8); w.Write((byte)node.Value!); break;
            case EsfNodeKind.U16: w.Write(EsfNodeTypeCodes.U16); w.Write((ushort)node.Value!); break;
            case EsfNodeKind.U32: WriteU32(w, (uint)node.Value!, node.Optimized); break;
            case EsfNodeKind.U64: w.Write(EsfNodeTypeCodes.U64); w.Write((ulong)node.Value!); break;
            case EsfNodeKind.F32: WriteF32(w, (float)node.Value!, node.Optimized); break;
            case EsfNodeKind.F64: w.Write(EsfNodeTypeCodes.F64); w.Write((double)node.Value!); break;

            case EsfNodeKind.Coord2d:
            {
                var c = (Coord2d)node.Value!;
                w.Write(EsfNodeTypeCodes.Coord2D); w.Write(c.X); w.Write(c.Y);
                break;
            }
            case EsfNodeKind.Coord3d:
            {
                var c = (Coord3d)node.Value!;
                w.Write(EsfNodeTypeCodes.Coord3D); w.Write(c.X); w.Write(c.Y); w.Write(c.Z);
                break;
            }

            case EsfNodeKind.Utf16: w.Write(EsfNodeTypeCodes.Utf16); w.Write((uint)stringsUtf16.IndexOf((string)node.Value!)); break;
            case EsfNodeKind.Ascii: w.Write(EsfNodeTypeCodes.Ascii); w.Write((uint)stringsUtf8.IndexOf((string)node.Value!)); break;
            case EsfNodeKind.Angle: w.Write(EsfNodeTypeCodes.Angle); w.Write((short)node.Value!); break;

            case EsfNodeKind.Unknown21: w.Write(EsfNodeTypeCodes.Unknown21); w.Write((uint)node.Value!); break;
            case EsfNodeKind.Unknown23: w.Write(EsfNodeTypeCodes.Unknown23); w.Write((byte)node.Value!); break;
            case EsfNodeKind.Unknown24: w.Write(EsfNodeTypeCodes.Unknown24); w.Write((ushort)node.Value!); break;
            case EsfNodeKind.Unknown25: w.Write(EsfNodeTypeCodes.Unknown25); w.Write((uint)node.Value!); break;
            case EsfNodeKind.Unknown26: w.Write(EsfNodeTypeCodes.Unknown26); w.Write((byte[])node.Value!); break;

            case EsfNodeKind.BoolArray: WriteArray(w, EsfNodeTypeCodes.BoolArray, (bool[])node.Value!, (bw, v) => bw.Write(v)); break;
            case EsfNodeKind.I8Array: WriteByteSizedArray(w, EsfNodeTypeCodes.I8Array, ToByteArray((sbyte[])node.Value!)); break;
            case EsfNodeKind.I16Array: WriteArray(w, EsfNodeTypeCodes.I16Array, (short[])node.Value!, (bw, v) => bw.Write(v)); break;
            case EsfNodeKind.I64Array: WriteArray(w, EsfNodeTypeCodes.I64Array, (long[])node.Value!, (bw, v) => bw.Write(v)); break;
            case EsfNodeKind.U8Array: WriteByteSizedArray(w, EsfNodeTypeCodes.U8Array, (byte[])node.Value!); break;
            case EsfNodeKind.U16Array: WriteArray(w, EsfNodeTypeCodes.U16Array, (ushort[])node.Value!, (bw, v) => bw.Write(v)); break;
            case EsfNodeKind.U64Array: WriteArray(w, EsfNodeTypeCodes.U64Array, (ulong[])node.Value!, (bw, v) => bw.Write(v)); break;
            case EsfNodeKind.F32Array: WriteArray(w, EsfNodeTypeCodes.F32Array, (float[])node.Value!, (bw, v) => bw.Write(v)); break;
            case EsfNodeKind.F64Array: WriteArray(w, EsfNodeTypeCodes.F64Array, (double[])node.Value!, (bw, v) => bw.Write(v)); break;
            case EsfNodeKind.Coord2dArray: WriteArray(w, EsfNodeTypeCodes.Coord2DArray, (Coord2d[])node.Value!, (bw, v) => { bw.Write(v.X); bw.Write(v.Y); }); break;
            case EsfNodeKind.Coord3dArray: WriteArray(w, EsfNodeTypeCodes.Coord3DArray, (Coord3d[])node.Value!, (bw, v) => { bw.Write(v.X); bw.Write(v.Y); bw.Write(v.Z); }); break;
            case EsfNodeKind.Utf16Array: WriteArray(w, EsfNodeTypeCodes.Utf16Array, (string[])node.Value!, (bw, v) => bw.Write((uint)stringsUtf16.IndexOf(v))); break;
            case EsfNodeKind.AsciiArray: WriteArray(w, EsfNodeTypeCodes.AsciiArray, (string[])node.Value!, (bw, v) => bw.Write((uint)stringsUtf8.IndexOf(v))); break;
            case EsfNodeKind.AngleArray: WriteArray(w, EsfNodeTypeCodes.AngleArray, (short[])node.Value!, (bw, v) => bw.Write(v)); break;

            case EsfNodeKind.I32Array: WriteI32Array(w, (int[])node.Value!, node.Optimized); break;
            case EsfNodeKind.U32Array: WriteU32Array(w, (uint[])node.Value!, node.Optimized); break;

            default:
                throw new NotSupportedException($"Don't know how to encode node kind {node.Kind}.");
        }
    }

    private static void WriteI32(BinaryWriter w, int value, bool optimized)
    {
        if (!optimized) { w.Write(EsfNodeTypeCodes.I32); w.Write(value); return; }

        if (value == 0) w.Write(EsfNodeTypeCodes.I32Zero);
        else if (value is <= sbyte.MaxValue and >= sbyte.MinValue) { w.Write(EsfNodeTypeCodes.I32Byte); w.Write((sbyte)value); }
        else if (value is <= short.MaxValue and >= short.MinValue) { w.Write(EsfNodeTypeCodes.I32_16Bit); w.Write((short)value); }
        else if (value is <= 8_388_607 and >= -8_388_608) { w.Write(EsfNodeTypeCodes.I32_24Bit); w.WriteInt24(value); }
        else { w.Write(EsfNodeTypeCodes.I32); w.Write(value); }
    }

    private static void WriteU32(BinaryWriter w, uint value, bool optimized)
    {
        if (!optimized) { w.Write(EsfNodeTypeCodes.U32); w.Write(value); return; }

        if (value == 0) w.Write(EsfNodeTypeCodes.U32Zero);
        else if (value == 1) w.Write(EsfNodeTypeCodes.U32One);
        else if (value <= 0xFF) { w.Write(EsfNodeTypeCodes.U32Byte); w.Write((byte)value); }
        else if (value <= 0xFFFF) { w.Write(EsfNodeTypeCodes.U32_16Bit); w.Write((ushort)value); }
        else if (value <= 0xFF_FFFF) { w.Write(EsfNodeTypeCodes.U32_24Bit); w.WriteUInt24(value); }
        else { w.Write(EsfNodeTypeCodes.U32); w.Write(value); }
    }

    private static void WriteF32(BinaryWriter w, float value, bool optimized)
    {
        if (optimized && value == 0f) { w.Write(EsfNodeTypeCodes.F32Zero); return; }
        w.Write(EsfNodeTypeCodes.F32);
        w.Write(value);
    }

    private static void WriteI32Array(BinaryWriter w, int[] values, bool optimized)
    {
        using var buf = new MemoryStream();
        using (var bw = new BinaryWriter(buf, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            if (!optimized || values.Length == 0)
            {
                w.Write(EsfNodeTypeCodes.I32Array);
                foreach (var v in values) bw.Write(v);
            }
            else
            {
                var maxAbs = values.Max(v => Math.Abs((long)v));
                if (maxAbs <= sbyte.MaxValue) { w.Write(EsfNodeTypeCodes.I32ByteArray); foreach (var v in values) bw.Write((sbyte)v); }
                else if (maxAbs <= short.MaxValue) { w.Write(EsfNodeTypeCodes.I32_16BitArray); foreach (var v in values) bw.Write((short)v); }
                else if (maxAbs <= 8_388_607) { w.Write(EsfNodeTypeCodes.I32_24BitArray); foreach (var v in values) bw.WriteInt24(v); }
                else { w.Write(EsfNodeTypeCodes.I32Array); foreach (var v in values) bw.Write(v); }
            }

            w.WriteCauleb128((uint)buf.Length);
            w.Write(buf.ToArray());
        }
    }

    private static void WriteU32Array(BinaryWriter w, uint[] values, bool optimized)
    {
        using var buf = new MemoryStream();
        using (var bw = new BinaryWriter(buf, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            if (!optimized || values.Length == 0)
            {
                w.Write(EsfNodeTypeCodes.U32Array);
                foreach (var v in values) bw.Write(v);
            }
            else
            {
                var max = values.Max();
                if (max < 0xFF) { w.Write(EsfNodeTypeCodes.U32ByteArray); foreach (var v in values) bw.Write((byte)v); }
                else if (max < 0xFFFF) { w.Write(EsfNodeTypeCodes.U32_16BitArray); foreach (var v in values) bw.Write((ushort)v); }
                else if (max < 0xFF_FFFF) { w.Write(EsfNodeTypeCodes.U32_24BitArray); foreach (var v in values) bw.WriteUInt24(v); }
                else { w.Write(EsfNodeTypeCodes.U32Array); foreach (var v in values) bw.Write(v); }
            }

            w.WriteCauleb128((uint)buf.Length);
            w.Write(buf.ToArray());
        }
    }

    private static void WriteArray<T>(BinaryWriter w, byte typeCode, T[] values, Action<BinaryWriter, T> writeElement)
    {
        using var buf = new MemoryStream();
        using (var bw = new BinaryWriter(buf, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            foreach (var v in values)
                writeElement(bw, v);

            w.Write(typeCode);
            w.WriteCauleb128((uint)buf.Length);
            w.Write(buf.ToArray());
        }
    }

    private static void WriteByteSizedArray(BinaryWriter w, byte typeCode, byte[] bytes)
    {
        w.Write(typeCode);
        w.WriteCauleb128((uint)bytes.Length);
        w.Write(bytes);
    }

    private static byte[] ToByteArray(sbyte[] values)
    {
        var result = new byte[values.Length];
        for (var i = 0; i < values.Length; i++)
            result[i] = unchecked((byte)values[i]);
        return result;
    }

    private static void WriteRecord(BinaryWriter w, EsfNode node, bool isRoot, OrderedStringSet recordNames, OrderedStringSet stringsUtf8, OrderedStringSet stringsUtf16)
    {
        var flags = node.RecordFlags;
        var nameIndex = recordNames.IndexOf(node.Name!);

        if (flags.HasFlag(EsfRecordFlags.HasNonOptimizedInfo) || isRoot)
        {
            w.Write((byte)flags);
            w.Write((ushort)nameIndex);
            w.Write(node.Version);
        }
        else
        {
            // Inverse of EsfReader's packed-header read: byte 1 carries the record/nested-block
            // flags plus the version and the name index's 9th bit; byte 2 is the low 8 bits of
            // the name index.
            var b1 = (byte)(((byte)flags & 0b1100_0000) | (node.Version << 1) | ((nameIndex >> 8) & 1));
            var b2 = (byte)(nameIndex & 0xFF);
            w.Write(b1);
            w.Write(b2);
        }

        using var childrenBuffer = new MemoryStream();
        using (var cw = new BinaryWriter(childrenBuffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            if (flags.HasFlag(EsfRecordFlags.HasNestedBlocks))
            {
                foreach (var group in node.Groups!)
                {
                    using var groupBuffer = new MemoryStream();
                    using (var gw = new BinaryWriter(groupBuffer, System.Text.Encoding.UTF8, leaveOpen: true))
                    {
                        foreach (var child in group)
                            WriteNode(gw, child, isRoot: false, recordNames, stringsUtf8, stringsUtf16);
                    }

                    cw.WriteCauleb128((uint)groupBuffer.Length);
                    cw.Write(groupBuffer.ToArray());
                }
            }
            else
            {
                foreach (var child in node.Groups!.FirstOrDefault() ?? [])
                    WriteNode(cw, child, isRoot: false, recordNames, stringsUtf8, stringsUtf16);
            }
        }

        var childrenBytes = childrenBuffer.ToArray();
        w.WriteCauleb128((uint)childrenBytes.Length);
        if (flags.HasFlag(EsfRecordFlags.HasNestedBlocks))
            w.WriteCauleb128((uint)node.Groups!.Count);
        w.Write(childrenBytes);
    }

    /// <summary>Dedups strings by first occurrence while preserving that order, with O(1) index lookup.</summary>
    private sealed class OrderedStringSet
    {
        private readonly List<string> _items = [];
        private readonly Dictionary<string, int> _indexOf = new();

        public IReadOnlyList<string> Items => _items;
        public int Count => _items.Count;

        public void Add(string value)
        {
            if (_indexOf.ContainsKey(value)) return;
            _indexOf[value] = _items.Count;
            _items.Add(value);
        }

        public int IndexOf(string value) => _indexOf[value];
    }
}
