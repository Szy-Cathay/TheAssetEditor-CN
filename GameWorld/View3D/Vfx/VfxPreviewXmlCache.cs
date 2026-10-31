using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

// Only converter-owned, read-only XML trees are shared. Callers still resolve and hash actual bytes.
internal sealed class VfxPreviewXmlCache
{
    internal static VfxPreviewXmlCache Shared { get; } = new();
    private readonly object _gate = new();
    private readonly long _maxBytes;
    private readonly int _maxEntries;
    private readonly Func<byte[], VfxDocument> _parse;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = new();
    private long _bytes;

    internal VfxPreviewXmlCache(long maxBytes = 8 * 1024 * 1024, int maxEntries = 16, Func<byte[], VfxDocument>? parser = null)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        _maxBytes = maxBytes;
        _maxEntries = maxEntries;
        _parse = parser ?? VfxDocument.Read;
    }

    internal int EntryCount { get { lock (_gate) return _entries.Count; } }
    internal long RetainedSourceBytes { get { lock (_gate) return _bytes; } }

    internal VfxDocument Read(string path, string sha, byte[] bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = path.Replace('\\', '/');
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_entries.TryGetValue(path, out var existing) && existing.Hash == sha)
            {
                _recent.Remove(existing.Node);
                _recent.AddLast(existing.Node);
                return existing.Document;
            }
            var document = _parse(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (existing != null) Remove(path, existing);
            if (bytes.LongLength > _maxBytes) return document;
            while (_entries.Count >= _maxEntries || _bytes + bytes.LongLength > _maxBytes)
            {
                var oldest = _recent.First!.Value;
                Remove(oldest, _entries[oldest]);
            }
            var node = _recent.AddLast(path);
            _entries.Add(path, new(sha, document, bytes.LongLength, node));
            _bytes += bytes.LongLength;
            return document;
        }
    }

    private void Remove(string path, Entry entry)
    {
        _entries.Remove(path);
        _recent.Remove(entry.Node);
        _bytes -= entry.Bytes;
    }

    private sealed record Entry(string Hash, VfxDocument Document, long Bytes, LinkedListNode<string> Node);
}
