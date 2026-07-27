using System.Globalization;
using SharpCompress.Archives.Zip;

namespace AssetEditorUpdater;

internal static class UpdateInstaller
{
    private const string AssetEditorExe = "AssetEditor.CN.exe";
    private const string UpdaterExe = "AssetEditor.CN.Updater.exe";
    private const string ArchiveRoot = "AssetEditor.CN/";
    private const string BackupDirectoryName = "UpdateBackup";
    private const string BackupCandidatePrefix = "backup-";
    private const string BackupTimestampFormat = "yyyyMMddHHmmssfff";

    internal static string Install(
        string archivePath,
        string installationDirectory,
        UpdaterWorkspace workspace)
    {
        return Install(
            archivePath,
            installationDirectory,
            workspace,
            CopyDirectory,
            null,
            null,
            null);
    }

    internal static string Install(
        string archivePath,
        string installationDirectory,
        UpdaterWorkspace workspace,
        Action<string, string> copyDirectory,
        string? localApplicationDataRoot,
        string? commonApplicationDataRoot)
    {
        return Install(
            archivePath,
            installationDirectory,
            workspace,
            copyDirectory,
            localApplicationDataRoot,
            commonApplicationDataRoot,
            null);
    }

    internal static string Install(
        string archivePath,
        string installationDirectory,
        UpdaterWorkspace workspace,
        Action<string, string> copyDirectory,
        string? localApplicationDataRoot,
        string? commonApplicationDataRoot,
        Action<string>? afterStagingCreated)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(copyDirectory);

        var validatedWorkspace = ValidateWorkspaceMatch(
            installationDirectory,
            workspace,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
            validatedWorkspace.UpdateDirectory);
        ValidateArchivePath(archivePath, paths.UpdateDirectory);

        using var installationIdentity = WindowsPathIdentity.OpenExistingDirectory(
            installationDirectory,
            nameof(installationDirectory));
        using var transactionIdentity = WindowsPathIdentity.OpenExistingDirectory(
            paths.TransactionRoot,
            nameof(workspace));
        WindowsPathIdentity.RequireDisjoint(
            installationIdentity,
            transactionIdentity,
            "The updater transaction root must not overlap the installation directory.");

        ExtractToStaging(
            archivePath,
            validatedWorkspace,
            paths,
            installationDirectory,
            installationIdentity,
            transactionIdentity,
            localApplicationDataRoot,
            commonApplicationDataRoot,
            afterStagingCreated);

        ValidateRequiredPayload(paths.StagingDirectory);
        var backupRootDirectory = EnsureBackupRoot(
            validatedWorkspace,
            paths,
            transactionIdentity);
        var backupDirectory = CreateBackupCandidate(
            validatedWorkspace,
            backupRootDirectory);

        BackupInstallation(
            installationDirectory,
            paths.StagingDirectory,
            backupDirectory,
            validatedWorkspace,
            installationIdentity,
            transactionIdentity,
            copyDirectory,
            localApplicationDataRoot,
            commonApplicationDataRoot);

