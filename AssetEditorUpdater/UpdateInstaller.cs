using SharpCompress.Archives.Zip;

namespace AssetEditorUpdater;

internal static class UpdateInstaller
{
    private const string AssetEditorExe = "AssetEditor.CN.exe";
    private const string UpdaterExe = "AssetEditor.CN.Updater.exe";
    private const string ArchiveRoot = "AssetEditor.CN/";
    private const string BackupDirectoryName = "UpdateBackup";
    private const string StagingDirectoryName = "staging";

    internal static void Install(string archivePath, string installationDirectory, string updateDirectory)
    {
        Install(archivePath, installationDirectory, updateDirectory, CopyDirectory);
    }

    internal static void Install(
        string archivePath,
        string installationDirectory,
        string updateDirectory,
        Action<string, string> copyDirectory)
    {
        var stagingDirectory = Path.Combine(updateDirectory, StagingDirectoryName);
        ExtractToStaging(archivePath, stagingDirectory);

        if (!File.Exists(Path.Combine(stagingDirectory, AssetEditorExe)))
            throw new InvalidDataException($"The update does not contain {AssetEditorExe}.");

        var updaterPath = Path.Combine(stagingDirectory, "Updater", UpdaterExe);
        if (!File.Exists(updaterPath))
            throw new InvalidDataException($"The update does not contain Updater/{UpdaterExe}.");

        var backupDirectory = GetBackupDirectory(installationDirectory);
        BackupInstallation(installationDirectory, backupDirectory, copyDirectory);

        try
        {
            copyDirectory(stagingDirectory, installationDirectory);
        }
        catch (Exception installException)
        {
            try
            {
                RestoreBackup(installationDirectory, backupDirectory, copyDirectory);
            }
            catch (Exception restoreException)
            {
                throw new AggregateException(
                    "The update installation and rollback both failed.",
                    installException,
                    restoreException);
            }

            throw;
        }
    }

    internal static void RestoreBackup(string installationDirectory)
    {
        RestoreBackup(installationDirectory, GetBackupDirectory(installationDirectory), CopyDirectory);
    }

    internal static string GetBackupDirectory(string installationDirectory)
    {
        var fullInstallationPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationDirectory));
        var parentDirectory = Path.GetDirectoryName(fullInstallationPath);
        var installationName = Path.GetFileName(fullInstallationPath);
        if (string.IsNullOrEmpty(parentDirectory) || string.IsNullOrEmpty(installationName))
            throw new InvalidOperationException("The installation directory must have a parent directory.");

        return Path.Combine(parentDirectory, $"{installationName}.{BackupDirectoryName}");
    }

    private static void RestoreBackup(
        string installationDirectory,
        string backupDirectory,
        Action<string, string> copyDirectory)
    {
        if (!Directory.Exists(backupDirectory))
            throw new DirectoryNotFoundException($"Update backup not found: {backupDirectory}");

        foreach (var entryPath in Directory.EnumerateFileSystemEntries(installationDirectory))
            DeleteEntry(entryPath);

        copyDirectory(backupDirectory, installationDirectory);
    }

    internal static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(filePath, destinationPath, true);
        }
    }

    private static void ExtractToStaging(string archivePath, string stagingDirectory)
    {
        if (Directory.Exists(stagingDirectory))
            Directory.Delete(stagingDirectory, true);
        Directory.CreateDirectory(stagingDirectory);

        var stagingRoot = EnsureTrailingSeparator(Path.GetFullPath(stagingDirectory));

        try
        {
            using var archive = ZipArchive.OpenArchive(archivePath);
            foreach (var entry in archive.Entries)
            {
                var destinationPath = GetValidatedDestinationPath(entry.Key, entry.IsDirectory, stagingRoot);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                using var entryStream = entry.OpenEntryStream();
                using var destinationStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                entryStream.CopyTo(destinationStream);
            }
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, true);
            throw;
        }
    }

    private static string GetValidatedDestinationPath(
        string? entryKey,
        bool isDirectory,
        string stagingRoot)
    {
        var normalizedKey = entryKey?.Replace('\\', '/') ?? string.Empty;
        var normalizedPath = isDirectory && normalizedKey.EndsWith('/')
            ? normalizedKey[..^1]
            : normalizedKey;

        if (isDirectory && string.Equals(normalizedPath, ArchiveRoot[..^1], StringComparison.Ordinal))
            return stagingRoot;

        if (!normalizedPath.StartsWith(ArchiveRoot, StringComparison.Ordinal))
            throw new InvalidDataException($"Update entry is outside {ArchiveRoot}: {entryKey}");

        var relativePath = normalizedPath[ArchiveRoot.Length..];
        var segments = relativePath.Split('/');
        if (Path.IsPathRooted(relativePath)
            || segments.Length == 0
            || segments.Any(segment =>
                string.IsNullOrEmpty(segment)
                || segment is "." or ".."
                || segment.EndsWith('.')
                || segment.EndsWith(' ')))
        {
            throw new InvalidDataException($"Update entry has an unsafe path: {entryKey}");
        }

        if (string.Equals(segments[0], BackupDirectoryName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Update entry uses the reserved {BackupDirectoryName} directory: {entryKey}");

        var destinationPath = Path.GetFullPath(
            Path.Combine(stagingRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destinationPath.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Update entry escapes the staging directory: {entryKey}");

        return destinationPath;
    }

    private static void BackupInstallation(
        string installationDirectory,
        string backupDirectory,
        Action<string, string> copyDirectory)
    {
        if (Directory.Exists(backupDirectory))
            Directory.Delete(backupDirectory, true);
        Directory.CreateDirectory(backupDirectory);

        try
        {
            foreach (var entryPath in Directory.EnumerateFileSystemEntries(installationDirectory))
            {
                var destinationPath = Path.Combine(backupDirectory, Path.GetFileName(entryPath));
                if (Directory.Exists(entryPath))
                    Directory.Move(entryPath, destinationPath);
                else
                    File.Move(entryPath, destinationPath);
            }
        }
        catch (Exception backupException)
        {
            try
            {
                copyDirectory(backupDirectory, installationDirectory);
            }
            catch (Exception restoreException)
            {
                throw new AggregateException(
                    "The installation backup and partial-backup restore both failed.",
                    backupException,
                    restoreException);
            }

            throw;
        }
    }

    private static void DeleteEntry(string entryPath)
    {
        if (Directory.Exists(entryPath))
            Directory.Delete(entryPath, true);
        else
            File.Delete(entryPath);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
    }
}
