using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;

namespace AssetEditor.Services;

public interface IFolderProjectGitOperationCoordinator
{
    T Execute<T>(
        string projectRoot,
        Func<T> detachedOperation,
        bool openWhenComplete = false);

    Task<T> ExecuteAsync<T>(
        string projectRoot,
        Func<T> detachedOperation,
        bool openWhenComplete = false);

    Task<T> ExecuteTransactionalAsync<T>(
        string projectRoot,
        Func<T> detachedOperation,
        Action<T> completeOperation,
        Action<T> rollbackOperation,
        bool openWhenComplete = false);
}

public sealed class FolderProjectGitOperationCanceledException :
    OperationCanceledException
{
    public FolderProjectGitOperationCanceledException()
        : base("The folder project could not be closed.")
    {
    }
}

public sealed class FolderProjectGitHostException : InvalidOperationException
{
    public Exception? OperationException { get; }
    public Exception HostException { get; }

    internal FolderProjectGitHostException(
        string message,
        Exception hostException,
        Exception? operationException = null)
        : base(
            message,
            operationException == null
                ? hostException
                : new AggregateException(
                    operationException,
                    hostException))
    {
        OperationException = operationException;
        HostException = hostException;
    }
}

internal class FolderProjectGitOperationPlatform
{
    public virtual bool DirectoryExists(string path) =>
        Directory.Exists(path);

    public virtual bool FileExists(string path) =>
        File.Exists(path);

    public virtual FileAttributes GetAttributes(string path) =>
        File.GetAttributes(path);

    public virtual IEnumerable<string> EnumerateFileSystemEntries(
        string path) =>
        Directory.EnumerateFileSystemEntries(path);

    public virtual void DeleteDirectory(string path) =>
        Directory.Delete(path, false);

    public virtual void CreateDirectory(string path) =>
        Directory.CreateDirectory(path);
}

