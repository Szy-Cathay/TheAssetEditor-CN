using Shared.Core.PackFiles.Utility;
using Shared.Core.PackFiles.Serialization;

namespace Shared.Core.PackFiles.Models;

public readonly record struct FolderProjectReconciliation(
    int Added,
    int Removed,
    int Updated);

public sealed class FolderProjectReconciledEventArgs(
    FolderProjectReconciliation changes,
    IReadOnlyList<PackFile> addedFiles,
    IReadOnlyList<PackFile> removedFiles,
    IReadOnlyList<PackFile> updatedFiles,
    bool directoriesChanged) : EventArgs
{
    public FolderProjectReconciliation Changes { get; } = changes;
    public IReadOnlyList<PackFile> AddedFiles { get; } = addedFiles;
    public IReadOnlyList<PackFile> RemovedFiles { get; } = removedFiles;
    public IReadOnlyList<PackFile> UpdatedFiles { get; } = updatedFiles;
    public bool DirectoriesChanged { get; } = directoriesChanged;
}

public sealed class FolderProjectContainer :
    PackFileContainer,
    IDisposable
{
    private readonly object _stateGate = new();
    private readonly object _watcherReconciliationGate = new();
    private readonly SynchronizationContext? _synchronizationContext =
        SynchronizationContext.Current;
    private Dictionary<string, FileFingerprint> _fingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcherWrapper? _watcher;
    private Timer? _debounceTimer;
    private bool _watcherSettingsDirty;
    private FolderProjectReconciledEventArgs?
        _pendingWatcherReconciliation;
    private bool _disposed;

    internal Func<string, IEnumerable<string>> EnumerateFileSystemEntries
    {
        get;
        set;
    } = Directory.EnumerateFileSystemEntries;

    internal Func<string, FileAttributes> GetFileAttributes
    {
        get;
        set;
    } = File.GetAttributes;

    public string ProjectRoot => SystemFilePath;
    public FolderProjectSettings ProjectSettings { get; }
    public HashSet<string> EmptyDirectories { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public bool IsWatching
    {
        get
        {
            lock (_stateGate)
                return _watcher != null;
        }
    }

    public event EventHandler<FolderProjectReconciledEventArgs>?
        FilesReconciled;

    private FolderProjectContainer(
        string projectRoot,
        FolderProjectSettings settings)
        : base(GetProjectName(projectRoot, settings))
    {
        SystemFilePath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        ProjectSettings = settings;
        Header = new PFHeader(
            PackFileVersionConverter.ToString(settings.PackFileVersion),
            settings.PackFileType)
        {
            DependantFiles = settings.DependantFiles.ToList(),
        };
    }

    public static FolderProjectContainer Create(
        string projectRoot,
        FolderProjectSettings settings)
    {
        ValidateRoot(projectRoot);
        if (!string.IsNullOrWhiteSpace(settings.OutputPackPath))
        {
            FolderProjectPathPolicy.EnsureOutputOutsideProject(
                projectRoot,
                settings.OutputPackPath);
        }
        settings.Name = GetProjectName(projectRoot, settings);
        settings.Save(projectRoot);

        var container = new FolderProjectContainer(
            projectRoot,
            settings);
        container.RefreshFromDisk();
        return container;
    }

    public static FolderProjectContainer Open(string projectRoot)
    {
        ValidateRoot(projectRoot);
        var settings = FolderProjectSettings.Load(
            projectRoot,
            out var requiresSave);
        var previousName = settings.Name;
        settings.Name = GetProjectName(projectRoot, settings);
        requiresSave |= !string.Equals(
            previousName,
            settings.Name,
            StringComparison.Ordinal);
        requiresSave |= RecreateConfiguredEmptyDirectories(
            projectRoot,
            settings);

        var container = new FolderProjectContainer(
            projectRoot,
            settings);
        container.EmptyDirectories.UnionWith(
            settings.EmptyDirectories);
        var eventArgs = container.RefreshFromDiskDetailed();
        requiresSave |= eventArgs.DirectoriesChanged;
        if (requiresSave)
            settings.Save(container.ProjectRoot);

        return container;
    }

    public FolderProjectReconciliation RefreshFromDisk()
    {
        var eventArgs = RefreshFromDiskDetailed();
        if (eventArgs.DirectoriesChanged)
            SaveSettings();
        return eventArgs.Changes;
    }

    private FolderProjectReconciledEventArgs RefreshFromDiskDetailed()
    {
        lock (_stateGate)
        {
            ThrowIfDisposed();
            VirtualizeMaterializedCorruptionDetectionFiles();
            var previousEmptyDirectories =
                new HashSet<string>(
                    EmptyDirectories,
                    StringComparer.OrdinalIgnoreCase);
            var scannedFiles =
                new Dictionary<string, PackFile>(
                    StringComparer.OrdinalIgnoreCase);
            var scannedDirectories =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scannedFingerprints =
                new Dictionary<string, FileFingerprint>(
                    StringComparer.OrdinalIgnoreCase);
            ScanDirectory(
                ProjectRoot,
                scannedFiles,
                scannedDirectories,
                scannedFingerprints);

            var removedFiles = FileList
                .Where(pair => !scannedFiles.ContainsKey(pair.Key))
                .Select(pair => pair.Value)
                .ToList();
            var addedFiles = new List<PackFile>();
            var updatedFiles = new List<PackFile>();
            foreach (var path in scannedFiles.Keys.ToList())
            {
                var scannedFile = scannedFiles[path];
                if (!FileList.TryGetValue(path, out var existingFile))
                {
                    addedFiles.Add(scannedFile);
                    continue;
                }

                var fingerprintChanged =
                    !_fingerprints.TryGetValue(
                        path,
                        out var previousFingerprint) ||
                    previousFingerprint != scannedFingerprints[path];
                var nameChanged = !string.Equals(
                    existingFile.Name,
                    scannedFile.Name,
                    StringComparison.Ordinal);
                if (fingerprintChanged || nameChanged)
                {
                    existingFile.Name = scannedFile.Name;
                    existingFile.DataSource = scannedFile.DataSource;
                    updatedFiles.Add(existingFile);
                }

                scannedFiles[path] = existingFile;
            }

            foreach (var removedPath in FileList.Keys
                         .Where(path => !scannedFiles.ContainsKey(path))
                         .ToList())
            {
                FileList.Remove(removedPath);
            }
            foreach (var (path, file) in scannedFiles)
                FileList[path] = file;

            _fingerprints = scannedFingerprints;
            EmptyDirectories.Clear();
            EmptyDirectories.UnionWith(scannedDirectories);
            ProjectSettings.EmptyDirectories =
                new HashSet<string>(
                    scannedDirectories,
                    StringComparer.OrdinalIgnoreCase);

            var changes = new FolderProjectReconciliation(
                addedFiles.Count,
                removedFiles.Count,
                updatedFiles.Count);
            return new FolderProjectReconciledEventArgs(
                changes,
                addedFiles,
                removedFiles,
                updatedFiles,
                !previousEmptyDirectories.SetEquals(
                    scannedDirectories));
        }
    }

    private void VirtualizeMaterializedCorruptionDetectionFiles()
    {
        var removed = false;
        foreach (var relativePath in FolderProjectPathPolicy
                     .CorruptionDetectionFilePaths)
        {
            var fullPath = Path.Combine(ProjectRoot, relativePath);
            var directory = Path.GetDirectoryName(fullPath)!;
            if (Directory.Exists(directory) &&
                (File.GetAttributes(directory) &
                 FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                removed = true;
            }
            if (Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        if (removed &&
            !ProjectSettings.EnablePackFileCorruptionDetection)
        {
            ProjectSettings.EnablePackFileCorruptionDetection = true;
            ProjectSettings.Save(ProjectRoot);
        }
    }

    public void StartWatching()
    {
        lock (_stateGate)
        {
            ThrowIfDisposed();
            if (_watcher != null)
                return;

            _debounceTimer = new Timer(
                OnDebounceElapsed,
                null,
                Timeout.Infinite,
                Timeout.Infinite);
            _watcher = new FileSystemWatcherWrapper(ProjectRoot);
            _watcher.ProjectChanged += OnProjectChanged;
            _watcher.Start();
        }

        ReconcileAfterWatcherEvent();
    }

    public void SetIgnored(string relativePath, bool ignored)
    {
        ExecuteSynchronized(
            () =>
            {
                var normalized =
                    FolderProjectPathPolicy.EnsureResourceDirectoryPath(
                        relativePath);
                if (ignored)
                    ProjectSettings.IgnoredPaths.Add(normalized);
                else
                    ProjectSettings.IgnoredPaths.Remove(normalized);

                ProjectSettings.Save(ProjectRoot);
            });
    }

    public bool IsPackFileCorruptionDetectionEnabled()
    {
        return ExecuteSynchronized(
            () => ProjectSettings
                .EnablePackFileCorruptionDetection);
    }

    public void SetPackFileCorruptionDetectionEnabled(bool enabled)
    {
        ExecuteSynchronized(
            () =>
            {
                ProjectSettings.EnablePackFileCorruptionDetection =
                    enabled;
                ProjectSettings.Save(ProjectRoot);
            });
    }

    public bool IsIgnored(string relativePath)
    {
        return ExecuteSynchronized(
            () =>
            {
                var normalized =
                    FolderProjectPathPolicy.NormalizeRelativePath(
                        relativePath);
                return ProjectSettings.IgnoredPaths.Any(
                    ignored =>
                        normalized.Equals(
                            ignored,
                            StringComparison.OrdinalIgnoreCase) ||
                        normalized.StartsWith(
                            ignored + "\\",
                            StringComparison.OrdinalIgnoreCase));
            });
    }

    public void SaveSettings()
    {
        ExecuteSynchronized(
            () => ProjectSettings.Save(ProjectRoot));
    }

    public void CreateDirectoryOnDisk(string relativePath)
    {
        ExecuteSynchronized(
            () =>
            {
                var normalized =
                    FolderProjectPathPolicy.EnsureResourceDirectoryPath(
                        relativePath);
                var fullPath = FolderProjectPathPolicy.ResolveFilePath(
                    ProjectRoot,
                    normalized);
                Directory.CreateDirectory(fullPath);
                EmptyDirectories.Add(normalized);
                SaveEmptyDirectorySettings();
            });
    }

    public List<PackFile> AddFiles(
        IReadOnlyList<Shared.Core.PackFiles.NewPackFileEntry> newFiles,
        bool overwriteExisting = false)
    {
        return ExecuteSynchronized(
            () =>
            {
                var writes = new List<(
                    string RelativePath,
                    string FullPath,
                    byte[] Data,
                    bool Overwrite)>();
                foreach (var entry in newFiles)
                {
                    if (string.IsNullOrWhiteSpace(entry.PackFile.Name))
                    {
                        throw new InvalidDataException(
                            "The file name is empty.");
                    }

                    var relativePath =
                        FolderProjectPathPolicy.EnsureResourcePath(
                            Path.Combine(
                                entry.DirectoyPath ?? "",
                                entry.PackFile.Name.Trim()));
                    var fullPath =
                        FolderProjectPathPolicy.ResolveFilePath(
                        ProjectRoot,
                        relativePath);
                    var overwrite = File.Exists(fullPath);
                    if (overwrite && !overwriteExisting)
                    {
                        throw new IOException(
                            $"The destination already exists: {fullPath}");
                    }
                    writes.Add(
                        (
                            relativePath,
                            fullPath,
                            entry.PackFile.DataSource.ReadData(),
                            overwrite));
                }

                var addedFiles = new List<PackFile>();
                foreach (var write in writes)
                {
                    WriteAllBytesAtomic(
                        write.FullPath,
                        write.Data,
                        write.Overwrite);
                    var packFile = PackFile.CreateFromFileSystem(
                        Path.GetFileName(write.FullPath),
                        write.FullPath);
                    FileList[write.RelativePath.ToLowerInvariant()] =
                        packFile;
                    UpdateFingerprint(
                        write.RelativePath,
                        write.FullPath);
                    MarkPathHasContent(write.RelativePath);
                    addedFiles.Add(packFile);
                }

                SaveEmptyDirectorySettings();
                return addedFiles;
            });
    }

    public void SaveFileData(PackFile file, byte[] data)
    {
        ExecuteSynchronized(
            () =>
            {
                var key = FindKey(file);
                var fullPath = FolderProjectPathPolicy.ResolveFilePath(
                    ProjectRoot,
                    key);
                WriteAllBytesAtomic(fullPath, data, overwrite: true);
                file.DataSource = new FileSystemSource(fullPath);
                UpdateFingerprint(key, fullPath);
            });
    }

    public PackFile DeleteFileFromDisk(PackFile file)
    {
        return ExecuteSynchronized(
            () =>
            {
                var key = FindKey(file);
                var fullPath = FolderProjectPathPolicy.ResolveFilePath(
                    ProjectRoot,
                    key);
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
                FileList.Remove(key);
                _fingerprints.Remove(key);
                RemoveIgnoredPaths(key);
                MarkParentEmptyIfNeeded(key);
                SaveEmptyDirectorySettings();
                return file;
            });
    }

    public IReadOnlyList<PackFile> DeleteFolderFromDisk(string folder)
    {
        return ExecuteSynchronized(
            () =>
            {
                var normalized =
                    FolderProjectPathPolicy.EnsureResourceDirectoryPath(
                        folder);
                var fullPath = FolderProjectPathPolicy.ResolveFilePath(
                    ProjectRoot,
                    normalized);
                if (Directory.Exists(fullPath))
                    Directory.Delete(fullPath, true);

                var keys = FileList.Keys
                    .Where(
                        key =>
                            key.Equals(
                                normalized,
                                StringComparison.OrdinalIgnoreCase) ||
                            key.StartsWith(
                                normalized + "\\",
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var removed =
                    keys.Select(key => FileList[key]).ToList();
                foreach (var key in keys)
                {
                    FileList.Remove(key);
                    _fingerprints.Remove(key);
                }

                RemoveIgnoredPaths(normalized);
                EmptyDirectories.RemoveWhere(
                    path =>
                        path.Equals(
                            normalized,
                            StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith(
                            normalized + "\\",
                            StringComparison.OrdinalIgnoreCase));
                MarkParentEmptyIfNeeded(normalized);
                SaveEmptyDirectorySettings();
                return removed;
            });
    }

    public string MoveFileOnDisk(
        PackFile file,
        string newFolderPath)
    {
        return ExecuteSynchronized(
            () =>
            {
                var oldKey = FindKey(file);
                var newKey =
                    FolderProjectPathPolicy.EnsureResourcePath(
                        Path.Combine(
                            newFolderPath ?? "",
                            file.Name));
                MoveFileCore(file, oldKey, newKey);
                MoveIgnoredPaths(oldKey, newKey);
                MarkParentEmptyIfNeeded(oldKey);
                MarkPathHasContent(newKey);
                SaveEmptyDirectorySettings();
                return newKey;
            });
    }

    public string RenameFileOnDisk(PackFile file, string newName)
    {
        return ExecuteSynchronized(
            () =>
            {
                var oldKey = FindKey(file);
                var directory = Path.GetDirectoryName(oldKey);
                var newKey =
                    FolderProjectPathPolicy.EnsureResourcePath(
                        Path.Combine(directory ?? "", newName));
                MoveFileCore(file, oldKey, newKey);
                MoveIgnoredPaths(oldKey, newKey);
                file.Name = newName;
                MarkParentEmptyIfNeeded(oldKey);
                MarkPathHasContent(newKey);
                SaveEmptyDirectorySettings();
                return newKey;
            });
    }

    public string RenameDirectoryOnDisk(
        string currentPath,
        string newName)
    {
        return ExecuteSynchronized(
            () =>
            {
                var oldPath =
                    FolderProjectPathPolicy.EnsureResourceDirectoryPath(
                        currentPath);
                var parent = Path.GetDirectoryName(oldPath);
                var newPath =
                    FolderProjectPathPolicy.EnsureResourceDirectoryPath(
                        Path.Combine(parent ?? "", newName));
                var oldFullPath =
                    FolderProjectPathPolicy.ResolveFilePath(
                        ProjectRoot,
                        oldPath);
                var newFullPath =
                    FolderProjectPathPolicy.ResolveFilePath(
                        ProjectRoot,
                        newPath);

                MovePathCaseAware(oldFullPath, newFullPath, true);

                var oldPrefix = oldPath + "\\";
                var affected = FileList
                    .Where(
                        pair =>
                            pair.Key.StartsWith(
                                oldPrefix,
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var (oldKey, file) in affected)
                {
                    var suffix = oldKey[oldPath.Length..];
                    var newKey =
                        (newPath + suffix).ToLowerInvariant();
                    FileList.Remove(oldKey);
                    _fingerprints.Remove(oldKey);
                    var filePath =
                        FolderProjectPathPolicy.ResolveFilePath(
                            ProjectRoot,
                            newKey);
                    file.DataSource = new FileSystemSource(filePath);
                    FileList[newKey] = file;
                    UpdateFingerprint(newKey, filePath);
                }

                var renamedEmptyDirectories = EmptyDirectories
                    .Where(
                        path =>
                            path.Equals(
                                oldPath,
                                StringComparison.OrdinalIgnoreCase) ||
                            path.StartsWith(
                                oldPrefix,
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var oldEmptyPath in renamedEmptyDirectories)
                {
                    EmptyDirectories.Remove(oldEmptyPath);
                    EmptyDirectories.Add(
                        newPath +
                        oldEmptyPath[oldPath.Length..]);
                }
                MoveIgnoredPaths(oldPath, newPath);
                SaveEmptyDirectorySettings();
                return newPath;
            });
    }

    public void Dispose()
    {
        FileSystemWatcherWrapper? watcher;
        Timer? debounceTimer;
        lock (_stateGate)
        {
            if (_disposed)
                return;

            PersistWatcherSettings();
            _disposed = true;
            watcher = _watcher;
            _watcher = null;
            debounceTimer = _debounceTimer;
            _debounceTimer = null;
            FileList.Clear();
            _fingerprints.Clear();
            EmptyDirectories.Clear();
            _pendingWatcherReconciliation = null;
        }

        if (watcher != null)
        {
            watcher.ProjectChanged -= OnProjectChanged;
            watcher.Dispose();
        }
        debounceTimer?.Dispose();
    }

    private void ScanDirectory(
        string directory,
        IDictionary<string, PackFile> files,
        ISet<string> emptyDirectories,
        IDictionary<string, FileFingerprint> fingerprints)
    {
        if (IsReparsePoint(directory))
            return;

        var hasIncludedContent = false;
        foreach (var entry in EnumerateFileSystemEntries(directory))
        {
            if (IsReparsePoint(entry))
                continue;

            var relative = FolderProjectPathPolicy.NormalizeRelativePath(
                Path.GetRelativePath(ProjectRoot, entry));
            var isDirectory = Directory.Exists(entry);
            var isFile = File.Exists(entry);
            if ((isDirectory &&
                 FolderProjectPathPolicy.IsMetadataDirectoryPath(
                     relative)) ||
                (isFile &&
                 (FolderProjectPathPolicy.IsMetadataPath(relative) ||
                  FolderProjectPathPolicy.IsControlFile(relative))))
                continue;

            if (isDirectory)
            {
                var directoriesBefore = emptyDirectories.Count;
                var filesBefore = files.Count;
                ScanDirectory(
                    entry,
                    files,
                    emptyDirectories,
                    fingerprints);
                if (files.Count != filesBefore ||
                    emptyDirectories.Count != directoriesBefore)
                {
                    hasIncludedContent = true;
                }
            }
            else if (isFile)
            {
                var fileName = Path.GetFileName(entry);
                files[relative.ToLowerInvariant()] =
                    PackFile.CreateFromFileSystem(fileName, entry);
                var fileInfo = new FileInfo(entry);
                fingerprints[relative.ToLowerInvariant()] =
                    new FileFingerprint(
                        fileInfo.Length,
                        fileInfo.LastWriteTimeUtc.Ticks);
                hasIncludedContent = true;
            }
        }

        if (!hasIncludedContent &&
            !directory.Equals(
                ProjectRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            emptyDirectories.Add(
                FolderProjectPathPolicy.NormalizeRelativePath(
                    Path.GetRelativePath(ProjectRoot, directory)));
        }
    }

    private void ApplyWatcherPathChange(
        FolderProjectFileSystemChangedEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Renamed &&
            e.OldFullPath != null &&
            e.FullPath != null)
        {
            var isDirectory = Directory.Exists(e.FullPath);
            var oldIsResource = TryGetProjectRelativePath(
                e.OldFullPath,
                out var oldPath,
                isDirectory);
            var newIsResource = TryGetProjectRelativePath(
                e.FullPath,
                out var newPath,
                isDirectory);
            if (oldIsResource && newIsResource)
            {
                _watcherSettingsDirty |=
                    MoveIgnoredPaths(oldPath, newPath);
            }
            else if (oldIsResource)
            {
                _watcherSettingsDirty |=
                    RemoveIgnoredPaths(oldPath);
            }
        }
        else if (e.ChangeType == WatcherChangeTypes.Deleted &&
                 (TryGetProjectRelativePath(
                      e.FullPath,
                      out var deletedPath) ||
                  TryGetDeletedResourceDirectoryPath(
                      e.FullPath,
                      out deletedPath)))
        {
            _watcherSettingsDirty |=
                RemoveIgnoredPaths(deletedPath);
        }
    }

    private bool TryGetDeletedResourceDirectoryPath(
        string? fullPath,
        out string relativePath)
    {
        if (!TryGetProjectRelativePath(
                fullPath,
                out relativePath,
                isDirectory: true))
        {
            return false;
        }

        var prefix = relativePath + "\\";
        return EmptyDirectories.Contains(relativePath) ||
               FileList.Keys.Any(
                   path => path.StartsWith(
                       prefix,
                       StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetProjectRelativePath(
        string? fullPath,
        out string relativePath,
        bool isDirectory = false)
    {
        relativePath = "";
        if (string.IsNullOrWhiteSpace(fullPath))
            return false;

        try
        {
            var candidate = Path.GetRelativePath(
                ProjectRoot,
                Path.GetFullPath(fullPath));
            if (Path.IsPathRooted(candidate) ||
                candidate.Equals(
                    "..",
                    StringComparison.Ordinal) ||
                candidate.StartsWith(
                    @"..\",
                    StringComparison.Ordinal))
            {
                return false;
            }

            relativePath =
                FolderProjectPathPolicy.NormalizeRelativePath(
                    candidate);
            return !(isDirectory
                       ? FolderProjectPathPolicy
                           .IsMetadataDirectoryPath(relativePath)
                       : FolderProjectPathPolicy
                           .IsMetadataPath(relativePath)) &&
                   (isDirectory ||
                    !FolderProjectPathPolicy.IsControlFile(
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

    private void OnProjectChanged(
        object? sender,
        FolderProjectFileSystemChangedEventArgs e)
    {
        ProcessWatcherChange(e);
    }

    internal void ProcessWatcherChange(
        FolderProjectFileSystemChangedEventArgs e)
    {
        try
        {
            lock (_stateGate)
            {
                if (_disposed)
                    return;

                ApplyWatcherPathChange(e);
                PersistWatcherSettings();
                _debounceTimer?.Change(300, Timeout.Infinite);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException)
        {
            ScheduleWatcherRetry();
        }
    }

    private void PersistWatcherSettings(bool settingsChanged = false)
    {
        lock (_stateGate)
        {
            ThrowIfDisposed();
            _watcherSettingsDirty |= settingsChanged;
            if (!_watcherSettingsDirty)
                return;

            ProjectSettings.Save(ProjectRoot);
            _watcherSettingsDirty = false;
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        SynchronizationContext? synchronizationContext;
        lock (_stateGate)
        {
            if (_disposed)
                return;

            synchronizationContext = _synchronizationContext;
        }

        if (synchronizationContext != null)
        {
            synchronizationContext.Post(
                _ => ReconcileAfterWatcherEvent(),
                null);
        }
        else
            ReconcileAfterWatcherEvent();
    }

    internal void ReconcileAfterWatcherEvent()
    {
        lock (_watcherReconciliationGate)
        {
            try
            {
                PersistWatcherSettings();
                PublishPendingWatcherReconciliation();

                var eventArgs = RefreshFromDiskDetailed();
                QueueWatcherReconciliation(eventArgs);
                PersistWatcherSettings(eventArgs.DirectoriesChanged);
                PublishPendingWatcherReconciliation();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
                when (exception is IOException or
                      UnauthorizedAccessException)
            {
                ScheduleWatcherRetry();
            }
        }
    }

    private void ScheduleWatcherRetry()
    {
        lock (_stateGate)
        {
            if (!_disposed)
                _debounceTimer?.Change(300, Timeout.Infinite);
        }
    }

    private void QueueWatcherReconciliation(
        FolderProjectReconciledEventArgs eventArgs)
    {
        if (!HasChanges(eventArgs))
            return;

        lock (_stateGate)
        {
            _pendingWatcherReconciliation = eventArgs;
        }
    }

    private void PublishPendingWatcherReconciliation()
    {
        FolderProjectReconciledEventArgs? pending;
        lock (_stateGate)
        {
            pending = _pendingWatcherReconciliation;
            _pendingWatcherReconciliation = null;
        }

        if (pending != null)
            FilesReconciled?.Invoke(this, pending);
    }

    private static bool HasChanges(
        FolderProjectReconciledEventArgs eventArgs)
    {
        return eventArgs.Changes.Added != 0 ||
               eventArgs.Changes.Removed != 0 ||
               eventArgs.Changes.Updated != 0 ||
               eventArgs.DirectoriesChanged;
    }

    private bool IsReparsePoint(string path)
    {
        return (GetFileAttributes(path) &
                FileAttributes.ReparsePoint) != 0;
    }

    private static void ValidateRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) ||
            !Directory.Exists(projectRoot))
        {
            throw new DirectoryNotFoundException(projectRoot);
        }

        FolderProjectPathPolicy.ResolveFilePath(
            projectRoot,
            ".folder-project-root-check");
    }

    private static bool RecreateConfiguredEmptyDirectories(
        string projectRoot,
        FolderProjectSettings settings)
    {
        var validPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var configuredPath in settings.EmptyDirectories)
        {
            if (!FolderProjectPathPolicy.TryNormalizeResourceDirectoryPath(
                    configuredPath,
                    out var normalized))
            {
                continue;
            }

            var fullPath = FolderProjectPathPolicy.ResolveFilePath(
                projectRoot,
                normalized);
            if (File.Exists(fullPath))
                continue;

            Directory.CreateDirectory(fullPath);
            validPaths.Add(normalized);
        }

        var settingsChanged =
            !settings.EmptyDirectories.SetEquals(validPaths);
        settings.EmptyDirectories = validPaths;
        return settingsChanged;
    }

    private static string GetProjectName(
        string projectRoot,
        FolderProjectSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Name))
            return settings.Name.Trim();

        return Path.GetFileName(
                   Path.TrimEndingDirectorySeparator(
                       Path.GetFullPath(projectRoot))) ??
               "工程";
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal void ExecuteSynchronized(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_stateGate)
        {
            ThrowIfDisposed();
            action();
        }
    }

    internal T ExecuteSynchronized<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_stateGate)
        {
            ThrowIfDisposed();
            return action();
        }
    }

    private string FindKey(PackFile file)
    {
        var key = FileList
            .FirstOrDefault(
                pair => ReferenceEquals(pair.Value, file))
            .Key;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                $"The file '{file.Name}' is not part of this project.");
        return key;
    }

    private void MoveFileCore(
        PackFile file,
        string oldKey,
        string newKey)
    {
        newKey = FolderProjectPathPolicy.EnsureResourcePath(newKey);
        var oldPath = FolderProjectPathPolicy.ResolveFilePath(
            ProjectRoot,
            oldKey);
        var newPath = FolderProjectPathPolicy.ResolveFilePath(
            ProjectRoot,
            newKey);
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        MovePathCaseAware(oldPath, newPath, false);

        FileList.Remove(oldKey);
        _fingerprints.Remove(oldKey);
        file.DataSource = new FileSystemSource(newPath);
        FileList[newKey.ToLowerInvariant()] = file;
        UpdateFingerprint(newKey, newPath);
    }

    private bool MoveIgnoredPaths(
        string oldPath,
        string newPath)
    {
        var oldPrefix = oldPath + "\\";
        var affected = ProjectSettings.IgnoredPaths
            .Where(
                path =>
                    path.Equals(
                        oldPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(
                        oldPrefix,
                        StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var ignoredPath in affected)
        {
            ProjectSettings.IgnoredPaths.Remove(ignoredPath);
            ProjectSettings.IgnoredPaths.Add(
                newPath + ignoredPath[oldPath.Length..]);
        }
        return affected.Count != 0;
    }

    private bool RemoveIgnoredPaths(string path)
    {
        var prefix = path + "\\";
        return ProjectSettings.IgnoredPaths.RemoveWhere(
                   ignoredPath =>
                       ignoredPath.Equals(
                           path,
                           StringComparison.OrdinalIgnoreCase) ||
                       ignoredPath.StartsWith(
                           prefix,
                           StringComparison.OrdinalIgnoreCase)) != 0;
    }

    private void MarkPathHasContent(string relativePath)
    {
        var directory = Path.GetDirectoryName(relativePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            EmptyDirectories.Remove(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private void MarkParentEmptyIfNeeded(string relativePath)
    {
        var directory = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrWhiteSpace(directory))
            return;

        var fullPath = FolderProjectPathPolicy.ResolveFilePath(
            ProjectRoot,
            directory);
        if (Directory.Exists(fullPath) &&
            !Directory.EnumerateFileSystemEntries(fullPath).Any())
        {
            EmptyDirectories.Add(directory);
        }
    }

    private void SaveEmptyDirectorySettings()
    {
        ProjectSettings.EmptyDirectories =
            new HashSet<string>(
                EmptyDirectories,
                StringComparer.OrdinalIgnoreCase);
        ProjectSettings.Save(ProjectRoot);
    }

    private static void MovePathCaseAware(
        string oldPath,
        string newPath,
        bool directory)
    {
        var caseOnlyRename = oldPath.Equals(
            newPath,
            StringComparison.OrdinalIgnoreCase);
        if (!caseOnlyRename)
        {
            if (File.Exists(newPath) || Directory.Exists(newPath))
                throw new IOException($"The destination already exists: {newPath}");

            if (directory)
                Directory.Move(oldPath, newPath);
            else
                File.Move(oldPath, newPath);
            return;
        }

        var temporaryPath = $"{oldPath}.{Guid.NewGuid():N}.rename";
        if (directory)
        {
            Directory.Move(oldPath, temporaryPath);
            Directory.Move(temporaryPath, newPath);
        }
        else
        {
            File.Move(oldPath, temporaryPath);
            File.Move(temporaryPath, newPath);
        }
    }

    private static void WriteAllBytesAtomic(
        string fullPath,
        byte[] data,
        bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, data);
            File.Move(tempPath, fullPath, overwrite);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private void UpdateFingerprint(
        string relativePath,
        string fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        _fingerprints[relativePath.ToLowerInvariant()] =
            new FileFingerprint(
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.Ticks);
    }

    private readonly record struct FileFingerprint(
        long Length,
        long LastWriteTicks);
}
