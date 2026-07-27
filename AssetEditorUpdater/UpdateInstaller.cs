using SharpCompress.Archives.Zip;

namespace AssetEditorUpdater;

internal static class UpdateInstaller
{
    private const string AssetEditorExe = "AssetEditor.CN.exe";
    private const string UpdaterExe = "AssetEditor.CN.Updater.exe";
    private const string ArchiveRoot = "AssetEditor.CN/";
    private const string BackupDirectoryName = "UpdateBackup";
    internal const string BackupRootMarkerFileName = ".asset-editor-cn-updater-backups";
    private const string BackupRootMarkerContents = "AssetEditor.CN updater backup root v1";
    private const string StagingDirectoryName = "staging";

    internal static string Install(string archivePath, string installationDirectory, string updateDirectory)
    {
        return Install(archivePath, installationDirectory, updateDirectory, CopyDirectory);
    }

    internal static string Install(
        string archivePath,
        string installationDirectory,
        string updateDirectory,
        Action<string, string> copyDirectory)
    {
        ArgumentNullException.ThrowIfNull(copyDirectory);

        var backupRootDirectory = GetBackupRootDirectory(updateDirectory);
        ValidateDirectoryLayout(installationDirectory, updateDirectory, backupRootDirectory);

        var stagingDirectory = Path.Combine(updateDirectory, StagingDirectoryName);
        ExtractToStaging(archivePath, stagingDirectory);

        if (!File.Exists(Path.Combine(stagingDirectory, AssetEditorExe)))
            throw new InvalidDataException($"The update does not contain {AssetEditorExe}.");

        var updaterPath = Path.Combine(stagingDirectory, "Updater", UpdaterExe);
        if (!File.Exists(updaterPath))
            throw new InvalidDataException($"The update does not contain Updater/{UpdaterExe}.");

        var backupDirectory = CreateBackupCandidate(backupRootDirectory);
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

        return backupDirectory;
    }

    internal static void RestoreBackup(string installationDirectory, string backupDirectory)
    {
        RestoreBackup(installationDirectory, backupDirectory, CopyDirectory);
    }

    internal static string GetBackupRootDirectory(string updateDirectory)
    {
        return UpdaterWorkspaceFactory
            .GetTransactionPaths(updateDirectory)
            .BackupRootDirectory;
    }

    internal static void ValidateDirectoryLayout(string installationDirectory, string updateDirectory)
    {
        ValidateDirectoryLayout(
            installationDirectory,
            updateDirectory,
            GetBackupRootDirectory(updateDirectory));
    }

    internal static UpdaterWorkspace ValidateDirectoryLayout(
        string installationDirectory,
        string updateDirectory,
        bool isElevated,
        string? localApplicationDataRoot = null,
        string? commonApplicationDataRoot = null)
    {
        var layout = UpdaterWorkspaceFactory.GetExistingLayout(
            isElevated,
            updateDirectory,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        var transactionPaths = UpdaterWorkspaceFactory.GetTransactionPaths(
            layout.UpdateDirectory);

        ValidateDirectoryLayout(
            installationDirectory,
            transactionPaths.UpdateDirectory,
            transactionPaths.BackupRootDirectory);

        if (DirectoriesOverlap(
                installationDirectory,
                transactionPaths.TransactionRoot))
        {
            throw new InvalidOperationException(
                "The updater transaction root must not overlap the installation directory.");
        }

        using var installationIdentity = WindowsPathIdentity.OpenExistingDirectory(
            installationDirectory,
            nameof(installationDirectory));
        using var transactionIdentity = WindowsPathIdentity.OpenExistingDirectory(
            transactionPaths.TransactionRoot,
            nameof(transactionPaths.TransactionRoot));
        WindowsPathIdentity.RequireDisjoint(
            installationIdentity,
            transactionIdentity,
            "The updater transaction root must not overlap the installation directory.");

        return UpdaterWorkspaceFactory.ValidateExisting(layout);
    }

    private static void ValidateDirectoryLayout(
        string installationDirectory,
        string updateDirectory,
        string backupRootDirectory)
    {
        if (DirectoriesOverlap(installationDirectory, updateDirectory)
            || DirectoriesOverlap(installationDirectory, backupRootDirectory))
        {
            throw new InvalidOperationException(
                "The updater working directories must not overlap the installation directory.");
        }

        if (DirectoriesOverlap(updateDirectory, backupRootDirectory))
            throw new InvalidOperationException("The update and backup directories must not overlap.");
    }

    private static void RestoreBackup(
        string installationDirectory,
        string backupDirectory,
        Action<string, string> copyDirectory)
    {
        if (!Directory.Exists(backupDirectory))
            throw new DirectoryNotFoundException($"Update backup not found: {backupDirectory}");

        ClearDirectory(installationDirectory);
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
        copyDirectory(installationDirectory, backupDirectory);

        try
        {
            ClearDirectory(installationDirectory);
        }
        catch (Exception clearException)
        {
            try
            {
                copyDirectory(backupDirectory, installationDirectory);
            }
            catch (Exception restoreException)
            {
                throw new AggregateException(
                    "Clearing the installation and restoring its backup both failed.",
                    clearException,
                    restoreException);
            }

            throw;
        }
    }

    private static string CreateBackupCandidate(string backupRootDirectory)
    {
        EnsureOwnedBackupRoot(backupRootDirectory);

        var backupDirectory = Path.Combine(
            backupRootDirectory,
            $"backup-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);
        return backupDirectory;
    }

    private static void EnsureOwnedBackupRoot(string backupRootDirectory)
    {
        var markerPath = Path.Combine(backupRootDirectory, BackupRootMarkerFileName);
        if (Directory.Exists(backupRootDirectory))
        {
            if (!File.Exists(markerPath)
                || !string.Equals(
                    File.ReadAllText(markerPath),
                    BackupRootMarkerContents,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The backup root is not owned by AssetEditor.CN updater: {backupRootDirectory}");
            }

            return;
        }

        Directory.CreateDirectory(backupRootDirectory);
        using var markerStream = new FileStream(
            markerPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var markerWriter = new StreamWriter(markerStream);
        markerWriter.Write(BackupRootMarkerContents);
    }

    private static void ClearDirectory(string directory)
    {
        foreach (var entryPath in Directory.EnumerateFileSystemEntries(directory))
            DeleteEntry(entryPath);
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

    private static bool DirectoriesOverlap(string firstDirectory, string secondDirectory)
    {
        var firstRoot = EnsureTrailingSeparator(Path.GetFullPath(firstDirectory));
        var secondRoot = EnsureTrailingSeparator(Path.GetFullPath(secondDirectory));
        return firstRoot.StartsWith(secondRoot, StringComparison.OrdinalIgnoreCase)
            || secondRoot.StartsWith(firstRoot, StringComparison.OrdinalIgnoreCase);
    }
}