public sealed class FolderProjectGitOperationCoordinator :
    IFolderProjectGitOperationCoordinator
{
    private readonly IPackFileService _packFileService;
    private readonly IFolderProjectFactory _folderProjectFactory;
    private readonly IFolderProjectVersionControlService _versionControl;
    private readonly FolderProjectGitOperationPlatform _platform;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _rootGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ReattachState>
        _pendingReattach =
        new(StringComparer.OrdinalIgnoreCase);

    public FolderProjectGitOperationCoordinator(
        IPackFileService packFileService,
        IFolderProjectFactory folderProjectFactory,
        IFolderProjectVersionControlService versionControl)
        : this(
            packFileService,
            folderProjectFactory,
            versionControl,
            new FolderProjectGitOperationPlatform())
    {
    }

    internal FolderProjectGitOperationCoordinator(
        IPackFileService packFileService,
        IFolderProjectFactory folderProjectFactory,
        IFolderProjectVersionControlService versionControl,
        FolderProjectGitOperationPlatform platform)
    {
        _packFileService = packFileService;
        _folderProjectFactory = folderProjectFactory;
        _versionControl = versionControl;
        _platform = platform;
    }

    public T Execute<T>(
        string projectRoot,
        Func<T> detachedOperation,
        bool openWhenComplete = false)
    {
        ArgumentNullException.ThrowIfNull(detachedOperation);
        var normalizedRoot = NormalizeProjectRoot(projectRoot);
        var rootGate = _rootGates.GetOrAdd(
            normalizedRoot,
            _ => new SemaphoreSlim(1, 1));

        rootGate.Wait();
        try
        {
            return ExecuteCore(
                normalizedRoot,
                detachedOperation,
                openWhenComplete);
        }
        finally
        {
            rootGate.Release();
        }
    }

    public async Task<T> ExecuteAsync<T>(
        string projectRoot,
        Func<T> detachedOperation,
        bool openWhenComplete = false)
    {
        ArgumentNullException.ThrowIfNull(detachedOperation);
        var normalizedRoot = NormalizeProjectRoot(projectRoot);
        var rootGate = _rootGates.GetOrAdd(
            normalizedRoot,
            _ => new SemaphoreSlim(1, 1));

        await rootGate.WaitAsync();
        try
        {
            var preparation = PrepareOperation(normalizedRoot);
            T? result = default;
            var operationException = preparation.OperationException;
            if (operationException == null)
            {
                try
                {
                    result = await Task.Run(detachedOperation);
                }
                catch (Exception exception)
                {
                    operationException = exception;
                }
            }

            return await CompleteOperationAsync(
                normalizedRoot,
                preparation.LoadedProject,
                result,
                operationException,
                openWhenComplete);
        }
        finally
        {
            rootGate.Release();
        }
    }

    public async Task<T> ExecuteTransactionalAsync<T>(
        string projectRoot,
        Func<T> detachedOperation,
        Action<T> completeOperation,
        Action<T> rollbackOperation,
        bool openWhenComplete = false)
    {
        ArgumentNullException.ThrowIfNull(detachedOperation);
        ArgumentNullException.ThrowIfNull(completeOperation);
        ArgumentNullException.ThrowIfNull(rollbackOperation);
        var normalizedRoot = NormalizeProjectRoot(projectRoot);
        var rootGate = _rootGates.GetOrAdd(
            normalizedRoot,
            _ => new SemaphoreSlim(1, 1));

        await rootGate.WaitAsync();
        try
        {
            var preparation = PrepareOperation(normalizedRoot);
            if (preparation.OperationException != null)
            {
                return await CompleteOperationAsync<T>(
                    normalizedRoot,
                    preparation.LoadedProject,
                    default,
                    preparation.OperationException,
                    openWhenComplete);
            }

            T result;
            try
            {
                result = await Task.Run(detachedOperation);
            }
            catch (Exception exception)
            {
                return await CompleteOperationAsync<T>(
                    normalizedRoot,
                    preparation.LoadedProject,
                    default,
                    exception,
                    openWhenComplete);
            }

            try
            {
                await ReattachAfterTransactionAsync(
                    normalizedRoot,
                    preparation.LoadedProject,
                    openWhenComplete);
            }
            catch (Exception hostFailure)
            {
                try
                {
                    RemoveAllConfiguredEmptyDirectories(normalizedRoot);
                    await Task.Run(() => rollbackOperation(result));
                    await ReattachAfterTransactionAsync(
                        normalizedRoot,
                        preparation.LoadedProject,
                        openWhenComplete);
                }
                catch (Exception rollbackFailure)
                {
                    throw new FolderProjectGitHostException(
                        "The folder project could not be rolled back after reload failed.",
                        rollbackFailure,
                        hostFailure);
                }

                throw new FolderProjectGitHostException(
                    "The folder project operation was rolled back after reload failed.",
                    hostFailure);
            }

            try
            {
                await Task.Run(() => completeOperation(result));
            }
            catch (Exception completionFailure)
            {
                try
                {
                    var rollbackPreparation = PrepareOperation(normalizedRoot);
                    if (rollbackPreparation.OperationException != null)
                    {
                        ExceptionDispatchInfo.Capture(
                            rollbackPreparation.OperationException).Throw();
                    }
                    RemoveAllConfiguredEmptyDirectories(normalizedRoot);
                    await Task.Run(() => rollbackOperation(result));
                    await ReattachAfterTransactionAsync(
                        normalizedRoot,
                        rollbackPreparation.LoadedProject,
                        openWhenComplete);
                }
                catch (Exception rollbackFailure)
                {
                    throw new FolderProjectGitHostException(
                        "The folder project could not be rolled back after finalization failed.",
                        rollbackFailure,
                        completionFailure);
                }

                throw new FolderProjectGitHostException(
                    "The folder project operation was rolled back after finalization failed.",
                    completionFailure);
            }
            return result;
        }
        finally
        {
            rootGate.Release();
        }
    }

    private async Task ReattachAfterTransactionAsync(
        string projectRoot,
        FolderProjectContainer? loadedProject,
        bool openWhenComplete)
    {
        var reattachState = GetReattachState(
            projectRoot,
            loadedProject,
            null,
            openWhenComplete);
        if (reattachState == null)
            return;

        var project = await Task.Run(() => OpenForReattach(projectRoot));
        Attach(projectRoot, reattachState, project);
    }

    private T ExecuteCore<T>(
        string projectRoot,
        Func<T> detachedOperation,
        bool openWhenComplete)
    {
        var preparation = PrepareOperation(projectRoot);
        T? result = default;
        var operationException = preparation.OperationException;
        if (operationException == null)
        {
            try
            {
                result = detachedOperation();
            }
            catch (Exception exception)
            {
                operationException = exception;
            }
        }

        return CompleteOperation(
            projectRoot,
            preparation.LoadedProject,
            result,
            operationException,
            openWhenComplete);
    }

    private OperationPreparation PrepareOperation(string projectRoot)
    {
        Exception? operationException = null;
        var loadedProject = FindLoadedProject(projectRoot);
        if (loadedProject != null)
        {
            _pendingReattach.TryRemove(projectRoot, out _);
            var containers =
                _packFileService.GetAllPackfileContainers();
            var reattachState = new ReattachState(
                containers.IndexOf(loadedProject),
                ReferenceEquals(
                    _packFileService.GetEditablePack(),
                    loadedProject),
                loadedProject.ProjectSettings.EmptyDirectories.ToArray());

            try
            {
                if (!_packFileService.TryUnloadPackContainer(
                        loadedProject))
                {
                    throw new
                        FolderProjectGitOperationCanceledException();
                }

                _pendingReattach[projectRoot] = reattachState;
                RemoveTemporaryEmptyDirectories(
                    projectRoot,
                    reattachState.EmptyDirectories);
            }
            catch (Exception exception)
            {
                if (!IsLoaded(loadedProject))
                    _pendingReattach[projectRoot] = reattachState;
                operationException = exception;
            }
        }

        if (operationException != null &&
            loadedProject != null &&
            IsLoaded(loadedProject))
        {
            ExceptionDispatchInfo.Capture(operationException).Throw();
        }

        return new OperationPreparation(
            loadedProject,
            operationException);
    }

    private T CompleteOperation<T>(
        string projectRoot,
        FolderProjectContainer? loadedProject,
        T? result,
        Exception? operationException,
        bool openWhenComplete)
    {
        Exception? hostException = null;
        try
        {
            var reattachState = GetReattachState(
                projectRoot,
                loadedProject,
                operationException,
                openWhenComplete);
            if (reattachState != null)
                Reattach(projectRoot, reattachState);
        }
        catch (Exception exception)
        {
            hostException = exception;
        }

        if (hostException != null)
        {
            throw new FolderProjectGitHostException(
                "The folder project could not be reattached after the Git operation.",
                hostException,
                operationException);
        }

        if (operationException != null)
            ExceptionDispatchInfo.Capture(operationException).Throw();

        return result!;
    }

    private async Task<T> CompleteOperationAsync<T>(
        string projectRoot,
        FolderProjectContainer? loadedProject,
        T? result,
        Exception? operationException,
        bool openWhenComplete)
    {
        Exception? hostException = null;
        try
        {
            var reattachState = GetReattachState(
                projectRoot,
                loadedProject,
                operationException,
                openWhenComplete);
            if (reattachState != null)
            {
                var project = await Task.Run(
                    () => OpenForReattach(projectRoot));
                Attach(projectRoot, reattachState, project);
            }
        }
        catch (Exception exception)
        {
            hostException = exception;
        }

        if (hostException != null)
        {
            throw new FolderProjectGitHostException(
                "The folder project could not be reattached after the Git operation.",
                hostException,
                operationException);
        }

        if (operationException != null)
            ExceptionDispatchInfo.Capture(operationException).Throw();

        return result!;
    }

    private ReattachState? GetReattachState(
        string projectRoot,
        FolderProjectContainer? loadedProject,
        Exception? operationException,
        bool openWhenComplete)
    {
        bool shouldReattach;
        try
        {
            shouldReattach = _versionControl
                .GetMergeState(projectRoot)
                .Phase == FolderProjectMergePhase.None;
        }
        catch (FolderProjectVersionControlException exception)
            when (operationException != null &&
                  exception.Code ==
                      FolderProjectVersionControlError
                          .RepositoryNotInitialized)
        {
            shouldReattach = _pendingReattach.ContainsKey(projectRoot);
        }

        if (!shouldReattach)
            return null;
        if (_pendingReattach.TryGetValue(
                projectRoot,
                out var reattachState))
        {
            return reattachState;
        }
        if (!openWhenComplete || loadedProject != null)
            return null;

        return new ReattachState(
            _packFileService.GetAllPackfileContainers().Count,
            false,
            []);
    }

    private void Reattach(
        string projectRoot,
        ReattachState reattachState)
    {
        var project = OpenForReattach(projectRoot);
        Attach(projectRoot, reattachState, project);
    }

    private FolderProjectContainer OpenForReattach(string projectRoot)
    {
        PrepareProjectForOpen(projectRoot);
        return _folderProjectFactory.Open(projectRoot);
    }

    private void Attach(
        string projectRoot,
        ReattachState reattachState,
        FolderProjectContainer project)
    {
        try
        {
            var added = _packFileService.ReattachFolderProject(
                project,
                reattachState.InsertionIndex,
                reattachState.WasEditable
                    ? FolderProjectReattachMode.Editable
                    : FolderProjectReattachMode.Inactive);
            if (!ReferenceEquals(added, project))
            {
                throw new InvalidOperationException(
                    "The folder project was not added to the host.");
            }
        }
        catch
        {
            if (IsLoaded(project))
                _packFileService.TryUnloadPackContainer(project);
            else
                project.Dispose();
            throw;
        }

        _pendingReattach.TryRemove(projectRoot, out _);
    }

    private void RemoveTemporaryEmptyDirectories(
        string projectRoot,
        IReadOnlyCollection<string> emptyDirectories)
    {
        foreach (var configuredPath in emptyDirectories
                     .Select(TryNormalizeDirectory)
                     .Where(path => path != null)
                     .Cast<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(GetPathDepth))
        {
            string fullPath;
            try
            {
                fullPath = FolderProjectPathPolicy.ResolveFilePath(
                    projectRoot,
                    configuredPath);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (!_platform.DirectoryExists(fullPath) ||
                IsReparsePoint(fullPath) ||
                _platform.EnumerateFileSystemEntries(fullPath).Any())
            {
                continue;
            }

            _platform.DeleteDirectory(fullPath);
        }
    }

    private void RemoveAllConfiguredEmptyDirectories(string projectRoot)
    {
        var settings = FolderProjectSettings.Load(projectRoot);
        RemoveTemporaryEmptyDirectories(
            projectRoot,
            settings.EmptyDirectories.ToArray());
    }

    private void PrepareProjectForOpen(string projectRoot)
    {
        var settings = FolderProjectSettings.Load(projectRoot);
        var validDirectories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var configuredPath in settings.EmptyDirectories)
        {
            var normalized = TryNormalizeDirectory(configuredPath);
            if (normalized != null &&
                TryCreateConfiguredDirectory(
                    projectRoot,
                    normalized))
            {
                validDirectories.Add(normalized);
            }
        }

        if (!settings.EmptyDirectories.SetEquals(validDirectories))
        {
            settings.EmptyDirectories = validDirectories;
            settings.Save(projectRoot);
        }
    }

    private bool TryCreateConfiguredDirectory(
        string projectRoot,
        string relativePath)
    {
        string fullPath;
        try
        {
            fullPath = FolderProjectPathPolicy.ResolveFilePath(
                projectRoot,
                relativePath);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        var currentPath = projectRoot;
        foreach (var segment in relativePath.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (_platform.FileExists(currentPath))
                return false;

            if (_platform.DirectoryExists(currentPath))
            {
                if (IsReparsePoint(currentPath))
                    return false;
                continue;
            }

            try
            {
                _platform.CreateDirectory(currentPath);
            }
            catch (IOException)
                when (_platform.FileExists(currentPath))
            {
                return false;
            }
        }

        return _platform.DirectoryExists(fullPath) &&
               !IsReparsePoint(fullPath);
    }

    private bool IsReparsePoint(string path)
    {
        return (_platform.GetAttributes(path) &
                FileAttributes.ReparsePoint) != 0;
    }

    private FolderProjectContainer? FindLoadedProject(
        string projectRoot)
    {
        return _packFileService.GetAllPackfileContainers()
            .OfType<FolderProjectContainer>()
            .FirstOrDefault(project =>
                string.Equals(
                    NormalizeProjectRoot(project.ProjectRoot),
                    projectRoot,
                    StringComparison.OrdinalIgnoreCase));
    }

    private bool IsLoaded(PackFileContainer container)
    {
        return _packFileService.GetAllPackfileContainers()
            .Any(item => ReferenceEquals(item, container));
    }

    private static string? TryNormalizeDirectory(string path)
    {
        return FolderProjectPathPolicy
            .TryNormalizeResourceDirectoryPath(
                path,
                out var normalized)
            ? normalized
            : null;
    }

    private static int GetPathDepth(string path)
    {
        return path.Count(
            character =>
                character == Path.DirectorySeparatorChar);
    }

    private static string NormalizeProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException(
                "The folder-project root is empty.",
                nameof(projectRoot));

        return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
    }

    private sealed record ReattachState(
        int InsertionIndex,
        bool WasEditable,
        IReadOnlyCollection<string> EmptyDirectories);

    private sealed record OperationPreparation(
        FolderProjectContainer? LoadedProject,
        Exception? OperationException);
}
