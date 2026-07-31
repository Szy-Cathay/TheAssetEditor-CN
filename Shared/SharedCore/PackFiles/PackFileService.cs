using System.Windows;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Shared.Core.PackFiles
{
    class PackFileService : IPackFileService
    {
        private static readonly object s_saveGate = new();
        private readonly ILogger _logger = Logging.Create<PackFileService>();
        private readonly IGlobalEventHub? _globalEventHub;

        private readonly List<PackFileContainer> _packFileContainers = [];
        private PackFileContainer? _packFileContainerSelectedForEdit;

        // We use this instead of the standard dialog helper, to avaid a circular dependency
        public ISimpleMessageBox MessageBoxProvider { get; set; } = new SimpleMessageBox();
        public bool EnableFileLookUpEvents { get; set; } = false;
        public bool EnforceGameFilesMustBeLoaded { get; set; } = true;

        // Injected via DI after construction
        public ApplicationSettingsService? SettingsService { get; set; }

        public PackFileService(IGlobalEventHub? globalEventHub)
        {
            _globalEventHub = globalEventHub;
        }

        public List<PackFileContainer> GetAllPackfileContainers() => _packFileContainers.ToList(); // Return a list of the list to avoid bugs!

        public PackFileContainer? AddContainer(PackFileContainer container, bool setToMainPackIfFirst = false)
        {
            return AddContainer(
                container,
                _packFileContainers.Count,
                setToMainPackIfFirst,
                PackFileContainerAddedReason.UserOpen);
        }

        public PackFileContainer? AddContainer(
            PackFileContainer container,
            int insertionIndex,
            bool setEditablePack,
            PackFileContainerAddedReason reason)
        {
            if (EnforceGameFilesMustBeLoaded)
            {
                var caPacksLoaded = _packFileContainers.Count(x => x.IsCaPackFile);
                if (caPacksLoaded == 0 &&
                    container.IsCaPackFile == false &&
                    container is not FolderProjectContainer)
                {
                    MessageBoxProvider.ShowDialogBox(LocalizationManager.Instance.Get("Msg.LoadBeforeCaPackfile"), LocalizationManager.Instance.Get("Msg.GeneralError"));
                    return null;
                }
            }

            // Check if already added!
            foreach (var packFile in _packFileContainers)
            {
                if (string.Equals(
                        packFile.SystemFilePath,
                        container.SystemFilePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    MessageBoxProvider.ShowDialogBox(LocalizationManager.Instance.GetFormat("Msg.PackFileAlreadyLoaded", packFile.SystemFilePath), LocalizationManager.Instance.Get("Msg.GeneralError"));
                    return null;
                }
            }

            var previousEditable = GetEditablePack();
            try
            {
                AddContainerInternal(
                    container,
                    insertionIndex,
                    setEditablePack,
                    reason);
                if (container is FolderProjectContainer folderProject)
                {
                    folderProject.FilesReconciled += OnFolderProjectReconciled;
                    folderProject.StartWatching();
                }
                return container;
            }
            catch
            {
                RollbackFailedAdd(container, previousEditable);
                throw;
            }
        }

        private void RollbackFailedAdd(
            PackFileContainer container,
            PackFileContainer? previousEditable)
        {
            var removed = _packFileContainers.Remove(container);
            bool editableChanged;
            lock (s_saveGate)
            {
                editableChanged = !ReferenceEquals(
                    _packFileContainerSelectedForEdit,
                    previousEditable);
                _packFileContainerSelectedForEdit = previousEditable;
            }

            if (editableChanged)
            {
                try
                {
                    _globalEventHub?.PublishGlobalEvent(
                        new PackFileContainerSetAsMainEditableEvent(
                            previousEditable));
                }
                catch
                {
                    // Preserve the original add failure.
                }
            }

            if (removed)
            {
                try
                {
                    _globalEventHub?.PublishGlobalEvent(
                        new PackFileContainerRemovedEvent(container));
                }
                catch
                {
                    // Preserve the original add failure.
                }
            }

            if (container is FolderProjectContainer folderProject)
            {
                folderProject.FilesReconciled -=
                    OnFolderProjectReconciled;
                try
                {
                    folderProject.Dispose();
                }
                catch
                {
                    // Preserve the original add failure.
                }
            }
        }

        void AddContainerInternal(
            PackFileContainer container,
            int insertionIndex,
            bool setEditablePack,
            PackFileContainerAddedReason reason)
        {
            var index = Math.Clamp(
                insertionIndex,
                0,
                _packFileContainers.Count);
            _packFileContainers.Insert(index, container);
            _globalEventHub?.PublishGlobalEvent(
                new PackFileContainerAddedEvent(container, reason));

            var notCaPacksLoaded = _packFileContainers.Count(x => !x.IsCaPackFile);
            if (container.IsCaPackFile == false && setEditablePack)
                SetEditablePack(container);
        }

        public PackFileContainer CreateNewPackFileContainer(string name, PackFileVersion packFileVersion, PackFileCAType type, bool setEditablePack = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new Exception("Name can not be empty");

            var versionString = PackFileVersionConverter.ToString(packFileVersion);
            var newPackFile = new PackFileContainer(name)
            {
                Header = new PFHeader(versionString, type),
            };

            AddContainerInternal(
                newPackFile,
                _packFileContainers.Count,
                setEditablePack,
                PackFileContainerAddedReason.UserOpen);

            return newPackFile;
        }

        public void AddFilesToPack(
            PackFileContainer container,
            List<NewPackFileEntry> newFiles,
            bool overwriteExisting = true)
        {
            if (container.IsCaPackFile)
                throw new Exception("Can not add files to ca pack file");

            if (container is FolderProjectContainer folderProject)
            {
                var addedFiles = folderProject.AddFiles(
                    newFiles,
                    overwriteExisting);
                _globalEventHub?.PublishGlobalEvent(
                    new PackFileContainerFilesAddedEvent(
                        container,
                        addedFiles));
                return;
            }

            foreach (var file in newFiles)
            {
                if (string.IsNullOrWhiteSpace(file.PackFile.Name))
                    throw new Exception("PackFile name can not be empty");
            }

            foreach (var file in newFiles)
            {
                file.PackFile.Name = file.PackFile.Name.Trim();

                var path = file.DirectoyPath.Trim();
                if (!string.IsNullOrWhiteSpace(path))
                    path += "\\";
                path += file.PackFile.Name;
                container.FileList[path.ToLower()] = file.PackFile;
            }

            var files = newFiles.Select(x => x.PackFile).ToList();
            _globalEventHub?.PublishGlobalEvent(new PackFileContainerFilesAddedEvent(container, files));
        }

        public void CopyFileFromOtherPackFile(PackFileContainer source, string path, PackFileContainer target)
        {
            var lowerPath = path.Replace('/', '\\').ToLower().Trim();
            var newFile = source is FolderProjectContainer sourceProject
                ? sourceProject.ExecuteSynchronized(
                    () => CloneFile(source, lowerPath))
                : CloneFile(source, lowerPath);
            if (newFile != null)
            {
                if (target is FolderProjectContainer folderProject)
                {
                    var added = folderProject.AddFiles(
                    [
                        new NewPackFileEntry(
                            Path.GetDirectoryName(lowerPath) ?? "",
                            newFile),
                    ]);
                    _globalEventHub?.PublishGlobalEvent(
                        new PackFileContainerFilesAddedEvent(
                            target,
                            added));
                    return;
                }

                target.FileList[lowerPath] = newFile;

                _globalEventHub?.PublishGlobalEvent(new PackFileContainerFilesAddedEvent(target, [newFile]));
            }
        }

        private static PackFile? CloneFile(
            PackFileContainer source,
            string path)
        {
            if (!source.FileList.TryGetValue(path, out var file))
                return null;

            return new PackFile(
                file.Name,
                new MemorySource(file.DataSource.ReadData()));
        }

        public void CreateFolder(
            PackFileContainer container,
            string folder)
        {
            if (container.IsCaPackFile)
                throw new Exception("Can not create folders inside CA pack file");

            if (container is FolderProjectContainer folderProject)
                folderProject.CreateDirectoryOnDisk(folder);
        }

        public void SetEditablePack(PackFileContainer? pf)
        {
            if (pf != null && pf.IsCaPackFile)
                throw new Exception("Trying to set CA packfile container to be editable - this is not legal!");

            lock (s_saveGate)
                _packFileContainerSelectedForEdit = pf;

            _globalEventHub?.PublishGlobalEvent(new PackFileContainerSetAsMainEditableEvent(pf));
        }

        public PackFileContainer? GetEditablePack() => _packFileContainerSelectedForEdit;

        public void UnloadPackContainer(PackFileContainer pf)
        {
            TryUnloadPackContainer(pf);
        }

        public bool TryUnloadPackContainer(PackFileContainer pf)
        {
            if (!_packFileContainers.Contains(pf))
                return false;

            var e = new BeforePackFileContainerRemovedEvent(pf);
            _globalEventHub?.PublishGlobalEvent(e);

            if (!e.AllowClose ||
                !e.ExecuteApprovedCloseAction())
            {
                return false;
            }

            if (!_packFileContainers.Remove(pf))
                return false;
            bool wasEditable;
            lock (s_saveGate)
            {
                wasEditable = ReferenceEquals(
                    _packFileContainerSelectedForEdit,
                    pf);
                if (wasEditable)
                    _packFileContainerSelectedForEdit = null;
            }

            try
            {
                if (wasEditable)
                {
                    _globalEventHub?.PublishGlobalEvent(
                        new PackFileContainerSetAsMainEditableEvent(null));
                }
            }
            finally
            {
                try
                {
                    _globalEventHub?.PublishGlobalEvent(
                        new PackFileContainerRemovedEvent(pf));
                }
                finally
                {
                    if (pf is FolderProjectContainer folderProject)
                    {
                        folderProject.FilesReconciled -=
                            OnFolderProjectReconciled;
                        folderProject.Dispose();
                    }
                }
            }
            return true;
        }

        public void DeleteFolder(PackFileContainer pf, string folder)
        {
            if (pf.IsCaPackFile)
                throw new Exception("Can not delete folder inside CA pack file");

            if (pf is FolderProjectContainer folderProject)
            {
                folderProject.DeleteFolderFromDisk(folder);
                _globalEventHub?.PublishGlobalEvent(
                    new PackFileContainerFolderRemovedEvent(pf, folder));
                return;
            }

            var filesToDelete = new List<string>();
            foreach (var file in pf.FileList)
            { 
                var directory = Path.GetDirectoryName(file.Key);
                if (directory == null)
                    continue;

                if (directory.StartsWith(folder, StringComparison.InvariantCultureIgnoreCase))
                    filesToDelete.Add(file.Key);
            }

            _globalEventHub?.PublishGlobalEvent(new PackFileContainerFolderRemovedEvent(pf, folder));

            foreach (var item in filesToDelete)
            {
                _logger.Here().Information($"Deleting file {item} in directory {folder}");
                pf.FileList.Remove(item);
            }
        }

        public void DeleteFile(PackFileContainer pf, PackFile file)
        {
            if (pf.IsCaPackFile)
                throw new Exception("Can not delete files inside CA pack file");

            if (pf is FolderProjectContainer folderProject)
            {
                folderProject.DeleteFileFromDisk(file);
                _globalEventHub?.PublishGlobalEvent(
                    new PackFileContainerFilesRemovedEvent(pf, [file]));
                return;
            }

            var key = pf.FileList.FirstOrDefault(x => x.Value == file).Key;
            _logger.Here().Information($"Deleting file {key}");

            _globalEventHub?.PublishGlobalEvent(new PackFileContainerFilesRemovedEvent(pf, [file]));
            pf.FileList.Remove(key);
        }

        public void MoveFile(PackFileContainer pf, PackFile file, string newFolderPath)
        {
            if (pf.IsCaPackFile)
                throw new Exception("Can not move files inside CA pack file");

            if (pf is FolderProjectContainer folderProject)
            {
                folderProject.MoveFileOnDisk(file, newFolderPath);
                _globalEventHub?.PublishGlobalEvent(
                    new PackFileContainerFilesRemovedEvent(pf, [file]));
                _globalEventHub?.PublishGlobalEvent(
                    new PackFileContainerFilesAddedEvent(pf, [file]));
                return;
            }

            var newFullPath = newFolderPath + "\\" + file.Name;

            var key = pf.FileList.FirstOrDefault(x => x.Value == file).Key;
            _globalEventHub?.PublishGlobalEvent(new PackFileContainerFilesRemovedEvent(pf, [file]));

            pf.FileList.Remove(key);
            pf.FileList[newFullPath] = file;

            _logger.Here().Information($"Moving file {key}");

            _globalEventHub?.PublishGlobalEvent(new PackFileContainerFilesAddedEvent(pf, [file]));
        }

        public void RenameDirectory(PackFileContainer pf, string currentNodeName, string newName)
        {
            if (pf.IsCaPackFile)
                throw new Exception("Can not rename in ca pack file");

            if (string.IsNullOrWhiteSpace(newName))
                throw new Exception("Name can not be empty");

            if (pf is FolderProjectContainer folderProject)
            {
                var projectNewNodePath =
                    folderProject.RenameDirectoryOnDisk(
                        currentNodeName,
                        newName);
                _globalEventHub?.PublishGlobalEvent(
                    new PackFileContainerFolderRenamedEvent(
                        pf,
                        projectNewNodePath));
                return;
            }

            var oldNodePath = currentNodeName;
            var newNodePath = currentNodeName;

            var files = pf.FileList.Where(x => x.Key.StartsWith(oldNodePath)).ToList();
            foreach (var (path, file) in files)
            {
                pf.FileList.Remove(path);
                var newPath = newNodePath;
                if (oldNodePath.Length != 0)
                    newPath = path.Replace(oldNodePath, newNodePath);

                pf.FileList[newPath] = file;
            }

            _globalEventHub?.PublishGlobalEvent(new PackFileContainerFolderRenamedEvent(pf, newNodePath));
        }

        public void RenameFile(PackFileContainer pf, PackFile file, string newName)
        {
            if (pf.IsCaPackFile)
                throw new Exception("Can not rename file in ca pack file");

            if (string.IsNullOrWhiteSpace(newName))
                throw new Exception("Name can not be empty");

            if (pf is FolderProjectContainer folderProject)
            {
                folderProject.RenameFileOnDisk(file, newName);
                _globalEventHub?.PublishGlobalEvent(
                    new PackFileContainerFilesUpdatedEvent(pf, [file]));
                return;
            }

            var key = pf.FileList.FirstOrDefault(x => x.Value == file).Key;
            pf.FileList.Remove(key);

            var dir = Path.GetDirectoryName(key);
            file.Name = newName;
            pf.FileList[dir + "\\" + file.Name] = file;

            _globalEventHub?.PublishGlobalEvent(new PackFileContainerFilesUpdatedEvent(pf, [file]));
        }

        public void SaveFile(PackFile file, byte[] data)
        {
            var pf = GetEditablePack();
            if (pf == null)
                throw new InvalidOperationException("No editable pack is selected.");
            if (pf.IsCaPackFile)
                throw new Exception("Can not save ca pack file");

            if (pf is FolderProjectContainer folderProject)
                folderProject.SaveFileData(file, data);
            else
                file.DataSource = new MemorySource(data);

            _globalEventHub?.PublishGlobalEvent(new PackFileContainerFilesUpdatedEvent(pf, [file]));
            _globalEventHub?.PublishGlobalEvent(new PackFileSavedEvent(file));
        }

        public void SavePackContainer(PackFileContainer pf, string path, bool createBackup, GameInformation gameInformation)
        {
            lock (s_saveGate)
            {
                if (pf is FolderProjectContainer folderProject)
                {
                    SaveFolderProjectContainerCore(
                        folderProject,
                        path,
                        createBackup,
                        gameInformation);
                }
                else
                    SavePackContainerCore(pf, path, createBackup, gameInformation);
            }

            _globalEventHub?.PublishGlobalEvent(new PackFileContainerSavedEvent(pf));
        }

        public bool TryAutoSavePackContainer(PackFileContainer pf, string expectedPath, GameInformation gameInformation)
        {
            lock (s_saveGate)
            {
                var currentPath =
                    pf is FolderProjectContainer folderProject
                        ? folderProject.ExecuteSynchronized(
                            () => folderProject.ProjectSettings
                                .OutputPackPath)
                        : pf.SystemFilePath;
                if (!ReferenceEquals(_packFileContainerSelectedForEdit, pf) ||
                    !string.Equals(
                        currentPath,
                        expectedPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (pf is FolderProjectContainer project)
                {
                    SaveFolderProjectContainerCore(
                        project,
                        expectedPath,
                        false,
                        gameInformation);
                }
                else
                    SavePackContainerCore(pf, expectedPath, false, gameInformation);
            }

            _globalEventHub?.PublishGlobalEvent(new PackFileContainerSavedEvent(pf));
            return true;
        }

        private void SaveFolderProjectContainerCore(
            FolderProjectContainer project,
            string path,
            bool createBackup,
            GameInformation gameInformation)
        {
            var outputPath = Path.GetFullPath(path);
            var loadedOutput = _packFileContainers.FirstOrDefault(
                container =>
                    !ReferenceEquals(container, project) &&
                    !string.IsNullOrWhiteSpace(
                        container.SystemFilePath) &&
                    string.Equals(
                        Path.GetFullPath(container.SystemFilePath),
                        outputPath,
                        StringComparison.OrdinalIgnoreCase));
            if (loadedOutput != null)
            {
                throw new InvalidOperationException(
                    LocalizationManager.Instance.GetFormat(
                        "Msg.PackFileAlreadyLoaded",
                        outputPath));
            }

            project.ExecuteSynchronized(
                () =>
                {
                    FolderProjectPathPolicy.EnsureOutputOutsideProject(
                        project.ProjectRoot,
                        path);

                    var transient =
                        new PackFileContainer(project.Name)
                        {
                            Header = new PFHeader(
                                PackFileVersionConverter.ToString(
                                    project.ProjectSettings
                                        .PackFileVersion),
                                project.ProjectSettings.PackFileType)
                            {
                                DependantFiles =
                                    project.Header.DependantFiles
                                        .ToList(),
                            },
                            SystemFilePath = path,
                        };

                    foreach (var (relativePath, file)
                             in project.FileList)
                    {
                        if (FolderProjectPathPolicy.IsExcludedPath(
                                relativePath) ||
                            project.IsIgnored(relativePath))
                        {
                            continue;
                        }

                        transient.FileList[relativePath] =
                            new PackFile(
                                file.Name,
                                file.DataSource);
                    }

                    if (project.ProjectSettings.GameVersion
                        is { } gameVersion)
                    {
                        gameInformation =
                            GameInformationDatabase.GetGameById(
                                gameVersion);
                    }

                    SavePackContainerCore(
                        transient,
                        path,
                        createBackup,
                        gameInformation,
                        project.ProjectSettings
                            .EnablePackFileCorruptionDetection);

                    project.ProjectSettings.OutputPackPath =
                        Path.GetFullPath(path);
                    project.SaveSettings();
                });
        }

        private void SavePackContainerCore(
            PackFileContainer pf,
            string path,
            bool createBackup,
            GameInformation gameInformation,
            bool enableCorruptionDetection = false)
        {
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                if (File.Exists(path) && DirectoryHelper.IsFileLocked(path))
                {
                    throw new IOException($"Cannot access {path} because another process has locked it, most likely the game.");
                }

                if (pf.IsCaPackFile)
                    throw new Exception("Can not save ca pack file");
                if (createBackup)
                    SaveUtility.CreateFileBackup(path);

                // Check if file has changed in size
                if (pf.OriginalLoadByteSize != -1)
                {
                    var fileInfo = new FileInfo(pf.SystemFilePath);
                    var byteSize = fileInfo.Length;
                    if (byteSize != pf.OriginalLoadByteSize)
                        throw new Exception("File has been changed outside of AssetEditor. Can not save the file as it will cause corruptions");
                }

                // Capture parent references before serialization so their streams can be
                // closed before replacing the destination file.
                var oldParents = pf.FileList.Values
                    .Where(f => f.DataSource is PackedFileSource)
                    .Select(f => ((PackedFileSource)f.DataSource).Parent)
                    .Distinct()
                    .ToList();

                PackFileSerializationResult serializationResult;
                using (var memoryStream = new FileStream(tempPath, FileMode.CreateNew))
                {
                    using var writer = new BinaryWriter(memoryStream);
                    var useCompression = SettingsService?.CurrentSettings.UseZstdCompression ?? true;
                    _logger.Here().Information($"Saving pack with compression={useCompression}");
                    serializationResult = PackFileSerializerWriter.SaveToByteArray(
                        path,
                        pf,
                        writer,
                        gameInformation,
                        useCompression,
                        enableCorruptionDetection);
                }

                foreach (var parent in oldParents)
                    parent.CloseStream();

                // Auto-backup the original file before overwriting (only for non-CA packs with existing file)
                if (!pf.IsCaPackFile && File.Exists(path))
                {
                    try
                    {
                        var settings = SettingsService?.CurrentSettings;
                        var backupDir = settings?.BackupPath ?? "";
                        var maxCount = settings?.MaxBackupCount ?? 10;
                        SaveUtility.CreateBackupWithRotation(path, backupDir, maxCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.Here().Error(ex, "Failed to create backup, proceeding with save");
                    }
                }

                File.Move(tempPath, path, true);

                serializationResult.Commit();
                pf.SystemFilePath = path;
                pf.OriginalLoadByteSize = new FileInfo(path).Length;
            }
            finally
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Cleanup is best effort; preserve the original save exception.
                }
            }
        }

        public PackFileContainer? GetPackFileContainer(PackFile file)
        {
            foreach (var pf in _packFileContainers)
            {
                var containsFile =
                    pf is FolderProjectContainer folderProject
                        ? folderProject.ExecuteSynchronized(
                            () => pf.FileList.Values.Any(
                                value => ReferenceEquals(value, file)))
                        : pf.FileList.Values.Any(
                            value => ReferenceEquals(value, file));
                if (containsFile)
                    return pf;
            }
            _logger.Here().Information($"Unknown packfile container for {file.Name}");
            return null;
        }

        public PackFile? FindFile(string path, PackFileContainer? container = null)
        {
            var lowerPath = path.Replace('/', '\\').ToLower().Trim();

            if (container == null)
            {
                for (var i = _packFileContainers.Count - 1; i >= 0; i--)
                {
                    var currentContainer = _packFileContainers[i];
                    var value =
                        currentContainer
                            is FolderProjectContainer folderProject
                            ? folderProject.ExecuteSynchronized(
                                () => currentContainer.FileList
                                    .GetValueOrDefault(lowerPath))
                            : currentContainer.FileList
                                .GetValueOrDefault(lowerPath);
                    if (value != null)
                    {
                        if (EnableFileLookUpEvents)
                            _globalEventHub?.PublishGlobalEvent(new PackFileLookUpEvent(path, currentContainer, true));
                        return value;
                    }
                }
            }
            else
            {
                var value =
                    container is FolderProjectContainer folderProject
                        ? folderProject.ExecuteSynchronized(
                            () => container.FileList
                                .GetValueOrDefault(lowerPath))
                        : container.FileList.GetValueOrDefault(lowerPath);
                if (value != null)
                {
                    if (EnableFileLookUpEvents)
                        _globalEventHub?.PublishGlobalEvent(new PackFileLookUpEvent(path, container, true));
                    return value;
                }
            }

            if (EnableFileLookUpEvents)
                _globalEventHub?.PublishGlobalEvent(new PackFileLookUpEvent(path, null, false));
            return null;
        }

        public string GetFullPath(PackFile file, PackFileContainer? container = null)
        {
            if (container == null)
            {
                foreach (var pf in _packFileContainers)
                {
                    var res =
                        pf is FolderProjectContainer folderProject
                            ? folderProject.ExecuteSynchronized(
                                () => FindPath(pf, file))
                            : FindPath(pf, file);
                    if (string.IsNullOrWhiteSpace(res) == false)
                        return res;
                }
            }
            else
            {
                var res =
                    container is FolderProjectContainer folderProject
                        ? folderProject.ExecuteSynchronized(
                            () => FindPath(container, file))
                        : FindPath(container, file);
                if (string.IsNullOrWhiteSpace(res) == false)
                    return res;
            }

            throw new Exception("Unknown path for " + file.Name);
        }

        private static string? FindPath(
            PackFileContainer container,
            PackFile file)
        {
            return container.FileList
                .FirstOrDefault(
                    pair => ReferenceEquals(pair.Value, file))
                .Key;
        }

        /// <summary>
        /// Close all PackedFileSourceParent streams for files in a container,
        /// releasing file handles so the pack file can be deleted/overwritten.
        /// </summary>
        private static void ClosePackedFileStreams(PackFileContainer container)
        {
            if (container is FolderProjectContainer folderProject)
            {
                folderProject.ExecuteSynchronized(
                    () => ClosePackedFileStreamsCore(container));
                return;
            }

            ClosePackedFileStreamsCore(container);
        }

        private static void ClosePackedFileStreamsCore(
            PackFileContainer container)
        {
            foreach (var file in container.FileList.Values)
            {
                if (file.DataSource is PackedFileSource packedSource)
                    packedSource.Parent.CloseStream();
            }
        }

        private void OnFolderProjectReconciled(
            object? sender,
            FolderProjectReconciledEventArgs e)
        {
            if (sender is not FolderProjectContainer folderProject)
                return;

            if (e.RemovedFiles.Count != 0)
            {
                _globalEventHub?.PublishGlobalEvent(
                    new PackFileContainerFilesRemovedEvent(
                        folderProject,
                        e.RemovedFiles.ToList()));
            }

            if (e.AddedFiles.Count != 0)
            {
                _globalEventHub?.PublishGlobalEvent(
                    new PackFileContainerFilesAddedEvent(
                        folderProject,
                        e.AddedFiles.ToList()));
            }

            if (e.UpdatedFiles.Count != 0 ||
                e.DirectoriesChanged)
            {
                _globalEventHub?.PublishGlobalEvent(
                    new PackFileContainerFilesUpdatedEvent(
                        folderProject,
                        e.UpdatedFiles.ToList()));
            }
        }
    }

    public record NewPackFileEntry(string DirectoyPath, PackFile PackFile);

    public interface ISimpleMessageBox
    {
        void ShowDialogBox(string message, string title);
    }

    public class SimpleMessageBox : ISimpleMessageBox
    {
        public void ShowDialogBox(string message, string title) => MessageBox.Show(message, title);
    }
}
