namespace Shared.Core.PackFiles.Utility;

public sealed class FolderProjectFileSystemChangedEventArgs(
    WatcherChangeTypes changeType,
    string? fullPath,
    string? oldFullPath = null) : EventArgs
{
    public WatcherChangeTypes ChangeType { get; } = changeType;
    public string? FullPath { get; } = fullPath;
    public string? OldFullPath { get; } = oldFullPath;
}

public sealed class FileSystemWatcherWrapper : IDisposable
{
    private readonly string _projectRoot;
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;

    public event EventHandler<FolderProjectFileSystemChangedEventArgs>?
        ProjectChanged;

    public FileSystemWatcherWrapper(string projectRoot)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _watcher = new FileSystemWatcher(projectRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite |
                NotifyFilters.Size,
        };
        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnChanged;
        _watcher.Changed -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnError;
        _watcher.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        var isDirectory = Directory.Exists(e.FullPath);
        if (IsExcludedPath(
                e.FullPath,
                treatControlNameAsFile:
                    e.ChangeType != WatcherChangeTypes.Deleted &&
                    !isDirectory))
            return;

        ProjectChanged?.Invoke(
            this,
            new FolderProjectFileSystemChangedEventArgs(
                e.ChangeType,
                e.FullPath));
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        var isDirectory = Directory.Exists(e.FullPath);
        if (IsExcludedPath(
                e.FullPath,
                treatControlNameAsFile: !isDirectory) &&
            IsExcludedPath(
                e.OldFullPath,
                treatControlNameAsFile: !isDirectory))
        {
            return;
        }

        ProjectChanged?.Invoke(
            this,
            new FolderProjectFileSystemChangedEventArgs(
                WatcherChangeTypes.Renamed,
                e.FullPath,
                e.OldFullPath));
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        ProjectChanged?.Invoke(
            this,
            new FolderProjectFileSystemChangedEventArgs(
                WatcherChangeTypes.All,
                null));
    }

    private bool IsExcludedPath(
        string fullPath,
        bool treatControlNameAsFile)
    {
        try
        {
            var relativePath = Path.GetRelativePath(
                _projectRoot,
                Path.GetFullPath(fullPath));
            var isMetadata = treatControlNameAsFile
                ? FolderProjectPathPolicy.IsMetadataPath(
                    relativePath)
                : FolderProjectPathPolicy.IsMetadataDirectoryPath(
                    relativePath);
            return isMetadata ||
                   (treatControlNameAsFile &&
                    FolderProjectPathPolicy.IsControlFile(
                        relativePath));
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidDataException or
                  NotSupportedException)
        {
            return false;
        }
    }
}
