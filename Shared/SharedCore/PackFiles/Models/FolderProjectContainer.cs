using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
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
    bool directoriesChanged,
    FolderProjectChangeSet changeSet) : EventArgs
{
    public FolderProjectReconciliation Changes { get; } = changes;
    public IReadOnlyList<PackFile> AddedFiles { get; } = addedFiles;
    public IReadOnlyList<PackFile> RemovedFiles { get; } = removedFiles;
    public IReadOnlyList<PackFile> UpdatedFiles { get; } = updatedFiles;
    public bool DirectoriesChanged { get; } = directoriesChanged;
    public FolderProjectChangeSet ChangeSet { get; } = changeSet;
}

public sealed class FolderProjectContainer :
    PackFileContainer,
    IDisposable
{
    private readonly object _stateGate = new();
    private readonly object _mutationGate = new();
    private readonly object _watcherReconciliationGate = new();
    private SynchronizationContext? _synchronizationContext =
        SynchronizationContext.Current;
    private Dictionary<string, FileFingerprint> _fingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PackFile, string> _pathsByFile =
        new(ReferenceEqualityComparer.Instance);
    private FileSystemWatcherWrapper? _watcher;
    private Timer? _debounceTimer;
    private bool _watcherSettingsDirty;
    private long _revision;
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

    internal Action<string, string> CommitPreparedFile
    {
        get;
        set;
    } = File.Move;

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

    internal long NextRevision() => Interlocked.Increment(ref _revision);

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
        return ExecuteSerializedMutation(
            () =>
            {
                ExecuteSynchronized(
                    VirtualizeMaterializedCorruptionDetectionFiles);

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

                return ExecuteSynchronized(
                    () =>
                    {
                        var previousEmptyDirectories =
                            new HashSet<string>(
                                EmptyDirectories,
                                StringComparer.OrdinalIgnoreCase);
                        var removedEntries = FileList
                            .Where(
                                pair =>
                                    !scannedFiles.ContainsKey(pair.Key))
                            .ToList();
                        var removedFiles = removedEntries
                            .Select(pair => pair.Value)
                            .ToList();
                        var addedFiles = new List<PackFile>();
                        var updatedFiles = new List<PackFile>();
                        var fileChanges = removedEntries
                            .Select(pair => new FolderProjectFileChange(
                                pair.Key,
                                FolderProjectFileChangeKind.Removed,
                                pair.Value))
                            .ToList();
                        foreach (var path in scannedFiles.Keys.ToList())
                        {
                            var scannedFile = scannedFiles[path];
                            if (!FileList.TryGetValue(
                                    path,
                                    out var existingFile))
                            {
                                addedFiles.Add(scannedFile);
                                fileChanges.Add(
                                    new FolderProjectFileChange(
                                        path,
                                        FolderProjectFileChangeKind.Added,
                                        scannedFile));
                                continue;
                            }

                            var fingerprintChanged =
                                !_fingerprints.TryGetValue(
                                    path,
                                    out var previousFingerprint) ||
                                previousFingerprint !=
                                scannedFingerprints[path];
                            var nameChanged = !string.Equals(
                                existingFile.Name,
                                scannedFile.Name,
                                StringComparison.Ordinal);
                            if (fingerprintChanged || nameChanged)
                            {
                                existingFile.Name = scannedFile.Name;
                                existingFile.DataSource =
                                    scannedFile.DataSource;
                                updatedFiles.Add(existingFile);
                                fileChanges.Add(
                                    new FolderProjectFileChange(
                                        path,
                                        FolderProjectFileChangeKind.Updated,
                                        existingFile));
                            }

                            scannedFiles[path] = existingFile;
                        }

                        foreach (var removedPath in FileList.Keys
                                     .Where(
                                         path =>
                                             !scannedFiles.ContainsKey(path))
                                     .ToList())
                        {
                            FileList.Remove(removedPath);
                        }
                        foreach (var (path, file) in scannedFiles)
                            FileList[path] = file;

                        _pathsByFile.Clear();
                        foreach (var (path, file) in scannedFiles)
                            _pathsByFile[file] = path;

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
                        var directoryChanges = previousEmptyDirectories
                            .Except(scannedDirectories)
                            .Select(
                                path => new FolderProjectDirectoryChange(
                                    path,
                                    FolderProjectDirectoryChangeKind
                                        .Removed))
                            .Concat(
                                scannedDirectories
                                    .Except(previousEmptyDirectories)
                                    .Select(
                                        path =>
                                            new FolderProjectDirectoryChange(
                                                path,
                                                FolderProjectDirectoryChangeKind
                                                    .Added)))
                            .ToList();
                        return new FolderProjectReconciledEventArgs(
                            changes,
                            addedFiles,
                            removedFiles,
                            updatedFiles,
                            !previousEmptyDirectories.SetEquals(
                                scannedDirectories),
                            new FolderProjectChangeSet(
                                ++_revision,
                                fileChanges,
                                directoryChanges));
                    });
            });
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

            _synchronizationContext ??= SynchronizationContext.Current;
            _debounceTimer = new Timer(
                OnDebounceElapsed,
                null,
                Timeout.Infinite,
                Timeout.Infinite);
            _watcher = new FileSystemWatcherWrapper(ProjectRoot);
            _watcher.ProjectChanged += OnProjectChanged;
            _watcher.Start();
            _debounceTimer.Change(300, Timeout.Infinite);
        }
    }

    public void SetIgnored(string relativePath, bool ignored)
    {
        ExecuteSynchronizedMutation(
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
        ExecuteSynchronizedMutation(
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
        ExecuteSynchronizedMutation(
            () => ProjectSettings.Save(ProjectRoot));
    }

    public void CreateDirectoryOnDisk(string relativePath)
    {
        ExecuteSynchronizedMutation(
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
        return ExecuteSerializedMutation(
            () =>
            {
                var writes = new List<Shared.Core.PackFiles.PackFileWrite>();
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
                    if (File.Exists(fullPath) && !overwriteExisting)
                    {
                        throw new IOException(
                            $"The destination already exists: {fullPath}");
                    }
                    writes.Add(new Shared.Core.PackFiles.PackFileWrite(
                        relativePath,
                        entry.PackFile.DataSource.ReadData()));
                }

                return ApplyFileWritesCore(writes);
            });
    }

    public List<PackFile> ApplyFileWrites(
        IReadOnlyCollection<Shared.Core.PackFiles.PackFileWrite> writes)
    {
        return ExecuteSerializedMutation(
            () => ApplyFileWritesCore(writes));
    }

    private List<PackFile> ApplyFileWritesCore(
        IReadOnlyCollection<Shared.Core.PackFiles.PackFileWrite> writes)
    {
        var prepared = new List<PreparedFileWrite>();
        var uniquePaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var createdDirectories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var write in writes)
            {
                var relativePath = FolderProjectPathPolicy
                    .EnsureResourcePath(write.Path);
                if (!uniquePaths.Add(relativePath))
                {
                    throw new InvalidDataException(
                        $"The batch contains duplicate path: {relativePath}");
                }

                var fullPath = FolderProjectPathPolicy.ResolveFilePath(
                    ProjectRoot,
                    relativePath);
                var directoryPath = Path.GetDirectoryName(fullPath)!;
                if (createdDirectories.Add(directoryPath))
                    Directory.CreateDirectory(directoryPath);
                var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
                prepared.Add(new PreparedFileWrite(
                    relativePath,
                    fullPath,
                    tempPath,
                    write.Content,
                    File.Exists(fullPath)));
            }

            try
            {
                Parallel.ForEach(
                    prepared,
                    CreateParallelFileOptions(),
                    write => File.WriteAllBytes(
                        write.TempPath,
                        write.Content));
            }
            catch (AggregateException exception)
            {
                RethrowSingleParallelFailure(exception);
                throw;
            }

            var committed = new List<CommittedFileWrite>();
            try
            {
                var newCommitted =
                    new ConcurrentBag<CommittedFileWrite>();
                try
                {
                    Parallel.ForEach(
                        prepared.Where(
                            write => !write.DestinationExisted),
                        CreateParallelFileOptions(),
                        write =>
                        {
                            CommitPreparedFile(
                                write.TempPath,
                                write.FullPath);
                            newCommitted.Add(new CommittedFileWrite(
                                write.FullPath,
                                null));
                        });
                }
                finally
                {
                    committed.AddRange(newCommitted);
                }

                foreach (var write in prepared.Where(
                             write => write.DestinationExisted))
                {
                    string? backupPath = null;
                    var restoreCurrent = false;
                    try
                    {
                        if (File.Exists(write.FullPath))
                        {
                            var candidateBackupPath =
                                $"{write.FullPath}.{Guid.NewGuid():N}.tmp";
                            File.Move(
                                write.FullPath,
                                candidateBackupPath);
                            backupPath = candidateBackupPath;
                            restoreCurrent = true;
                        }

                        restoreCurrent = true;
                        CommitPreparedFile(
                            write.TempPath,
                            write.FullPath);
                        committed.Add(new CommittedFileWrite(
                            write.FullPath,
                            backupPath));
                    }
                    catch
                    {
                        if (restoreCurrent)
                        {
                            RestoreCommittedFile(
                                write.FullPath,
                                backupPath);
                        }
                        throw;
                    }
                }
            }
            catch (Exception failure)
            {
                for (var index = committed.Count - 1;
                     index >= 0;
                     index--)
                {
                    RestoreCommittedFile(
                        committed[index].FullPath,
                        committed[index].BackupPath);
                }

                if (failure is AggregateException parallelFailure)
                    RethrowSingleParallelFailure(parallelFailure);
                throw;
            }

            foreach (var write in committed)
            {
                if (write.BackupPath == null)
                    continue;
                try
                {
                    File.Delete(write.BackupPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            var materialized = new List<MaterializedFileWrite>();
            foreach (var write in prepared)
            {
                var packFile = PackFile.CreateFromFileSystem(
                    Path.GetFileName(write.FullPath),
                    write.FullPath);
                var fileInfo = new FileInfo(write.FullPath);
                materialized.Add(new MaterializedFileWrite(
                    write.RelativePath.ToLowerInvariant(),
                    packFile,
                    new FileFingerprint(
                        fileInfo.Length,
                        fileInfo.LastWriteTimeUtc.Ticks)));
            }

            return ExecuteSynchronized(
                () =>
                {
                    var addedFiles = new List<PackFile>();
                    foreach (var write in materialized)
                    {
                        if (FileList.TryGetValue(
                                write.RelativePath,
                                out var replaced))
                        {
                            _pathsByFile.Remove(replaced);
                        }
                        FileList[write.RelativePath] = write.PackFile;
                        _pathsByFile[write.PackFile] = write.RelativePath;
                        _fingerprints[write.RelativePath] = write.Fingerprint;
                        MarkPathHasContent(write.RelativePath);
                        addedFiles.Add(write.PackFile);
                    }

                    SaveEmptyDirectorySettings();
                    return addedFiles;
                });
        }
        finally
        {
            foreach (var write in prepared)
                File.Delete(write.TempPath);
        }
    }

    public void SaveFileData(PackFile file, byte[] data)
    {
        ExecuteSynchronizedMutation(
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
        return ExecuteSynchronizedMutation(
            () =>
            {
                var key = FindKey(file);
                var fullPath = FolderProjectPathPolicy.ResolveFilePath(
                    ProjectRoot,
                    key);
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
                FileList.Remove(key);
                _pathsByFile.Remove(file);
                _fingerprints.Remove(key);
                RemoveIgnoredPaths(key);
                MarkParentEmptyIfNeeded(key);
                SaveEmptyDirectorySettings();
                return file;
            });
    }

    public IReadOnlyList<PackFile> DeleteFolderFromDisk(string folder)
    {
        return ExecuteSynchronizedMutation(
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
                    _pathsByFile.Remove(FileList[key]);
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
        return ExecuteSynchronizedMutation(
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
        return ExecuteSynchronizedMutation(
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
        return ExecuteSynchronizedMutation(
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
                    _pathsByFile[file] = newKey;
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
        lock (_mutationGate)
        {
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
                _pathsByFile.Clear();
                _fingerprints.Clear();
                EmptyDirectories.Clear();
                _pendingWatcherReconciliation = null;
            }
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
                       StringComparison.OrdinalIgnoreCase)) ||
               ProjectSettings.IgnoredPaths.Any(
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
            lock (_mutationGate)
            {
                lock (_stateGate)
                {
                    if (_disposed)
                        return;

                    ApplyWatcherPathChange(e);
                    PersistWatcherSettings();
                    if (RequiresWatcherReconciliation(e))
                        _debounceTimer?.Change(300, Timeout.Infinite);
                }
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
        lock (_stateGate)
        {
            if (_disposed)
                return;
        }

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
        SynchronizationContext? synchronizationContext;
        lock (_stateGate)
        {
            pending = _pendingWatcherReconciliation;
            _pendingWatcherReconciliation = null;
            synchronizationContext = _synchronizationContext;
        }

        if (pending == null)
            return;

        if (synchronizationContext != null &&
            !ReferenceEquals(
                SynchronizationContext.Current,
                synchronizationContext))
        {
            synchronizationContext.Post(
                _ => FilesReconciled?.Invoke(this, pending),
                null);
            return;
        }

        FilesReconciled?.Invoke(this, pending);
    }

    private bool RequiresWatcherReconciliation(
        FolderProjectFileSystemChangedEventArgs change)
    {
        if (change.ChangeType == WatcherChangeTypes.Renamed)
        {
            var isDirectory = Directory.Exists(change.FullPath);
            var oldIsResource = TryGetProjectRelativePath(
                change.OldFullPath,
                out var oldPath,
                isDirectory);
            var newIsResource = TryGetProjectRelativePath(
                change.FullPath,
                out var newPath,
                isDirectory);
            if (!oldIsResource && !newIsResource)
                return false;
            if (!isDirectory &&
                oldIsResource &&
                MatchesKnownFingerprint(
                    oldPath,
                    change.OldFullPath))
            {
                return false;
            }
            return isDirectory ||
                   !newIsResource ||
                   !MatchesKnownFingerprint(newPath, change.FullPath);
        }

        if (change.ChangeType == WatcherChangeTypes.Deleted)
        {
            if (TryGetProjectRelativePath(
                    change.FullPath,
                    out var deletedPath))
            {
                return !MatchesKnownFingerprint(
                    deletedPath,
                    change.FullPath);
            }
            return TryGetDeletedResourceDirectoryPath(
                change.FullPath,
                out _);
        }

        var changedPathIsDirectory = Directory.Exists(change.FullPath);
        if (!TryGetProjectRelativePath(
                change.FullPath,
                out var changedPath,
                changedPathIsDirectory))
        {
            return false;
        }

        if (changedPathIsDirectory)
            return change.ChangeType != WatcherChangeTypes.Changed;

        return !MatchesKnownFingerprint(
            changedPath,
            change.FullPath);
    }

    private bool MatchesKnownFingerprint(
        string relativePath,
        string? fullPath)
    {
        if (fullPath == null ||
            !_fingerprints.TryGetValue(
                relativePath.ToLowerInvariant(),
                out var fingerprint) ||
            !File.Exists(fullPath))
        {
            return false;
        }

        var fileInfo = new FileInfo(fullPath);
        return fingerprint == new FileFingerprint(
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc.Ticks);
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

    internal void ExecuteSerializedMutation(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_mutationGate)
        {
            ExecuteSynchronized(ThrowIfDisposed);
            action();
        }
    }

    internal T ExecuteSerializedMutation<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_mutationGate)
        {
            ExecuteSynchronized(ThrowIfDisposed);
            return action();
        }
    }

    private void ExecuteSynchronizedMutation(Action action)
    {
        ExecuteSerializedMutation(
            () => ExecuteSynchronized(action));
    }

    private T ExecuteSynchronizedMutation<T>(Func<T> action)
    {
        return ExecuteSerializedMutation(
            () => ExecuteSynchronized(action));
    }

    public string GetRelativePath(PackFile file)
    {
        return ExecuteSynchronized(() => FindKey(file));
    }

    private string FindKey(PackFile file)
    {
        if (!_pathsByFile.TryGetValue(file, out var key))
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
        var normalizedNewKey = newKey.ToLowerInvariant();
        FileList[normalizedNewKey] = file;
        _pathsByFile[file] = normalizedNewKey;
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

    private static void RestoreCommittedFile(
        string fullPath,
        string? backupPath)
    {
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        if (backupPath != null && File.Exists(backupPath))
            File.Move(backupPath, fullPath);
    }

    private static ParallelOptions CreateParallelFileOptions()
    {
        return new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
        };
    }

    private static void RethrowSingleParallelFailure(
        AggregateException failure)
    {
        var flattened = failure.Flatten();
        if (flattened.InnerExceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(
                flattened.InnerExceptions[0]).Throw();
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

    private sealed record PreparedFileWrite(
        string RelativePath,
        string FullPath,
        string TempPath,
        byte[] Content,
        bool DestinationExisted);

    private sealed record MaterializedFileWrite(
        string RelativePath,
        PackFile PackFile,
        FileFingerprint Fingerprint);

    private sealed record CommittedFileWrite(
        string FullPath,
        string? BackupPath);
}
