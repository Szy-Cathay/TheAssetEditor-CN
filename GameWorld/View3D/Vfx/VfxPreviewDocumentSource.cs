using System.IO;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace GameWorld.Core.Vfx;

// Actual file identity keeps identically named resources from different packs isolated.
public sealed class VfxPreviewDocumentStore
{
    private readonly object _gate = new();
    private readonly Dictionary<object, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private long _revision;
    public event Action<object>? Changed;
    public void Publish(object owner, object file, VfxPreviewSnapshot? snapshot)
    {
        object? previous = null;
        lock (_gate)
        {
            if (_entries.TryGetValue(owner, out var entry)) previous = entry.File;
            _entries[owner] = new(file, snapshot, ++_revision);
        }
        if (previous != null && !ReferenceEquals(previous, file)) Changed?.Invoke(previous);
        Changed?.Invoke(file);
    }
    public bool TryGet(object file, out VfxPreviewSnapshot? snapshot)
    {
        lock (_gate)
        {
            var entry = _entries.Values.Where(x => ReferenceEquals(x.File, file)).MaxBy(x => x.Revision);
            snapshot = entry?.Snapshot;
            return entry != null;
        }
    }
    public byte[] ReadResource(PackFile file)
    {
        if (TryGet(file, out var snapshot))
            return snapshot?.GameBytes ?? throw new InvalidDataException("A referenced VFX has invalid unsaved values.");
        return file.DataSource.ReadData();
    }
    public Func<byte[]> CaptureResourceRead(PackFile file)
    {
        if (TryGet(file, out var snapshot))
        {
            var bytes = snapshot?.GameBytes ?? throw new InvalidDataException("A referenced VFX has invalid unsaved values.");
            return () => bytes;
        }
        return CaptureDataSourceRead(file.DataSource);
    }
    public static Func<byte[]> CaptureDataSourceRead(IDataSource source)
    {
        // Unknown implementations may own editor state. Their reads must remain on the caller.
        if (source.GetType() == typeof(PackedFileSource))
        {
            var packed = (PackedFileSource)source;
            var captured = packed with { };
            var path = packed.Parent.FilePath;
            return () =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return captured.ReadData(stream);
            };
        }
        if (source.GetType() == typeof(FileSystemSource))
        {
            var path = ((FileSystemSource)source).FilePath;
            return () => File.ReadAllBytes(path);
        }
        var bytes = source.ReadData();
        return () => bytes;
    }
    public void Remove(object owner)
    {
        object? file = null;
        lock (_gate) if (_entries.Remove(owner, out var entry)) file = entry.File;
        if (file != null) Changed?.Invoke(file);
    }
    private sealed record Entry(object File, VfxPreviewSnapshot? Snapshot, long Revision);
}

public sealed class VfxPreviewDocumentSource : IDisposable
{
    private readonly IPackFileService _files;
    private readonly PackFile _file;
    private readonly VfxPreviewDocumentStore _live;
    private readonly HashSet<object> _dependencies = new(ReferenceEqualityComparer.Instance);
    private readonly object _gate = new();
    private bool _disposed;
    public string Path { get; }
    public event Action? Changed;
    public VfxPreviewDocumentSource(IPackFileService files, PackFile file, VfxPreviewDocumentStore live)
    {
        _files = files; _file = file; _live = live;
        Path = files.GetFullPath(file);
        _dependencies.Add(file);
        live.Changed += OnChanged;
    }
    public VfxPreviewSnapshot? Read(string quality = "0")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate) { _dependencies.Clear(); _dependencies.Add(_file); }
        PackFileContainer? owner = null;
        PackFileContainer?[]? containers = null;
        PackFile? Find(string path)
        {
            if (containers == null)
            {
                owner = _files.GetPackFileContainer(_file);
                containers = new[] { owner, _files.GetEditablePack() }
                    .Concat(_files.GetAllPackfileContainers().AsEnumerable().Reverse()).Where(x => x != null).Distinct().ToArray();
            }
            if (owner == null && _files.FindFile(path) is { } globalFile) return globalFile;
            foreach (var container in containers)
            {
                var file = _files.FindFile(path, container!);
                if (file != null) return file;
            }
            return null;
        }
        byte[]? Resolve(string path) => Find(path) is { } file ? ReadResource(file) : null;
        Func<byte[]?>? CaptureRead(string path)
        {
            if (Find(path) is not { } file) return null;
            lock (_gate) _dependencies.Add(file);
            return _live.CaptureResourceRead(file);
        }
        if (_live.TryGet(_file, out var snapshot)) return snapshot == null ? null
            : snapshot with { Resolve = Resolve, Quality = quality, CaptureResourceRead = CaptureRead };
        return new(_file.DataSource.ReadData(), Path, Resolve, quality) { CaptureResourceRead = CaptureRead };
    }
    private byte[] ReadResource(PackFile file)
    {
        lock (_gate) _dependencies.Add(file);
        return _live.ReadResource(file);
    }
    private void OnChanged(object file)
    {
        bool affected;
        lock (_gate) affected = !_disposed && _dependencies.Contains(file);
        if (affected) Changed?.Invoke();
    }
    public bool DependsOn(object file)
    {
        lock (_gate) return _dependencies.Contains(file);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _live.Changed -= OnChanged;
        Changed = null;
        lock (_gate) _dependencies.Clear();
    }
}