        try
        {
            ValidateDestructivePhase(
                installationDirectory,
                validatedWorkspace,
                installationIdentity,
                transactionIdentity,
                localApplicationDataRoot,
                commonApplicationDataRoot);
            ValidateOwnedDerivedDirectory(
                paths.StagingDirectory,
                paths.TransactionRoot,
                validatedWorkspace.IsProtected);
            using var stagingIdentity = WindowsPathIdentity.OpenExistingDirectory(
                paths.StagingDirectory,
                nameof(paths.StagingDirectory));
            RequireDirectPhysicalChild(
                transactionIdentity,
                stagingIdentity,
                "staging must be a direct child of the transaction root.");
            CaptureTree(paths.StagingDirectory);
            CaptureTree(installationDirectory);
            copyDirectory(paths.StagingDirectory, installationDirectory);
        }
        catch (Exception installException)
        {
            try
            {
                RestoreBackupCore(
                    installationDirectory,
                    validatedWorkspace,
                    backupDirectory,
                    installationIdentity,
                    transactionIdentity,
                    copyDirectory,
                    localApplicationDataRoot,
                    commonApplicationDataRoot);
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

    internal static void RestoreBackup(
        string installationDirectory,
        UpdaterWorkspace workspace,
        string backupDirectory,
        Action<string, string> copyDirectory,
        string? localApplicationDataRoot,
        string? commonApplicationDataRoot)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(copyDirectory);

        var validatedWorkspace = ValidateWorkspaceMatch(
            installationDirectory,
            workspace,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        using var installationIdentity = WindowsPathIdentity.OpenExistingDirectory(
            installationDirectory,
            nameof(installationDirectory));
        using var transactionIdentity = WindowsPathIdentity.OpenExistingDirectory(
            validatedWorkspace.TransactionRoot,
            nameof(workspace));
        WindowsPathIdentity.RequireDisjoint(
            installationIdentity,
            transactionIdentity,
            "The updater transaction root must not overlap the installation directory.");

        RestoreBackupCore(
            installationDirectory,
            validatedWorkspace,
            backupDirectory,
            installationIdentity,
            transactionIdentity,
            copyDirectory,
            localApplicationDataRoot,
            commonApplicationDataRoot);
    }

    internal static string GetBackupRootDirectory(string updateDirectory)
    {
        return UpdaterWorkspaceFactory
            .GetTransactionPaths(updateDirectory)
            .BackupRootDirectory;
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

    internal static UpdaterWorkspace ValidateWorkspace(
        string installationDirectory,
        UpdaterWorkspace workspace,
        string? localApplicationDataRoot = null,
        string? commonApplicationDataRoot = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return ValidateWorkspaceMatch(
            installationDirectory,
            workspace,
            localApplicationDataRoot,
            commonApplicationDataRoot);
    }

    internal static void DeleteOwnedArchive(
        string installationDirectory,
        UpdaterWorkspace workspace,
        string archivePath,
        string? localApplicationDataRoot = null,
        string? commonApplicationDataRoot = null)
    {
        var validatedWorkspace = ValidateWorkspace(
            installationDirectory,
            workspace,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
            validatedWorkspace.UpdateDirectory);
        ValidateArchivePath(archivePath, paths.UpdateDirectory);

        using var transactionIdentity = WindowsPathIdentity.OpenExistingDirectory(
            paths.TransactionRoot,
            nameof(workspace));
        using var updateIdentity = WindowsPathIdentity.OpenExistingDirectory(
            paths.UpdateDirectory,
            nameof(workspace));
        RequireDirectPhysicalChild(
            transactionIdentity,
            updateIdentity,
            "Update must be a direct child of the transaction root.");
        ValidateOrdinaryFile(archivePath);
        File.Delete(archivePath);
    }

    internal static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory)
    {
        _ = UpdaterPayloadCopier.CopyAndVerify(
            sourceDirectory,
            destinationDirectory);
    }

    private static void ExtractToStaging(
        string archivePath,
        UpdaterWorkspace workspace,
        UpdaterTransactionPaths paths,
        string installationDirectory,
        WindowsDirectoryIdentityLease installationIdentity,
        WindowsDirectoryIdentityLease transactionIdentity,
        string? localApplicationDataRoot,
        string? commonApplicationDataRoot,
        Action<string>? afterStagingCreated)
    {
        UpdaterWorkspaceFactory.CreateOwnedDirectoryFresh(
            paths.StagingDirectory,
            workspace.IsProtected);
        ValidateOwnedDerivedDirectory(
            paths.StagingDirectory,
            paths.TransactionRoot,
            workspace.IsProtected);
        using (var stagingIdentity = WindowsPathIdentity.OpenExistingDirectory(
                   paths.StagingDirectory,
                   nameof(paths.StagingDirectory)))
        {
            RequireDirectPhysicalChild(
                transactionIdentity,
                stagingIdentity,
                "staging must be a direct child of the transaction root.");
        }

        try
        {
            afterStagingCreated?.Invoke(paths.StagingDirectory);
            var stagingRoot = EnsureTrailingSeparator(
                Path.GetFullPath(paths.StagingDirectory));
            using var archive = ZipArchive.OpenArchive(archivePath);
            foreach (var entry in archive.Entries)
            {
                ValidateOwnedDerivedDirectory(
                    paths.StagingDirectory,
                    paths.TransactionRoot,
                    workspace.IsProtected);
                var destinationPath = GetValidatedDestinationPath(
                    entry.Key,
                    entry.IsDirectory,
                    stagingRoot);
                if (entry.IsDirectory)
                {
                    EnsureExtractedDirectory(
                        paths.StagingDirectory,
                        destinationPath,
                        workspace.IsProtected);
                    continue;
                }

                var parent = Path.GetDirectoryName(destinationPath)
                    ?? throw new InvalidDataException(
                        "The update entry has no staging parent.");
                EnsureExtractedDirectory(
                    paths.StagingDirectory,
                    parent,
                    workspace.IsProtected);
                ValidateParentChain(paths.StagingDirectory, destinationPath);

                using var entryStream = entry.OpenEntryStream();
                using var destinationStream = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                entryStream.CopyTo(destinationStream);
                destinationStream.Flush(flushToDisk: true);
            }
        }
        catch
        {
            try
            {
                ValidateDestructivePhase(
                    installationDirectory,
                    workspace,
                    installationIdentity,
                    transactionIdentity,
                    localApplicationDataRoot,
                    commonApplicationDataRoot);
                DeleteOwnedDerivedDirectory(
                    paths.StagingDirectory,
                    paths.TransactionRoot,
                    workspace.IsProtected,
                    transactionIdentity);
            }
            catch (Exception cleanupException)
            {
                Console.Error.WriteLine(
                    $"Updater cleanup failed; preserving the original failure: {cleanupException}");
            }

            throw;
        }
    }

    private static void EnsureExtractedDirectory(
        string stagingRoot,
        string directoryPath,
        bool isProtected)
    {
        var relativePath = Path.GetRelativePath(stagingRoot, directoryPath);
        if (relativePath == ".")
        {
            ValidateOrdinaryDirectory(stagingRoot);
            return;
        }

        var current = stagingRoot;
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (PathEntryExists(current))
            {
                ValidateOrdinaryDirectory(current);
                continue;
            }

            UpdaterWorkspaceFactory.CreateOwnedDirectoryFresh(
                current,
                isProtected);
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

        if (isDirectory
            && string.Equals(
                normalizedPath,
                ArchiveRoot[..^1],
                StringComparison.Ordinal))
        {
            return Path.TrimEndingDirectorySeparator(stagingRoot);
        }

        if (!normalizedPath.StartsWith(ArchiveRoot, StringComparison.Ordinal))
            throw new InvalidDataException($"Update entry is outside {ArchiveRoot}: {entryKey}");

        var relativePath = normalizedPath[ArchiveRoot.Length..];
        var segments = relativePath.Split('/');
        if (Path.IsPathRooted(relativePath)
            || segments.Length == 0
            || segments.Any(segment =>
                string.IsNullOrEmpty(segment)
                || segment is "." or ".."
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || segment.EndsWith('.')
                || segment.EndsWith(' ')))
        {
            throw new InvalidDataException($"Update entry has an unsafe path: {entryKey}");
        }

        if (string.Equals(
                segments[0],
                BackupDirectoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Update entry uses the reserved {BackupDirectoryName} directory: {entryKey}");
        }

        var destinationPath = Path.GetFullPath(
            Path.Combine(
                stagingRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinDirectory(
                Path.TrimEndingDirectorySeparator(stagingRoot),
                destinationPath))
        {
            throw new InvalidDataException(
                $"Update entry escapes the staging directory: {entryKey}");
        }

        return destinationPath;
    }

    private static void BackupInstallation(
        string installationDirectory,
        string stagingDirectory,
        string backupDirectory,
        UpdaterWorkspace workspace,
        WindowsDirectoryIdentityLease installationIdentity,
        WindowsDirectoryIdentityLease transactionIdentity,
        Action<string, string> copyDirectory,
        string? localApplicationDataRoot,
        string? commonApplicationDataRoot)
    {
        ValidateDestructivePhase(
            installationDirectory,
            workspace,
            installationIdentity,
            transactionIdentity,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
            workspace.UpdateDirectory);
        ValidateOwnedDerivedDirectory(
            stagingDirectory,
            paths.TransactionRoot,
            workspace.IsProtected);
        ValidateBackupCandidatePath(
            backupDirectory,
            paths.BackupRootDirectory);
        ValidateOwnedDerivedDirectory(
            backupDirectory,
            paths.BackupRootDirectory,
            workspace.IsProtected);
        using var stagingIdentity = WindowsPathIdentity.OpenExistingDirectory(
            stagingDirectory,
            nameof(stagingDirectory));
        using var backupRootIdentity = WindowsPathIdentity.OpenExistingDirectory(
            paths.BackupRootDirectory,
            nameof(paths.BackupRootDirectory));
        using var backupIdentity = WindowsPathIdentity.OpenExistingDirectory(
            backupDirectory,
            nameof(backupDirectory));
        RequireDirectPhysicalChild(
            transactionIdentity,
            stagingIdentity,
            "staging must be a direct child of the transaction root.");
        RequireDirectPhysicalChild(
            backupRootIdentity,
            backupIdentity,
            "The update backup must be a direct child of UpdateBackups.");
        CaptureTree(installationDirectory);
        CaptureTree(stagingDirectory);
        CaptureTree(backupDirectory);
        copyDirectory(installationDirectory, backupDirectory);

        ValidateDestructivePhase(
            installationDirectory,
            workspace,
            installationIdentity,
            transactionIdentity,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        CaptureTree(installationDirectory);
        CaptureTree(stagingDirectory);
        CaptureTree(backupDirectory);

        try
        {
            ClearDirectory(installationDirectory);
        }
        catch (Exception clearException)
        {
            try
            {
                RestoreBackupCore(
                    installationDirectory,
                    workspace,
                    backupDirectory,
                    installationIdentity,
                    transactionIdentity,
                    copyDirectory,
                    localApplicationDataRoot,
                    commonApplicationDataRoot);
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

    private static void RestoreBackupCore(
        string installationDirectory,
        UpdaterWorkspace workspace,
        string backupDirectory,
        WindowsDirectoryIdentityLease installationIdentity,
        WindowsDirectoryIdentityLease transactionIdentity,
        Action<string, string> copyDirectory,
        string? localApplicationDataRoot,
        string? commonApplicationDataRoot)
    {
        ValidateDestructivePhase(
            installationDirectory,
            workspace,
            installationIdentity,
            transactionIdentity,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
            workspace.UpdateDirectory);
        ValidateBackupCandidatePath(backupDirectory, paths.BackupRootDirectory);
        ValidateOwnedDerivedDirectory(
            paths.BackupRootDirectory,
            paths.TransactionRoot,
            workspace.IsProtected);
        ValidateOwnedDerivedDirectory(
            backupDirectory,
            paths.BackupRootDirectory,
            workspace.IsProtected);

        using var backupRootIdentity = WindowsPathIdentity.OpenExistingDirectory(
            paths.BackupRootDirectory,
            nameof(paths.BackupRootDirectory));
        using var backupIdentity = WindowsPathIdentity.OpenExistingDirectory(
            backupDirectory,
            nameof(backupDirectory));
        RequireDirectPhysicalChild(
            backupRootIdentity,
            backupIdentity,
            "The update backup must be a direct child of UpdateBackups.");
        WindowsPathIdentity.RequireDisjoint(
            installationIdentity,
            backupIdentity,
            "The update backup must not overlap the installation directory.");

        CaptureTree(backupDirectory);
        CaptureTree(installationDirectory);
        ClearDirectory(installationDirectory);

        ValidateDestructivePhase(
            installationDirectory,
            workspace,
            installationIdentity,
            transactionIdentity,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        CaptureTree(backupDirectory);
        CaptureTree(installationDirectory);
        copyDirectory(backupDirectory, installationDirectory);
    }

    private static string EnsureBackupRoot(
        UpdaterWorkspace workspace,
        UpdaterTransactionPaths paths,
        WindowsDirectoryIdentityLease transactionIdentity)
    {
        if (!PathEntryExists(paths.BackupRootDirectory))
        {
            UpdaterWorkspaceFactory.CreateOwnedDirectoryFresh(
                paths.BackupRootDirectory,
                workspace.IsProtected);
        }

        ValidateOwnedDerivedDirectory(
            paths.BackupRootDirectory,
            paths.TransactionRoot,
            workspace.IsProtected);
        using var backupRootIdentity = WindowsPathIdentity.OpenExistingDirectory(
            paths.BackupRootDirectory,
            nameof(paths.BackupRootDirectory));
        RequireDirectPhysicalChild(
            transactionIdentity,
            backupRootIdentity,
            "UpdateBackups must be a direct child of the transaction root.");
        return paths.BackupRootDirectory;
    }

    private static string CreateBackupCandidate(
        UpdaterWorkspace workspace,
        string backupRootDirectory)
    {
        var timestamp = DateTime.UtcNow.ToString(
            BackupTimestampFormat,
            CultureInfo.InvariantCulture);
        var backupDirectory = Path.Combine(
            backupRootDirectory,
            $"{BackupCandidatePrefix}{timestamp}-{Guid.NewGuid():N}");
        UpdaterWorkspaceFactory.CreateOwnedDirectoryFresh(
            backupDirectory,
            workspace.IsProtected);
        ValidateBackupCandidatePath(backupDirectory, backupRootDirectory);
        ValidateOwnedDerivedDirectory(
            backupDirectory,
            backupRootDirectory,
            workspace.IsProtected);
        using var backupRootIdentity = WindowsPathIdentity.OpenExistingDirectory(
            backupRootDirectory,
            nameof(backupRootDirectory));
        using var backupIdentity = WindowsPathIdentity.OpenExistingDirectory(
            backupDirectory,
            nameof(backupDirectory));
        RequireDirectPhysicalChild(
            backupRootIdentity,
            backupIdentity,
            "The update backup must be a direct child of UpdateBackups.");
        return backupDirectory;
    }

    private static void ValidateRequiredPayload(string stagingDirectory)
    {
        CaptureTree(stagingDirectory);
        var assetEditorPath = Path.Combine(stagingDirectory, AssetEditorExe);
        if (!IsOrdinaryFile(assetEditorPath))
            throw new InvalidDataException($"The update does not contain {AssetEditorExe}.");

        var updaterPath = Path.Combine(stagingDirectory, "Updater", UpdaterExe);
        if (!IsOrdinaryFile(updaterPath))
        {
            throw new InvalidDataException(
                $"The update does not contain Updater/{UpdaterExe}.");
        }
    }

    private static UpdaterWorkspace ValidateDestructivePhase(
        string installationDirectory,
        UpdaterWorkspace workspace,
        WindowsDirectoryIdentityLease installationIdentity,
        WindowsDirectoryIdentityLease transactionIdentity,
        string? localApplicationDataRoot,
        string? commonApplicationDataRoot)
    {
        var validatedWorkspace = ValidateWorkspaceMatch(
            installationDirectory,
            workspace,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        using var currentInstallationIdentity =
            WindowsPathIdentity.OpenExistingDirectory(
                installationDirectory,
                nameof(installationDirectory));
        using var currentTransactionIdentity =
            WindowsPathIdentity.OpenExistingDirectory(
                validatedWorkspace.TransactionRoot,
                nameof(workspace));
        if (!WindowsPathIdentity.IsSameDirectory(
                installationIdentity,
                currentInstallationIdentity)
            || !WindowsPathIdentity.IsSameDirectory(
                transactionIdentity,
                currentTransactionIdentity))
        {
            throw new InvalidOperationException(
                "An updater destructive root changed after validation.");
        }

        WindowsPathIdentity.RequireDisjoint(
            currentInstallationIdentity,
            currentTransactionIdentity,
            "The updater transaction root must not overlap the installation directory.");
        return validatedWorkspace;
    }

    private static UpdaterWorkspace ValidateWorkspaceMatch(
        string installationDirectory,
        UpdaterWorkspace workspace,
        string? localApplicationDataRoot,
        string? commonApplicationDataRoot)
    {
        var validatedWorkspace = ValidateDirectoryLayout(
            installationDirectory,
            workspace.UpdateDirectory,
            workspace.IsProtected,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        if (workspace.IsProtected != validatedWorkspace.IsProtected
            || !PathsEqual(
                workspace.TransactionRoot,
                validatedWorkspace.TransactionRoot)
            || !PathsEqual(
                workspace.UpdateDirectory,
                validatedWorkspace.UpdateDirectory))
        {
            throw new InvalidOperationException(
                "The supplied updater workspace does not match the validated transaction.");
        }

        return validatedWorkspace;
    }

    private static void ValidateArchivePath(
        string archivePath,
        string updateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var fullArchivePath = Path.GetFullPath(archivePath);
        var expectedArchivePath = AssetEditorUpdater.GetAssetDownloadPath(
            updateDirectory,
            Path.GetFileName(fullArchivePath));
        if (!PathsEqual(fullArchivePath, expectedArchivePath))
            throw new InvalidDataException("The update archive must be a direct child of Update.");

        ValidateOrdinaryFile(fullArchivePath);
    }

    private static void ValidateDirectoryLayout(
        string installationDirectory,
        string updateDirectory,
        string backupRootDirectory)
    {
        if (DirectoriesOverlap(installationDirectory, updateDirectory)
            || DirectoriesOverlap(
                installationDirectory,
                backupRootDirectory))
        {
            throw new InvalidOperationException(
                "The updater working directories must not overlap the installation directory.");
        }

        if (DirectoriesOverlap(updateDirectory, backupRootDirectory))
        {
            throw new InvalidOperationException(
                "The update and backup directories must not overlap.");
        }
    }

    private static void ValidateBackupCandidatePath(
        string backupDirectory,
        string backupRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        var fullBackupDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(backupDirectory));
        var fullBackupRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(backupRootDirectory));
        if (!string.Equals(
                Path.GetDirectoryName(fullBackupDirectory),
                fullBackupRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The update backup must be a direct child of UpdateBackups.");
        }

        var name = Path.GetFileName(fullBackupDirectory);
        var expectedLength = BackupCandidatePrefix.Length
                             + BackupTimestampFormat.Length
                             + 1
                             + 32;
        if (name.Length != expectedLength
            || !name.StartsWith(
                BackupCandidatePrefix,
                StringComparison.Ordinal)
            || name[BackupCandidatePrefix.Length
                    + BackupTimestampFormat.Length] != '-')
        {
            throw new InvalidDataException(
                "The update backup candidate name is invalid.");
        }

        var timestamp = name.Substring(
            BackupCandidatePrefix.Length,
            BackupTimestampFormat.Length);
        var transactionIdText = name[(BackupCandidatePrefix.Length
                                      + BackupTimestampFormat.Length
                                      + 1)..];
        if (!timestamp.All(character => character is >= '0' and <= '9')
            || !DateTime.TryParseExact(
                timestamp,
                BackupTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out _)
            || !Guid.TryParseExact(transactionIdText, "N", out var transactionId)
            || transactionId == Guid.Empty
            || !string.Equals(
                transactionIdText,
                transactionId.ToString("N"),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The update backup candidate name is invalid.");
        }
    }

    private static void ValidateOwnedDerivedDirectory(
        string directory,
        string expectedParent,
        bool requireProtectedAcl)
    {
        var fullDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(directory));
        var fullExpectedParent = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(expectedParent));
        if (!string.Equals(
                Path.GetDirectoryName(fullDirectory),
                fullExpectedParent,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The updater derived directory is not a direct owned child.");
        }

        ValidateOrdinaryDirectory(fullDirectory);
        if (requireProtectedAcl)
            UpdaterWorkspaceSecurity.ValidateProtectedDirectory(fullDirectory);
    }

    private static void DeleteOwnedDerivedDirectory(
        string directory,
        string expectedParent,
        bool requireProtectedAcl,
        WindowsDirectoryIdentityLease parentIdentity)
    {
        ValidateOwnedDerivedDirectory(
            directory,
            expectedParent,
            requireProtectedAcl);
        using (var directoryIdentity = WindowsPathIdentity.OpenExistingDirectory(
                   directory,
                   nameof(directory)))
        {
            RequireDirectPhysicalChild(
                parentIdentity,
                directoryIdentity,
                "The updater cleanup target is not the expected direct child.");
        }

        DeleteTree(directory);
    }

    private static void RequireDirectPhysicalChild(
        WindowsDirectoryIdentityLease parent,
        WindowsDirectoryIdentityLease child,
        string message)
    {
        if (WindowsPathIdentity.IsSameDirectory(parent, child)
            || !WindowsPathIdentity.IsSameOrAncestor(parent, child)
            || !string.Equals(
                Path.GetDirectoryName(child.FinalVolumePath),
                Path.TrimEndingDirectorySeparator(parent.FinalVolumePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static SafeTree CaptureTree(string root)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        ValidateOrdinaryDirectory(fullRoot);
        var directories = new List<string>();
        var files = new List<string>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CaptureDirectory(fullRoot, fullRoot, directories, files, paths);
        return new SafeTree(fullRoot, directories, files);
    }

    private static void CaptureDirectory(
        string root,
        string currentDirectory,
        List<string> directories,
        List<string> files,
        HashSet<string> paths)
    {
        ValidateOrdinaryDirectory(currentDirectory);
        var initialEntries = EnumerateImmediateEntries(currentDirectory);
        var initialKinds = GetEntryKinds(root, initialEntries);

        foreach (var entry in initialEntries)
        {
            var relativePath = GetSafeRelativePath(root, entry.FullName);
            if (!paths.Add(relativePath))
                throw new InvalidDataException(
                    $"The updater tree contains a path collision: {relativePath}");

            entry.Refresh();
            var attributes = entry.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    $"The updater tree entry cannot be a reparse point: {entry.FullName}");

            if ((attributes & FileAttributes.Directory) != 0)
                directories.Add(relativePath);
            else
                files.Add(relativePath);
        }

        foreach (var entry in initialEntries.Where(IsDirectory))
        {
            CaptureDirectory(
                root,
                entry.FullName,
                directories,
                files,
                paths);
        }

        ValidateOrdinaryDirectory(currentDirectory);
        var finalKinds = GetEntryKinds(
            root,
            EnumerateImmediateEntries(currentDirectory));
        if (initialKinds.Count != finalKinds.Count
            || initialKinds.Any(entry =>
                !finalKinds.TryGetValue(entry.Key, out var finalIsDirectory)
                || entry.Value != finalIsDirectory))
        {
            throw new InvalidDataException(
                $"The updater tree changed during preflight: {currentDirectory}");
        }
    }

    private static Dictionary<string, bool> GetEntryKinds(
        string root,
        IEnumerable<FileSystemInfo> entries)
    {
        var kinds = new Dictionary<string, bool>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            entry.Refresh();
            var attributes = entry.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    $"The updater tree entry cannot be a reparse point: {entry.FullName}");

            var relativePath = GetSafeRelativePath(root, entry.FullName);
            if (!kinds.TryAdd(
                    relativePath,
                    (attributes & FileAttributes.Directory) != 0))
            {
                throw new InvalidDataException(
                    $"The updater tree contains a path collision: {relativePath}");
            }
        }

        return kinds;
    }

    private static FileSystemInfo[] EnumerateImmediateEntries(string directory)
    {
        return new DirectoryInfo(directory)
            .EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
            .ToArray();
    }

    private static void ClearDirectory(string directory)
    {
        var tree = CaptureTree(directory);
        var recheckedTree = CaptureTree(directory);
        if (!tree.HasSameEntries(recheckedTree))
            throw new InvalidDataException(
                "The updater tree changed after destructive preflight.");

        foreach (var relativeFile in recheckedTree.Files)
        {
            var filePath = GetContainedPath(recheckedTree.Root, relativeFile);
            ValidateOrdinaryFile(filePath);
            ValidateParentChain(recheckedTree.Root, filePath);
            File.Delete(filePath);
        }

        foreach (var relativeDirectory in recheckedTree.Directories
                     .OrderByDescending(GetPathDepth))
        {
            var directoryPath = GetContainedPath(
                recheckedTree.Root,
                relativeDirectory);
            ValidateOrdinaryDirectory(directoryPath);
            if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                throw new InvalidDataException(
                    $"The updater directory changed before deletion: {directoryPath}");
            }

            Directory.Delete(directoryPath, recursive: false);
        }

        ValidateOrdinaryDirectory(recheckedTree.Root);
        if (Directory.EnumerateFileSystemEntries(recheckedTree.Root).Any())
            throw new InvalidDataException("The updater directory was not cleared.");
    }

    private static void DeleteTree(string directory)
    {
        ClearDirectory(directory);
        ValidateOrdinaryDirectory(directory);
        Directory.Delete(directory, recursive: false);
    }

    private static string GetSafeRelativePath(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath == "."
            || relativePath.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.None)
                .Any(segment =>
                    string.IsNullOrWhiteSpace(segment)
                    || segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"The updater tree has an unsafe path: {path}");
        }

        return relativePath;
    }

    private static string GetContainedPath(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithinDirectory(root, path))
            throw new InvalidDataException("The updater tree path escaped its root.");

        return path;
    }

    private static void ValidateParentChain(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var current = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException(
                "The updater tree entry has no parent.");
        while (true)
        {
            ValidateOrdinaryDirectory(current);
            if (PathsEqual(current, fullRoot))
                return;

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidDataException(
                    "The updater tree entry escaped its root.");
        }
    }

    private static void ValidateOrdinaryDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0
            || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"The updater path must be an ordinary directory: {path}");
        }
    }

    private static void ValidateOrdinaryFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException(
                $"The updater path must be an ordinary file: {path}");
        }
    }

    private static bool IsOrdinaryFile(string path)
    {
        try
        {
            ValidateOrdinaryFile(path);
            return true;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
            or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static bool PathEntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
            or DirectoryNotFoundException)
        {
            return false;
        }
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

    private static string EnsureTrailingSeparator(string path)
    {
        return Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool IsWithinDirectory(
        string parentDirectory,
        string candidatePath)
    {
        var parentRoot = EnsureTrailingSeparator(
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(parentDirectory)));
        return Path.GetFullPath(candidatePath).StartsWith(
            parentRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool DirectoriesOverlap(
        string firstDirectory,
        string secondDirectory)
    {
        var firstRoot = EnsureTrailingSeparator(Path.GetFullPath(firstDirectory));
        var secondRoot = EnsureTrailingSeparator(Path.GetFullPath(secondDirectory));
        return firstRoot.StartsWith(
                   secondRoot,
                   StringComparison.OrdinalIgnoreCase)
               || secondRoot.StartsWith(
                   firstRoot,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string firstPath, string secondPath)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstPath)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondPath)),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SafeTree(
        string Root,
        IReadOnlyList<string> Directories,
        IReadOnlyList<string> Files)
    {
        internal bool HasSameEntries(SafeTree other)
        {
            return PathsEqual(Root, other.Root)
                   && Directories.ToHashSet(StringComparer.OrdinalIgnoreCase)
                       .SetEquals(other.Directories)
                   && Files.ToHashSet(StringComparer.OrdinalIgnoreCase)
                       .SetEquals(other.Files);
        }
    }
}
