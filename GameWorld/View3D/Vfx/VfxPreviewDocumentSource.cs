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
        if (_live.TryGet(_file, out var snapshot)) return snapshot == null ? null : snapshot with { Resolve = Resolve, Quality = quality };
        return new(_file.DataSource.ReadData(), Path, Resolve, quality);
    }
    private byte[]? Resolve(string path)
    {
        if (_files.GetPackFileContainer(_file) == null && _files.FindFile(path) is { } globalFile)
            return ReadResource(globalFile);
        var containers = new[] { _files.GetPackFileContainer(_file), _files.GetEditablePack() }
            .Concat(_files.GetAllPackfileContainers().AsEnumerable().Reverse()).Where(x => x != null).Distinct();
        foreach (var container in containers)
        {
            var file = _files.FindFile(path, container!);
            if (file == null) continue;
            return ReadResource(file);
        }
        return null;
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
