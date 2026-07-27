using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AssetEditorUpdater;

internal static class UpdaterPayloadCopier
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    internal static IReadOnlyDictionary<string, string> CopyAndVerify(
        string sourceDirectory,
        string destinationDirectory)
    {
        using var sourceIdentity = WindowsPathIdentity.OpenExistingDirectory(
            sourceDirectory,
            nameof(sourceDirectory));
        using var destinationIdentity = WindowsPathIdentity.OpenExistingDirectory(
            destinationDirectory,
            nameof(destinationDirectory));
        WindowsPathIdentity.RequireDisjoint(
            sourceIdentity,
            destinationIdentity,
            "The updater payload source and destination must not overlap.");
        var sourceRoot = sourceIdentity.InputPath;
        var destinationRoot = destinationIdentity.InputPath;

        var sourceTree = CaptureTree(sourceRoot);
        var destinationTree = CaptureTree(destinationRoot);
        if (destinationTree.Directories.Count != 0 || destinationTree.Files.Count != 0)
            throw new InvalidOperationException("The updater payload destination must be empty.");

        foreach (var relativeDirectory in sourceTree.Directories.OrderBy(GetPathDepth))
        {
            var destinationPath = GetContainedPath(destinationRoot, relativeDirectory);
            CreateDirectoryFresh(destinationPath);
            RejectReparsePoint(destinationPath, true);
        }

        var hashes = new Dictionary<string, string>(PathComparer);
        foreach (var sourceFile in sourceTree.Files)
        {
            var destinationPath = GetContainedPath(destinationRoot, sourceFile.RelativePath);
            EnsureParentIsSafe(sourceRoot, sourceFile.FullPath);
            EnsureParentIsSafe(destinationRoot, destinationPath);
            var hash = CopyFileAndHash(sourceFile.FullPath, destinationPath);
            if (!hashes.TryAdd(sourceFile.RelativePath, hash))
            {
                throw new InvalidDataException(
                    $"Updater payload contains a case-insensitive path collision: {sourceFile.RelativePath}");
            }
        }

        var manifest = new ReadOnlyDictionary<string, string>(hashes);
        Verify(sourceRoot, destinationRoot, manifest);
        return manifest;
    }

    internal static void Verify(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyDictionary<string, string> expectedHashes)
    {
        Verify(sourceDirectory, destinationDirectory, expectedHashes, null);
    }

    internal static void Verify(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyDictionary<string, string> expectedHashes,
        Action<string>? afterDirectoryEnumerated)
    {
        ArgumentNullException.ThrowIfNull(expectedHashes);

        using var sourceIdentity = WindowsPathIdentity.OpenExistingDirectory(
            sourceDirectory,
            nameof(sourceDirectory));
        using var destinationIdentity = WindowsPathIdentity.OpenExistingDirectory(
            destinationDirectory,
            nameof(destinationDirectory));
        WindowsPathIdentity.RequireDisjoint(
            sourceIdentity,
            destinationIdentity,
            "The updater payload source and destination must not overlap.");
        var sourceRoot = sourceIdentity.InputPath;
        var destinationRoot = destinationIdentity.InputPath;

        var expected = NormalizeManifest(expectedHashes);
        var sourceTree = CaptureTree(sourceRoot, afterDirectoryEnumerated);
        var destinationTree = CaptureTree(destinationRoot, afterDirectoryEnumerated);
        ValidateTreeMatchesManifest("source", sourceTree, expected);
        ValidateTreeMatchesManifest("destination", destinationTree, expected);

        var sourceDirectories = sourceTree.Directories.ToHashSet(PathComparer);
        if (!sourceDirectories.SetEquals(destinationTree.Directories))
            throw new InvalidDataException("The updater payload destination directory tree does not match the source.");

        var destinationFiles = destinationTree.Files.ToDictionary(file => file.RelativePath, PathComparer);
        foreach (var sourceFile in sourceTree.Files)
        {
            var expectedHash = expected[sourceFile.RelativePath];
            EnsureParentIsSafe(sourceRoot, sourceFile.FullPath);
            var sourceHash = ComputeFileHash(sourceFile.FullPath, FileShare.Read);
            if (!string.Equals(sourceHash, expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Updater payload source file changed during copy: {sourceFile.RelativePath}");
            }

            var destinationFile = destinationFiles[sourceFile.RelativePath];
            EnsureParentIsSafe(destinationRoot, destinationFile.FullPath);
            var destinationHash = ComputeFileHash(destinationFile.FullPath, FileShare.None);
            if (!string.Equals(destinationHash, expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Updater payload destination file failed verification: {sourceFile.RelativePath}");
            }
        }
    }

    private static PayloadTree CaptureTree(
        string root,
        Action<string>? afterDirectoryEnumerated = null)
    {
        try
        {
            ValidateExistingDirectory(root);
            var directories = new List<string>();
            var files = new List<PayloadFile>();
            var paths = new HashSet<string>(PathComparer);
            CaptureDirectory(root, root, directories, files, paths, afterDirectoryEnumerated);
            return new PayloadTree(root, directories, files);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Unable to safely enumerate updater payload directory: {root}",
                exception);
        }
    }

    private static void CaptureDirectory(
        string root,
        string currentDirectory,
        List<string> directories,
        List<PayloadFile> files,
        HashSet<string> paths,
        Action<string>? afterDirectoryEnumerated)
    {
        ValidateExistingDirectory(currentDirectory);
        var entries = EnumerateImmediateEntries(currentDirectory);
        afterDirectoryEnumerated?.Invoke(currentDirectory);
        ValidateExistingDirectory(currentDirectory);

        var entryKinds = new Dictionary<string, bool>(PathComparer);
        foreach (var entry in entries)
        {
            entry.Refresh();
            var attributes = entry.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Updater payload entry cannot be a reparse point: {entry.FullName}");

            var relativePath = GetNormalizedRelativePath(root, entry.FullName);
            if (!paths.Add(relativePath))
                throw new InvalidDataException($"Updater payload contains a path collision: {relativePath}");

            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (!entryKinds.TryAdd(relativePath, isDirectory))
                throw new InvalidDataException($"Updater payload contains a path collision: {relativePath}");

            if (isDirectory)
                directories.Add(relativePath);
            else
                files.Add(new PayloadFile(relativePath, entry.FullName));
        }

        foreach (var directory in entries.Where(IsDirectory))
            CaptureDirectory(root, directory.FullName, directories, files, paths, afterDirectoryEnumerated);

        ValidateExistingDirectory(currentDirectory);
        var finalEntries = EnumerateImmediateEntries(currentDirectory);
        var finalKinds = new Dictionary<string, bool>(PathComparer);
        foreach (var entry in finalEntries)
        {
            entry.Refresh();
            var attributes = entry.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Updater payload entry cannot be a reparse point: {entry.FullName}");

            var relativePath = GetNormalizedRelativePath(root, entry.FullName);
            if (!finalKinds.TryAdd(
                    relativePath,
                    (attributes & FileAttributes.Directory) != 0))
                throw new InvalidDataException($"Updater payload contains a path collision: {relativePath}");
        }

        if (entryKinds.Count != finalKinds.Count
            || entryKinds.Any(entry =>
                !finalKinds.TryGetValue(entry.Key, out var finalIsDirectory)
                || entry.Value != finalIsDirectory))
        {
            throw new InvalidDataException(
                $"Updater payload directory changed during enumeration: {currentDirectory}");
        }
    }

    private static FileSystemInfo[] EnumerateImmediateEntries(string directory)
    {
        return new DirectoryInfo(directory)
            .EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> NormalizeManifest(
        IReadOnlyDictionary<string, string> expectedHashes)
    {
        var normalized = new Dictionary<string, string>(PathComparer);
        foreach (var entry in expectedHashes)
        {
            var relativePath = NormalizeManifestPath(entry.Key);
            if (!IsUppercaseSha256(entry.Value))
                throw new InvalidDataException($"Updater payload manifest has an invalid SHA-256 hash: {entry.Key}");

            if (!normalized.TryAdd(relativePath, entry.Value))
                throw new InvalidDataException($"Updater payload manifest contains a path collision: {relativePath}");
        }

        return normalized;
    }

    private static void ValidateTreeMatchesManifest(
        string treeName,
        PayloadTree tree,
        IReadOnlyDictionary<string, string> expected)
    {
        if (tree.Files.Count != expected.Count
            || tree.Files.Any(file => !expected.ContainsKey(file.RelativePath)))
        {
            throw new InvalidDataException(
                $"Updater payload {treeName} files do not match the expected manifest.");
        }
    }

    private static string CopyFileAndHash(string sourcePath, string destinationPath)
    {
        RejectReparsePoint(sourcePath, false);
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);

        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);

        source.Position = 0;
        destination.Position = 0;
        var sourceHash = SHA256.HashData(source);
        var destinationHash = SHA256.HashData(destination);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash))
            throw new InvalidDataException($"Updater payload copy failed verification: {sourcePath}");

        return Convert.ToHexString(sourceHash);
    }

    private static string ComputeFileHash(string path, FileShare fileShare)
    {
        RejectReparsePoint(path, false);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, fileShare);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void ValidateExistingDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Updater payload directory not found: {path}");

        RejectReparsePoint(path, true);
    }

    private static void RejectReparsePoint(string path, bool expectDirectory)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        if (isDirectory != expectDirectory)
            throw new InvalidDataException($"Updater payload entry has an unexpected type: {path}");

        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Updater payload entry cannot be a reparse point: {path}");
    }

    private static void EnsureParentIsSafe(string root, string path)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("Updater payload file has no parent directory.");
        var current = parent;
        while (true)
        {
            RejectReparsePoint(current, true);
            if (PathComparer.Equals(current, root))
                return;

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidDataException("Updater payload file escaped its root.");
        }
    }

    private static void CreateDirectoryFresh(string path)
    {
        if (CreateDirectory(path, IntPtr.Zero))
            return;

        var error = Marshal.GetLastWin32Error();
        throw new IOException(
            $"Unable to create a fresh updater payload directory: {path}",
            new Win32Exception(error));
    }

    private static string GetNormalizedRelativePath(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        return NormalizeManifestPath(relativePath);
    }

    private static string NormalizeManifestPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Updater payload has an unsafe relative path: {relativePath}");

        var normalized = relativePath.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar);
        if (segments.Any(segment =>
                string.IsNullOrEmpty(segment)
                || segment is "." or ".."
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException($"Updater payload has an unsafe relative path: {relativePath}");
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static string GetContainedPath(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithinDirectory(root, path))
            throw new InvalidDataException($"Updater payload path escaped its root: {relativePath}");

        return path;
    }

    private static bool IsUppercaseSha256(string? hash)
    {
        return hash != null
               && hash.Length == 64
               && hash.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
    }

    private static bool IsDirectory(FileSystemInfo entry)
    {
        entry.Refresh();
        return (entry.Attributes & FileAttributes.Directory) != 0;
    }

    private static int GetPathDepth(string path)
    {
        return path.Count(character =>
            character == Path.DirectorySeparatorChar
            || character == Path.AltDirectorySeparatorChar);
    }

    private static bool IsWithinDirectory(string parentDirectory, string candidatePath)
    {
        var parentRoot = Path.EndsInDirectorySeparator(parentDirectory)
            ? parentDirectory
            : parentDirectory + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(parentRoot, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectory(
        string path,
        IntPtr securityAttributes);

    private sealed record PayloadTree(
        string Root,
        IReadOnlyList<string> Directories,
        IReadOnlyList<PayloadFile> Files);

    private sealed record PayloadFile(
        string RelativePath,
        string FullPath);
}
