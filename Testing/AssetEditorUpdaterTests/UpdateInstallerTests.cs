using System.IO.Compression;
using System.Formats.Tar;
using System.Diagnostics;
using AssetEditorUpdater;
using Shared.Core.Misc;
using UpdaterProgram = AssetEditorUpdater.AssetEditorUpdater;

namespace AssetEditorUpdaterTests;

public class UpdateInstallerTests
{
    [Test]
    public void Install_SuccessDeletesSiblingStagingAndSecondInstallCanRun()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var archivePath = CreateUpdateZip(owned.Workspace.UpdateDirectory);
            var transactionPaths = UpdaterWorkspaceFactory.GetTransactionPaths(
                owned.Workspace.UpdateDirectory);
            var copySources = new List<string>();

            void CopyAndRecord(string sourceDirectory, string destinationDirectory)
            {
                copySources.Add(sourceDirectory);
                UpdateInstaller.CopyDirectory(sourceDirectory, destinationDirectory);
            }

            UpdateInstaller.Install(
                archivePath,
                installationDirectory,
                owned.Workspace,
                CopyAndRecord,
                owned.LocalRoot,
                owned.CommonRoot);
            var secondArchivePath = CreateUpdateZip(
                owned.Workspace.UpdateDirectory,
                ("AssetEditor.CN/second-update.txt", "second"));
            UpdateInstaller.Install(
                secondArchivePath,
                installationDirectory,
                owned.Workspace,
                CopyAndRecord,
                owned.LocalRoot,
                owned.CommonRoot);

            Assert.Multiple(() =>
            {
                Assert.That(
                    copySources.Count(path =>
                        PathsEqual(path, transactionPaths.StagingDirectory)),
                    Is.EqualTo(2));
                Assert.That(
                    Directory.Exists(Path.Combine(owned.Workspace.UpdateDirectory, "staging")),
                    Is.False);
                Assert.That(
                    Directory.Exists(transactionPaths.StagingDirectory),
                    Is.False);
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        installationDirectory,
                        "second-update.txt")),
                    Is.EqualTo("second"));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_StagingCleanupFailurePreservesSuccessfulResult()
    {
        var root = CreateTemporaryDirectory();
        FileStream? stagingBlocker = null;
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var archivePath = CreateUpdateZip(
                owned.Workspace.UpdateDirectory);
            var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
                owned.Workspace.UpdateDirectory);

            void CopyAndBlockStagingCleanup(
                string sourceDirectory,
                string destinationDirectory)
            {
                UpdateInstaller.CopyDirectory(
                    sourceDirectory,
                    destinationDirectory);
                if (!PathsEqual(
                        sourceDirectory,
                        paths.StagingDirectory))
                {
                    return;
                }

                stagingBlocker = new FileStream(
                    Path.Combine(
                        sourceDirectory,
                        "AssetEditor.CN.exe"),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
            }

            var previousError = Console.Error;
            using var cleanupLog = new StringWriter();
            string backupDirectory;
            try
            {
                Console.SetError(cleanupLog);
                backupDirectory = UpdateInstaller.Install(
                    archivePath,
                    installationDirectory,
                    owned.Workspace,
                    CopyAndBlockStagingCleanup,
                    owned.LocalRoot,
                    owned.CommonRoot);
            }
            finally
            {
                Console.SetError(previousError);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(backupDirectory), Is.True);
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        installationDirectory,
                        "AssetEditor.CN.exe")),
                    Is.EqualTo("new executable"));
                Assert.That(
                    Directory.Exists(paths.StagingDirectory),
                    Is.True);
                Assert.That(
                    cleanupLog.ToString(),
                    Does.Contain(
                        "Updater cleanup failed; preserving the primary result"));
            });
        }
        finally
        {
            stagingBlocker?.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_MissingMarkerIsRejectedBeforeExtractionOrCopy()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var archivePath = CreateUpdateZip(owned.Workspace.UpdateDirectory);
            var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
                owned.Workspace.UpdateDirectory);
            File.Delete(paths.MarkerPath);
            var copyCount = 0;

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.Install(
                    archivePath,
                    installationDirectory,
                    owned.Workspace,
                    (_, _) => copyCount++,
                    owned.LocalRoot,
                    owned.CommonRoot));

            Assert.Multiple(() =>
            {
                Assert.That(copyCount, Is.Zero);
                Assert.That(Directory.Exists(paths.StagingDirectory), Is.False);
                Assert.That(Directory.Exists(paths.BackupRootDirectory), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_ArchiveOutsideUpdateIsRejectedBeforeExtractionOrCopy()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var archivePath = CreateUpdateZip(root);
            var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
                owned.Workspace.UpdateDirectory);
            var copyCount = 0;

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.Install(
                    archivePath,
                    installationDirectory,
                    owned.Workspace,
                    (_, _) => copyCount++,
                    owned.LocalRoot,
                    owned.CommonRoot));

            Assert.Multiple(() =>
            {
                Assert.That(copyCount, Is.Zero);
                Assert.That(Directory.Exists(paths.StagingDirectory), Is.False);
                Assert.That(Directory.Exists(paths.BackupRootDirectory), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_ValidZip_ReplacesInstallationAndKeepsExternalBackup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var archivePath = CreateUpdateZip(updateDirectory,
                (@"AssetEditor.CN\data\config.json", "new config"));

            var backupDirectory = InstallUpdate(
                archivePath,
                installationDirectory,
                owned);
            var backupRoot = UpdateInstaller.GetBackupRootDirectory(updateDirectory);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("new executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "Updater", "AssetEditor.CN.Updater.exe")), Is.EqualTo("new updater"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "data", "config.json")), Is.EqualTo("new config"));
                Assert.That(File.Exists(Path.Combine(installationDirectory, "old-only.txt")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(backupDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(backupDirectory, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(IsWithinDirectory(installationDirectory, backupDirectory), Is.False);
                Assert.That(IsWithinDirectory(backupRoot, backupDirectory), Is.True);
                Assert.That(
                    Path.GetDirectoryName(backupDirectory),
                    Is.EqualTo(backupRoot));
                Assert.That(
                    File.Exists(Path.Combine(
                        backupRoot,
                        ".asset-editor-cn-updater-backups")),
                    Is.False);
                Assert.That(
                    File.Exists(Path.Combine(
                        owned.Workspace.TransactionRoot,
                        UpdaterWorkspaceFactory.TransactionMarkerFileName)),
                    Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_ProtectedDerivedDirectoriesUseExactProtectedDescriptor()
    {
        if (!UpdaterWorkspaceFactory.IsProcessElevated())
            Assert.Ignore("Protected installer verification requires an elevated Windows token.");

        var root = CreateTemporaryDirectory();
        try
        {
            var localRoot = Path.Combine(root, "local");
            var commonRoot = Directory.CreateDirectory(
                Path.Combine(root, "common")).FullName;
            var workspace = UpdaterWorkspaceFactory.Create(
                UpdaterWorkspaceFactory.GetLayout(
                    true,
                    Guid.NewGuid(),
                    localRoot,
                    commonRoot));
            var installationDirectory = CreateInstallation(root);
            var archivePath = CreateUpdateZip(workspace.UpdateDirectory);

            var backupDirectory = UpdateInstaller.Install(
                archivePath,
                installationDirectory,
                workspace,
                UpdateInstaller.CopyDirectory,
                localRoot,
                commonRoot);
            var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
                workspace.UpdateDirectory);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Directory.Exists(paths.StagingDirectory),
                    Is.False);
                Assert.DoesNotThrow(() =>
                    UpdaterWorkspaceSecurity.ValidateProtectedDirectory(
                        paths.BackupRootDirectory));
                Assert.DoesNotThrow(() =>
                    UpdaterWorkspaceSecurity.ValidateProtectedDirectory(
                        backupDirectory));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestCase("AssetEditor.CN/../escaped.txt")]
    [TestCase(@"AssetEditor.CN\..\escaped.txt")]
    [TestCase("AnotherRoot/file.txt")]
    [TestCase("C:/absolute.txt")]
    [TestCase("AssetEditor.CN/UpdateBackup./injected.txt")]
    [TestCase("AssetEditor.CN/UpdateBackup /injected.txt")]
    [TestCase("AssetEditor.CN/data./injected.txt")]
    [TestCase("AssetEditor.CN/data /injected.txt")]
    public void Install_UnsafeEntry_RejectsBeforeChangingInstallation(string unsafeEntry)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var archivePath = CreateUpdateZip(updateDirectory, (unsafeEntry, "unsafe"));

            Assert.Throws<InvalidDataException>(() =>
                InstallUpdate(archivePath, installationDirectory, owned));

            AssertInstallationWasNotTouched(installationDirectory, updateDirectory);
            Assert.That(
                Directory.Exists(
                    UpdaterWorkspaceFactory
                        .GetTransactionPaths(updateDirectory)
                        .StagingDirectory),
                Is.False);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_ZipWithoutMainExecutable_RejectsBeforeChangingInstallation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var archivePath = CreateZip(
                updateDirectory,
                ("AssetEditor.CN/readme.txt", "missing executable"));

            Assert.Throws<InvalidDataException>(() =>
                InstallUpdate(archivePath, installationDirectory, owned));

            AssertInstallationWasNotTouched(installationDirectory, updateDirectory);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_ZipWithoutUpdaterExecutable_RejectsBeforeChangingInstallation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var archivePath = CreateZip(
                updateDirectory,
                ("AssetEditor.CN/AssetEditor.CN.exe", "new executable"));

            Assert.Throws<InvalidDataException>(() =>
                InstallUpdate(archivePath, installationDirectory, owned));

            AssertInstallationWasNotTouched(installationDirectory, updateDirectory);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestCase("UpdateBackup")]
    [TestCase("updatebackup")]
    public void Install_PayloadContainingBackupDirectory_RejectsBeforeChangingInstallation(string backupDirectoryName)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var archivePath = CreateUpdateZip(updateDirectory,
                ($"AssetEditor.CN/{backupDirectoryName}/injected.txt", "injected"));

            Assert.Throws<InvalidDataException>(() =>
                InstallUpdate(archivePath, installationDirectory, owned));

            AssertInstallationWasNotTouched(installationDirectory, updateDirectory);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_DirectoryOutsideArchiveRoot_RejectsBeforeChangingInstallation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var archivePath = CreateZipWithDirectory(updateDirectory, "AnotherRoot/");

            Assert.Throws<InvalidDataException>(() =>
                InstallUpdate(archivePath, installationDirectory, owned));

            AssertInstallationWasNotTouched(installationDirectory, updateDirectory);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_TarRenamedAsZip_RejectsBeforeChangingInstallation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var archivePath = CreateTarWithZipExtension(updateDirectory);

            Assert.That(
                () => InstallUpdate(archivePath, installationDirectory, owned),
                Throws.Exception);

            AssertInstallationWasNotTouched(installationDirectory, updateDirectory);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_UpdateBackupShortNameVariant_CannotReachExternalBackup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var archivePath = CreateUpdateZip(updateDirectory,
                ("AssetEditor.CN/UPDATE~1/injected.txt", "payload file"));

            var backupDirectory = InstallUpdate(
                archivePath,
                installationDirectory,
                owned);

            Assert.Multiple(() =>
            {
                Assert.That(IsWithinDirectory(installationDirectory, backupDirectory), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(backupDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.Exists(Path.Combine(backupDirectory, "injected.txt")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "UPDATE~1", "injected.txt")), Is.EqualTo("payload file"));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_CopyFailure_RestoresOldInstallationAndRemovesPartialFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var archivePath = CreateUpdateZip(
                updateDirectory,
                ("AssetEditor.CN/new-only.txt", "new file"));
            var stagingDirectory = UpdaterWorkspaceFactory
                .GetTransactionPaths(updateDirectory)
                .StagingDirectory;
            var backupRoot = UpdateInstaller.GetBackupRootDirectory(updateDirectory);
            var installFailure = new IOException("controlled install copy failure");
            var rollbackWasAttempted = false;

            void CopyWithInstallFailure(string sourceDirectory, string destinationDirectory)
            {
                if (PathsEqual(sourceDirectory, stagingDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                    File.WriteAllText(Path.Combine(destinationDirectory, "AssetEditor.CN.exe"), "partial executable");
                    File.WriteAllText(Path.Combine(destinationDirectory, "partial-new.txt"), "partial file");
                    throw installFailure;
                }

                if (IsWithinDirectory(backupRoot, sourceDirectory))
                    rollbackWasAttempted = true;

                UpdateInstaller.CopyDirectory(sourceDirectory, destinationDirectory);
            }

            var exception = Assert.Throws<IOException>(() =>
                InstallUpdate(
                    archivePath,
                    installationDirectory,
                    owned,
                    CopyWithInstallFailure));

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.SameAs(installFailure));
                Assert.That(rollbackWasAttempted, Is.True);
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(File.Exists(Path.Combine(installationDirectory, "partial-new.txt")), Is.False);
                Assert.That(File.Exists(Path.Combine(installationDirectory, "new-only.txt")), Is.False);
                Assert.That(Directory.Exists(stagingDirectory), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_BackupIdentityRemainsLiveThroughRollback()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var archivePath = CreateUpdateZip(owned.Workspace.UpdateDirectory);
            var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
                owned.Workspace.UpdateDirectory);
            var movedBackupDirectory = Path.Combine(root, "moved-backup");
            var installFailure = new IOException("controlled install copy failure");
            string? backupDirectory = null;
            Exception? renameFailure = null;

            void CopyWithRenameAttempt(
                string sourceDirectory,
                string destinationDirectory)
            {
                if (PathsEqual(sourceDirectory, installationDirectory))
                {
                    backupDirectory = destinationDirectory;
                    UpdateInstaller.CopyDirectory(
                        sourceDirectory,
                        destinationDirectory);
                    return;
                }

                if (PathsEqual(sourceDirectory, paths.StagingDirectory))
                {
                    renameFailure = Assert.Catch(() =>
                        Directory.Move(
                            backupDirectory!,
                            movedBackupDirectory));
                    throw installFailure;
                }

                UpdateInstaller.CopyDirectory(
                    sourceDirectory,
                    destinationDirectory);
            }

            var actual = Assert.Throws<IOException>(() =>
                InstallUpdate(
                    archivePath,
                    installationDirectory,
                    owned,
                    CopyWithRenameAttempt));

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(installFailure));
                Assert.That(renameFailure, Is.TypeOf<IOException>());
                Assert.That(Directory.Exists(movedBackupDirectory), Is.False);
                Assert.That(Directory.Exists(backupDirectory), Is.True);
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        installationDirectory,
                        "old-only.txt")),
                    Is.EqualTo("old file"));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_CopyAndRollbackFailures_ThrowsBothRootCauses()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            File.WriteAllText(Path.Combine(installationDirectory, "current-only.txt"), "current file");

            var archivePath = CreateUpdateZip(
                updateDirectory,
                ("AssetEditor.CN/next-only.txt", "next file"));
            var stagingDirectory = UpdaterWorkspaceFactory
                .GetTransactionPaths(updateDirectory)
                .StagingDirectory;
            var backupRoot = UpdateInstaller.GetBackupRootDirectory(updateDirectory);
            var installFailure = new IOException("controlled install copy failure");
            var rollbackFailure = new UnauthorizedAccessException("controlled rollback copy failure");

            void CopyWithBothFailures(string sourceDirectory, string destinationDirectory)
            {
                if (PathsEqual(sourceDirectory, stagingDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                    File.WriteAllText(Path.Combine(destinationDirectory, "partial-new.txt"), "partial file");
                    throw installFailure;
                }

                if (IsWithinDirectory(backupRoot, sourceDirectory))
                    throw rollbackFailure;

                UpdateInstaller.CopyDirectory(sourceDirectory, destinationDirectory);
            }

            var exception = Assert.Throws<AggregateException>(() =>
                InstallUpdate(
                    archivePath,
                    installationDirectory,
                    owned,
                    CopyWithBothFailures));

            var backupCandidates = GetBackupCandidates(updateDirectory);
            var failedTransactionBackup = backupCandidates.Single();
            Assert.Multiple(() =>
            {
                Assert.That(exception!.InnerExceptions, Is.EqualTo(new Exception[] { installFailure, rollbackFailure }));
                Assert.That(File.Exists(Path.Combine(installationDirectory, "partial-new.txt")), Is.False);
                Assert.That(backupCandidates, Has.Count.EqualTo(1));
                Assert.That(File.ReadAllText(Path.Combine(failedTransactionBackup, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(File.ReadAllText(Path.Combine(failedTransactionBackup, "current-only.txt")), Is.EqualTo("current file"));
                Assert.That(Directory.Exists(stagingDirectory), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_BackupCopyFailure_LeavesInstallationUntouchedAndKeepsCandidate()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var archivePath = CreateUpdateZip(updateDirectory);
            var backupFailure = new IOException("controlled backup copy failure");

            void CopyWithBackupFailure(string sourceDirectory, string destinationDirectory)
            {
                if (PathsEqual(sourceDirectory, installationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                    File.WriteAllText(Path.Combine(destinationDirectory, "partial-backup.txt"), "partial backup");
                    throw backupFailure;
                }

                UpdateInstaller.CopyDirectory(sourceDirectory, destinationDirectory);
            }

            var exception = Assert.Throws<IOException>(() =>
                InstallUpdate(
                    archivePath,
                    installationDirectory,
                    owned,
                    CopyWithBackupFailure));

            var backupCandidate = GetBackupCandidates(updateDirectory).Single();
            var stagingDirectory = UpdaterWorkspaceFactory
                .GetTransactionPaths(updateDirectory)
                .StagingDirectory;
            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.SameAs(backupFailure));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(File.ReadAllText(Path.Combine(backupCandidate, "partial-backup.txt")), Is.EqualTo("partial backup"));
                Assert.That(Directory.Exists(stagingDirectory), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void IsZipAssetName_AcceptsZipCaseInsensitivelyAndRejectsRar()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UpdaterProgram.IsZipAssetName("AssetEditor.CN.ZIP"), Is.True);
            Assert.That(UpdaterProgram.IsZipAssetName("AssetEditor.CN.rar"), Is.False);
        });
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase(".")]
    [TestCase("..")]
    [TestCase(@"C:\release.zip")]
    [TestCase(@"folder\release.zip")]
    [TestCase("folder/release.zip")]
    [TestCase("release.zip.")]
    [TestCase("release.zip ")]
    [TestCase("invalid|name.zip")]
    [TestCase("release.rar")]
    public void GetAssetDownloadPath_UnsafeAssetLeafIsRejected(string assetName)
    {
        var updateDirectory = Path.Combine(
            Path.GetTempPath(),
            $"asset-download-{Guid.NewGuid():N}",
            "Update");

        Assert.Throws<InvalidDataException>(() =>
            UpdaterProgram.GetAssetDownloadPath(updateDirectory, assetName));
    }

    [Test]
    public void GetAssetDownloadPath_NullAssetLeafIsRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdaterProgram.GetAssetDownloadPath(Path.GetTempPath(), null!));
    }

    [Test]
    public void GetAssetDownloadPath_ValidZipLeafStaysDirectlyInsideUpdate()
    {
        var updateDirectory = Path.Combine(
            Path.GetTempPath(),
            $"asset-download-{Guid.NewGuid():N}",
            "Update");

        var assetPath = UpdaterProgram.GetAssetDownloadPath(
            updateDirectory,
            "AssetEditor.CN-1.2.3.ZIP");

        Assert.That(
            assetPath,
            Is.EqualTo(Path.Combine(
                Path.GetFullPath(updateDirectory),
                "AssetEditor.CN-1.2.3.ZIP")));
    }

    [Test]
    public async Task CopyAssetDownloadAsync_PreExistingArchiveIsNotOverwritten()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = owned.Workspace.UpdateDirectory;
            var assetPath = UpdaterProgram.GetAssetDownloadPath(
                updateDirectory,
                "AssetEditor.CN.zip");
            File.WriteAllText(assetPath, "preserve");
            await using var source = new MemoryStream("replacement"u8.ToArray());

            Assert.ThrowsAsync<IOException>(async () =>
                await UpdaterProgram.CopyAssetDownloadAsync(
                    source,
                    assetPath,
                    installationDirectory,
                    owned.Workspace,
                    owned.LocalRoot,
                    owned.CommonRoot));

            Assert.That(File.ReadAllText(assetPath), Is.EqualTo("preserve"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CopyAssetDownloadAsync_CopyFailureDeletesCreatedArchiveAndPreservesPrimary()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var assetPath = UpdaterProgram.GetAssetDownloadPath(
                owned.Workspace.UpdateDirectory,
                "AssetEditor.CN.zip");
            using var source = new MemoryStream("partial"u8.ToArray());
            var copyFailure = new IOException("controlled download copy failure");
            var previousError = Console.Error;
            using var cleanupLog = new StringWriter();
            IOException? actual;
            try
            {
                Console.SetError(cleanupLog);
                actual = Assert.ThrowsAsync<IOException>(async () =>
                    await UpdaterProgram.CopyAssetDownloadAsync(
                        source,
                        assetPath,
                        installationDirectory,
                        owned.Workspace,
                        owned.LocalRoot,
                        owned.CommonRoot,
                        () => throw copyFailure));
            }
            finally
            {
                Console.SetError(previousError);
            }

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(copyFailure));
                Assert.That(File.Exists(assetPath), Is.False);
                Assert.That(cleanupLog.ToString(), Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task CopyAssetDownloadAsync_HoldsUpdateIdentityAcrossCopy()
    {
        var root = CreateTemporaryDirectory();
        var movedUpdateDirectory = Path.Combine(root, "moved-update");
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var assetPath = UpdaterProgram.GetAssetDownloadPath(
                owned.Workspace.UpdateDirectory,
                "AssetEditor.CN.zip");
            await using var source = new GatedReadStream("archive"u8.ToArray());

            var copyTask = UpdaterProgram.CopyAssetDownloadAsync(
                source,
                assetPath,
                installationDirectory,
                owned.Workspace,
                owned.LocalRoot,
                owned.CommonRoot);
            await source.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Throws<IOException>(() =>
                Directory.Move(
                    owned.Workspace.UpdateDirectory,
                    movedUpdateDirectory));

            source.Release();
            await copyTask.WaitAsync(TimeSpan.FromSeconds(2));

            Directory.Move(
                owned.Workspace.UpdateDirectory,
                movedUpdateDirectory);
            Directory.Move(
                movedUpdateDirectory,
                owned.Workspace.UpdateDirectory);
            Assert.That(File.ReadAllText(assetPath), Is.EqualTo("archive"));
        }
        finally
        {
            if (Directory.Exists(movedUpdateDirectory))
                Directory.Move(
                    movedUpdateDirectory,
                    Path.Combine(
                        root,
                        "local",
                        "AssetEditor.CN",
                        "Temp",
                        "Update"));
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void UpdateDirectories_NonElevatedLayoutMatchesMainApplication()
    {
        var tempRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AssetEditor.CN",
            "Temp");
        var layout = UpdaterWorkspaceFactory.GetLayout(false, Guid.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(layout.TransactionRoot, Is.EqualTo(tempRoot));
            Assert.That(layout.UpdateDirectory, Is.EqualTo(Path.Combine(tempRoot, "Update")));
            Assert.That(layout.IsProtected, Is.False);
            Assert.That(UpdaterProgram.GetUpdateDirectory(), Is.EqualTo(layout.UpdateDirectory));
            Assert.That(UpdaterProgram.GetUpdateBackupRootDirectory(), Is.EqualTo(Path.Combine(tempRoot, "UpdateBackups")));
            Assert.That(layout.UpdateDirectory, Is.EqualTo(DirectoryHelper.UpdateDirectory));
            Assert.That(UpdaterProgram.GetUpdateBackupRootDirectory(), Is.EqualTo(DirectoryHelper.UpdateBackupRootDirectory));
        });
    }

    [TestCase("update-inside-installation")]
    [TestCase("installation-inside-update")]
    [TestCase("installation-inside-backup")]
    public void Install_OverlappingTransactionDirectories_RejectsBeforeChangingInstallation(string layout)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            OwnedLocalWorkspace owned;
            string installationDirectory;
            switch (layout)
            {
                case "update-inside-installation":
                    installationDirectory = CreateInstallationAt(Path.Combine(root, "installation"));
                    owned = CreateOwnedLocalWorkspace(
                        root,
                        Path.Combine(installationDirectory, "local"));
                    break;
                case "installation-inside-update":
                    owned = CreateOwnedLocalWorkspace(root);
                    installationDirectory = CreateInstallationAt(
                        Path.Combine(owned.Workspace.UpdateDirectory, "installation"));
                    break;
                case "installation-inside-backup":
                    owned = CreateOwnedLocalWorkspace(root);
                    var backupRoot = Directory.CreateDirectory(
                        UpdateInstaller.GetBackupRootDirectory(
                            owned.Workspace.UpdateDirectory)).FullName;
                    installationDirectory = CreateInstallationAt(Path.Combine(backupRoot, "installation"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(layout));
            }

            var archivePath = CreateUpdateZip(owned.Workspace.UpdateDirectory);
            var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
                owned.Workspace.UpdateDirectory);

            Assert.Throws<InvalidOperationException>(() =>
                InstallUpdate(archivePath, installationDirectory, owned));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(Directory.Exists(paths.StagingDirectory), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_LocalOwnedPreExistingStagingIsRecovered()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var stagingDirectory = Directory.CreateDirectory(
                UpdaterWorkspaceFactory
                    .GetTransactionPaths(owned.Workspace.UpdateDirectory)
                    .StagingDirectory).FullName;
            var userDataPath = Path.Combine(stagingDirectory, "user-data.txt");
            File.WriteAllText(userDataPath, "do not delete");
            var archivePath = CreateUpdateZip(owned.Workspace.UpdateDirectory);

            InstallUpdate(archivePath, installationDirectory, owned);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(userDataPath), Is.False);
                Assert.That(Directory.Exists(stagingDirectory), Is.False);
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        installationDirectory,
                        "AssetEditor.CN.exe")),
                    Is.EqualTo("new executable"));
                Assert.That(
                    File.Exists(Path.Combine(installationDirectory, "old-only.txt")),
                    Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CopyUpdaterPayload_CopiesNestedFilesAndOnlyRemovesOldUpdatePayload()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var installationDirectory = Directory.CreateDirectory(Path.Combine(root, "installation")).FullName;
            var payloadDirectory = Directory.CreateDirectory(Path.Combine(installationDirectory, "Updater")).FullName;
            Directory.CreateDirectory(Path.Combine(payloadDirectory, "runtimes", "win-x64"));
            File.WriteAllText(Path.Combine(payloadDirectory, "AssetEditor.CN.Updater.exe"), "updater");
            File.WriteAllText(Path.Combine(payloadDirectory, "runtimes", "win-x64", "native.dll"), "dependency");

            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var workspace = UpdaterWorkspaceFactory.Create(
                UpdaterWorkspaceFactory.GetLayout(
                    false,
                    Guid.Empty,
                    localRoot,
                    commonRoot));
            var updateDirectory = workspace.UpdateDirectory;
            File.WriteAllText(Path.Combine(updateDirectory, "obsolete.dll"), "old payload");
            var backupRoot = Directory.CreateDirectory(
                UpdateInstaller.GetBackupRootDirectory(updateDirectory)).FullName;
            var recoveryPath = Path.Combine(backupRoot, "existing-recovery.txt");
            File.WriteAllText(recoveryPath, "keep recovery");

            UpdaterProgram.CopyUpdaterPayload(
                payloadDirectory,
                workspace,
                installationDirectory,
                localRoot,
                commonRoot);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(updateDirectory, "AssetEditor.CN.Updater.exe")), Is.EqualTo("updater"));
                Assert.That(File.ReadAllText(Path.Combine(updateDirectory, "runtimes", "win-x64", "native.dll")), Is.EqualTo("dependency"));
                Assert.That(File.Exists(Path.Combine(updateDirectory, "obsolete.dll")), Is.False);
                Assert.That(File.ReadAllText(recoveryPath), Is.EqualTo("keep recovery"));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CopyUpdaterPayload_OverlapRejectsBeforeDeletingInstallation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var installationDirectory = CreateInstallation(root);
            var payloadDirectory = Directory.CreateDirectory(
                Path.Combine(installationDirectory, "Updater")).FullName;
            File.WriteAllText(Path.Combine(payloadDirectory, "AssetEditor.CN.Updater.exe"), "updater");
            var localRoot = Directory.CreateDirectory(
                Path.Combine(installationDirectory, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var workspace = UpdaterWorkspaceFactory.Create(
                UpdaterWorkspaceFactory.GetLayout(
                    false,
                    Guid.Empty,
                    localRoot,
                    commonRoot));

            Assert.Throws<InvalidOperationException>(() =>
                UpdaterProgram.CopyUpdaterPayload(
                    payloadDirectory,
                    workspace,
                    installationDirectory,
                    localRoot,
                    commonRoot));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(File.Exists(Path.Combine(payloadDirectory, "AssetEditor.CN.Updater.exe")), Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_DoesNotDeleteOtherGuidTransaction()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var siblingTransaction = Directory.CreateDirectory(
                Path.Combine(
                    Path.GetDirectoryName(owned.Workspace.TransactionRoot)!,
                    Guid.NewGuid().ToString("N"))).FullName;
            var siblingSentinel = Path.Combine(
                siblingTransaction,
                "recovery-evidence.txt");
            File.WriteAllText(siblingSentinel, "preserve");
            var installationDirectory = CreateInstallation(root);
            var archivePath = CreateUpdateZip(owned.Workspace.UpdateDirectory);

            InstallUpdate(archivePath, installationDirectory, owned);

            Assert.That(
                File.ReadAllText(siblingSentinel),
                Is.EqualTo("preserve"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_InstallationTreeReparseIsRejectedBeforeBackupOrClear()
    {
        var root = CreateTemporaryDirectory();
        var linkedPath = Path.Combine(root, "installation", "linked");
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var externalDirectory = Directory.CreateDirectory(
                Path.Combine(root, "external")).FullName;
            var externalSentinel = Path.Combine(externalDirectory, "preserve.txt");
            File.WriteAllText(externalSentinel, "preserve");
            TryCreateDirectorySymbolicLinkOrIgnore(linkedPath, externalDirectory);
            var archivePath = CreateUpdateZip(owned.Workspace.UpdateDirectory);
            var copyCount = 0;

            Assert.Throws<InvalidDataException>(() =>
                InstallUpdate(
                    archivePath,
                    installationDirectory,
                    owned,
                    (_, _) => copyCount++));

            Assert.Multiple(() =>
            {
                Assert.That(copyCount, Is.Zero);
                Assert.That(
                    File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")),
                    Is.EqualTo("old file"));
                Assert.That(File.ReadAllText(externalSentinel), Is.EqualTo("preserve"));
            });
        }
        finally
        {
            DeleteDirectoryLinkIfPresent(linkedPath);
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_RevalidatesMarkerAfterBackupBeforeClearingInstallation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var archivePath = CreateUpdateZip(owned.Workspace.UpdateDirectory);
            var markerPath = UpdaterWorkspaceFactory
                .GetTransactionPaths(owned.Workspace.UpdateDirectory)
                .MarkerPath;

            void CopyAndCorruptMarker(
                string sourceDirectory,
                string destinationDirectory)
            {
                UpdateInstaller.CopyDirectory(sourceDirectory, destinationDirectory);
                if (PathsEqual(sourceDirectory, installationDirectory))
                    File.WriteAllText(markerPath, "wrong marker");
            }

            Assert.Throws<InvalidDataException>(() =>
                InstallUpdate(
                    archivePath,
                    installationDirectory,
                    owned,
                    CopyAndCorruptMarker));

            Assert.Multiple(() =>
            {
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        installationDirectory,
                        "AssetEditor.CN.exe")),
                    Is.EqualTo("old executable"));
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        installationDirectory,
                        "old-only.txt")),
                    Is.EqualTo("old file"));
                Assert.That(GetBackupCandidates(owned.Workspace.UpdateDirectory), Has.Count.EqualTo(1));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_StagingLeaseRejectsReplacementAndPreservesPrimaryFailure()
    {
        var root = CreateTemporaryDirectory();
        string? stagingDirectory = null;
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var archivePath = CreateUpdateZip(owned.Workspace.UpdateDirectory);
            var extractionFailure = new InvalidDataException(
                "controlled extraction failure");
            Exception? replacementFailure = null;

            void TryReplaceStagingAndFail(string createdStagingDirectory)
            {
                stagingDirectory = createdStagingDirectory;
                replacementFailure = Assert.Catch(() =>
                    Directory.Delete(createdStagingDirectory));
                throw extractionFailure;
            }

            var previousError = Console.Error;
            using var cleanupLog = new StringWriter();
            InvalidDataException? actual;
            try
            {
                Console.SetError(cleanupLog);
                actual = Assert.Throws<InvalidDataException>(() =>
                    UpdateInstaller.Install(
                        archivePath,
                        installationDirectory,
                        owned.Workspace,
                        UpdateInstaller.CopyDirectory,
                        owned.LocalRoot,
                        owned.CommonRoot,
                        TryReplaceStagingAndFail));
            }
            finally
            {
                Console.SetError(previousError);
            }

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(extractionFailure));
                Assert.That(
                    cleanupLog.ToString(),
                    Is.Empty);
                Assert.That(replacementFailure, Is.TypeOf<IOException>());
                Assert.That(Directory.Exists(stagingDirectory), Is.False);
                Assert.That(
                    File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")),
                    Is.EqualTo("old file"));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_ArchiveLeaseRejectsReplacementDuringExtraction()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var archivePath = CreateUpdateZip(
                owned.Workspace.UpdateDirectory);
            var movedArchivePath = Path.Combine(root, "moved-update.zip");
            var extractionFailure = new InvalidDataException(
                "controlled extraction failure");
            Exception? moveFailure = null;

            void TryMoveArchiveAndFail(string _)
            {
                moveFailure = Assert.Catch(() =>
                    File.Move(archivePath, movedArchivePath));
                throw extractionFailure;
            }

            var actual = Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.Install(
                    archivePath,
                    installationDirectory,
                    owned.Workspace,
                    UpdateInstaller.CopyDirectory,
                    owned.LocalRoot,
                    owned.CommonRoot,
                    TryMoveArchiveAndFail));

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(extractionFailure));
                Assert.That(moveFailure, Is.TypeOf<IOException>());
                Assert.That(File.Exists(archivePath), Is.True);
                Assert.That(File.Exists(movedArchivePath), Is.False);
                Assert.That(
                    Directory.Exists(
                        UpdaterWorkspaceFactory
                            .GetTransactionPaths(
                                owned.Workspace.UpdateDirectory)
                            .StagingDirectory),
                    Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CopyUpdaterPayload_SourceInsideLegacyDestinationIsRejectedBeforeCleanup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var installationDirectory = CreateInstallation(root);
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var workspace = UpdaterWorkspaceFactory.Create(
                UpdaterWorkspaceFactory.GetLayout(
                    false,
                    Guid.Empty,
                    localRoot,
                    commonRoot));
            var payloadDirectory = Directory.CreateDirectory(
                Path.Combine(workspace.UpdateDirectory, "payload")).FullName;
            var updaterPath = Path.Combine(payloadDirectory, "AssetEditor.CN.Updater.exe");
            File.WriteAllText(updaterPath, "updater");

            Assert.Throws<InvalidOperationException>(() =>
                UpdaterProgram.CopyUpdaterPayload(
                    payloadDirectory,
                    workspace,
                    installationDirectory,
                    localRoot,
                    commonRoot));

            Assert.That(File.ReadAllText(updaterPath), Is.EqualTo("updater"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CopyUpdaterPayload_SourceRootLinkToLegacyDestinationIsRejectedBeforeCleanup()
    {
        var root = CreateTemporaryDirectory();
        var sourceLink = Path.Combine(root, "source-link");
        try
        {
            var installationDirectory = CreateInstallation(root);
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var workspace = UpdaterWorkspaceFactory.Create(
                UpdaterWorkspaceFactory.GetLayout(
                    false,
                    Guid.Empty,
                    localRoot,
                    commonRoot));
            var sentinelPath = Path.Combine(workspace.UpdateDirectory, "AssetEditor.CN.Updater.exe");
            File.WriteAllText(sentinelPath, "preserve");
            TryCreateDirectorySymbolicLinkOrIgnore(sourceLink, workspace.UpdateDirectory);

            Assert.Throws<InvalidDataException>(() =>
                UpdaterProgram.CopyUpdaterPayload(
                    sourceLink,
                    workspace,
                    installationDirectory,
                    localRoot,
                    commonRoot));

            Assert.That(File.ReadAllText(sentinelPath), Is.EqualTo("preserve"));
        }
        finally
        {
            DeleteDirectoryLinkIfPresent(sourceLink);
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CopyUpdaterPayload_SourceAncestorJunctionAliasIsRejectedBeforeCleanup()
    {
        var root = CreateTemporaryDirectory();
        var aliasParent = Path.Combine(root, "transaction-alias");
        try
        {
            var installationDirectory = CreateInstallation(root);
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var workspace = UpdaterWorkspaceFactory.Create(
                UpdaterWorkspaceFactory.GetLayout(
                    false,
                    Guid.Empty,
                    localRoot,
                    commonRoot));
            var sentinelPath = Path.Combine(workspace.UpdateDirectory, "AssetEditor.CN.Updater.exe");
            File.WriteAllText(sentinelPath, "preserve");
            TryCreateJunctionOrIgnore(aliasParent, workspace.TransactionRoot);
            var aliasedSource = Path.Combine(aliasParent, "Update");

            Assert.Throws<InvalidDataException>(() =>
                UpdaterProgram.CopyUpdaterPayload(
                    aliasedSource,
                    workspace,
                    installationDirectory,
                    localRoot,
                    commonRoot));

            Assert.That(File.ReadAllText(sentinelPath), Is.EqualTo("preserve"));
        }
        finally
        {
            DeleteDirectoryLinkIfPresent(aliasParent);
            Directory.Delete(root, true);
        }
    }

    [TestCase("transaction")]
    [TestCase("update")]
    public void CopyUpdaterPayload_InstallationPhysicalAliasOfWorkspaceIsRejectedBeforeCleanup(
        string aliasedDirectory)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var payloadDirectory = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
            File.WriteAllText(Path.Combine(payloadDirectory, "AssetEditor.CN.Updater.exe"), "updater");
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var workspace = UpdaterWorkspaceFactory.Create(
                UpdaterWorkspaceFactory.GetLayout(
                    false,
                    Guid.Empty,
                    localRoot,
                    commonRoot));
            var sentinelPath = Path.Combine(workspace.UpdateDirectory, "preserve.txt");
            File.WriteAllText(sentinelPath, "preserve");
            var physicalInstallationDirectory = aliasedDirectory == "transaction"
                ? workspace.TransactionRoot
                : workspace.UpdateDirectory;
            var installationAlias = ToExtendedDosPath(physicalInstallationDirectory);

            Assert.Throws<InvalidOperationException>(() =>
                UpdaterProgram.CopyUpdaterPayload(
                    payloadDirectory,
                    workspace,
                    installationAlias,
                    localRoot,
                    commonRoot));

            Assert.That(File.ReadAllText(sentinelPath), Is.EqualTo("preserve"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CopyUpdaterPayload_ProtectedWorkspaceRejectsNonEmptyUpdateWithoutDeletingIt()
    {
        if (!UpdaterWorkspaceFactory.IsProcessElevated())
            Assert.Ignore("Protected workspace copy requires an elevated Windows token.");

        var root = CreateTemporaryDirectory();
        try
        {
            var installationDirectory = CreateInstallation(root);
            var payloadDirectory = Directory.CreateDirectory(
                Path.Combine(installationDirectory, "Updater")).FullName;
            File.WriteAllText(Path.Combine(payloadDirectory, "AssetEditor.CN.Updater.exe"), "updater");
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var workspace = UpdaterWorkspaceFactory.Create(
                UpdaterWorkspaceFactory.GetLayout(
                    true,
                    Guid.NewGuid(),
                    localRoot,
                    commonRoot));
            var sentinelPath = Path.Combine(workspace.UpdateDirectory, "preserve.txt");
            File.WriteAllText(sentinelPath, "preserve");

            Assert.Throws<InvalidOperationException>(() =>
                UpdaterProgram.CopyUpdaterPayload(
                    payloadDirectory,
                    workspace,
                    installationDirectory,
                    localRoot,
                    commonRoot));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(sentinelPath), Is.EqualTo("preserve"));
                Assert.DoesNotThrow(() =>
                    UpdaterWorkspaceSecurity.ValidateProtectedDirectory(workspace.UpdateDirectory));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CopyUpdaterPayload_ProtectedWorkspacePreservesExactUpdateAcl()
    {
        if (!UpdaterWorkspaceFactory.IsProcessElevated())
            Assert.Ignore("Protected workspace copy requires an elevated Windows token.");

        var root = CreateTemporaryDirectory();
        try
        {
            var installationDirectory = CreateInstallation(root);
            var payloadDirectory = Directory.CreateDirectory(
                Path.Combine(installationDirectory, "Updater")).FullName;
            File.WriteAllText(Path.Combine(payloadDirectory, "AssetEditor.CN.Updater.exe"), "updater");
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var workspace = UpdaterWorkspaceFactory.Create(
                UpdaterWorkspaceFactory.GetLayout(
                    true,
                    Guid.NewGuid(),
                    localRoot,
                    commonRoot));

            UpdaterProgram.CopyUpdaterPayload(
                payloadDirectory,
                workspace,
                installationDirectory,
                localRoot,
                commonRoot);

            Assert.Multiple(() =>
            {
                Assert.That(
                    File.ReadAllText(
                        Path.Combine(workspace.UpdateDirectory, "AssetEditor.CN.Updater.exe")),
                    Is.EqualTo("updater"));
                Assert.DoesNotThrow(() =>
                    UpdaterWorkspaceSecurity.ValidateProtectedDirectory(workspace.UpdateDirectory));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void RestoreBackup_ReplacesPartialInstallationWithOldFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = Directory.CreateDirectory(Path.Combine(root, "installation")).FullName;
            var backupDirectory = Directory.CreateDirectory(
                Path.Combine(
                    UpdateInstaller.GetBackupRootDirectory(
                        owned.Workspace.UpdateDirectory),
                    $"backup-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}")).FullName;
            File.WriteAllText(Path.Combine(backupDirectory, "AssetEditor.CN.exe"), "old executable");
            File.WriteAllText(Path.Combine(backupDirectory, "old-only.txt"), "old file");
            File.WriteAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe"), "partial executable");
            File.WriteAllText(Path.Combine(installationDirectory, "partial-new.txt"), "partial file");

            UpdateInstaller.RestoreBackup(
                installationDirectory,
                owned.Workspace,
                backupDirectory,
                UpdateInstaller.CopyDirectory,
                owned.LocalRoot,
                owned.CommonRoot);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(File.Exists(Path.Combine(installationDirectory, "partial-new.txt")), Is.False);
                Assert.That(Directory.Exists(backupDirectory), Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        return Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"AssetEditorUpdaterTests-{Guid.NewGuid():N}")).FullName;
    }

    private static string CreateInstallation(string root)
    {
        return CreateInstallationAt(Path.Combine(root, "installation"));
    }

    private static string CreateInstallationAt(string path)
    {
        var installationDirectory = Directory.CreateDirectory(path).FullName;
        File.WriteAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe"), "old executable");
        File.WriteAllText(Path.Combine(installationDirectory, "old-only.txt"), "old file");
        return installationDirectory;
    }

    private static OwnedLocalWorkspace CreateOwnedLocalWorkspace(
        string root,
        string? localRoot = null)
    {
        localRoot ??= Path.Combine(root, "local");
        var commonRoot = Path.Combine(root, "common");
        var workspace = UpdaterWorkspaceFactory.Create(
            UpdaterWorkspaceFactory.GetLayout(
                false,
                Guid.Empty,
                localRoot,
                commonRoot));
        return new OwnedLocalWorkspace(localRoot, commonRoot, workspace);
    }

    private static string InstallUpdate(
        string archivePath,
        string installationDirectory,
        OwnedLocalWorkspace owned,
        Action<string, string>? copyDirectory = null)
    {
        return UpdateInstaller.Install(
            archivePath,
            installationDirectory,
            owned.Workspace,
            copyDirectory ?? UpdateInstaller.CopyDirectory,
            owned.LocalRoot,
            owned.CommonRoot);
    }

    private static string CreateZip(string root, params (string Path, string Contents)[] entries)
    {
        var archivePath = Path.Combine(root, $"update-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var (path, contents) in entries)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(contents);
        }

        return archivePath;
    }

    private static string CreateUpdateZip(string root, params (string Path, string Contents)[] entries)
    {
        var allEntries = new List<(string Path, string Contents)>
        {
            ("AssetEditor.CN/AssetEditor.CN.exe", "new executable"),
            ("AssetEditor.CN/Updater/AssetEditor.CN.Updater.exe", "new updater")
        };
        allEntries.AddRange(entries);
        return CreateZip(root, allEntries.ToArray());
    }

    private static string CreateZipWithDirectory(string root, string directoryPath)
    {
        var archivePath = Path.Combine(root, $"update-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        archive.CreateEntry(directoryPath);

        var executableEntry = archive.CreateEntry("AssetEditor.CN/AssetEditor.CN.exe");
        using (var writer = new StreamWriter(executableEntry.Open()))
            writer.Write("new executable");

        var updaterEntry = archive.CreateEntry("AssetEditor.CN/Updater/AssetEditor.CN.Updater.exe");
        using (var writer = new StreamWriter(updaterEntry.Open()))
            writer.Write("new updater");
        return archivePath;
    }

    private static string CreateTarWithZipExtension(string root)
    {
        var payloadRoot = Directory.CreateDirectory(Path.Combine(root, "tar-payload")).FullName;
        var archiveRoot = Directory.CreateDirectory(Path.Combine(payloadRoot, "AssetEditor.CN")).FullName;
        File.WriteAllText(Path.Combine(archiveRoot, "AssetEditor.CN.exe"), "new executable");
        var updaterRoot = Directory.CreateDirectory(Path.Combine(archiveRoot, "Updater")).FullName;
        File.WriteAllText(Path.Combine(updaterRoot, "AssetEditor.CN.Updater.exe"), "new updater");

        var archivePath = Path.Combine(root, $"update-{Guid.NewGuid():N}.zip");
        TarFile.CreateFromDirectory(payloadRoot, archivePath, false);
        return archivePath;
    }

    private static void AssertInstallationWasNotTouched(string installationDirectory, string updateDirectory)
    {
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
            Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
            Assert.That(Directory.Exists(UpdateInstaller.GetBackupRootDirectory(updateDirectory)), Is.False);
        });
    }

    private static List<string> GetBackupCandidates(string updateDirectory)
    {
        var backupRoot = UpdateInstaller.GetBackupRootDirectory(updateDirectory);
        return Directory.Exists(backupRoot)
            ? Directory.EnumerateDirectories(backupRoot).ToList()
            : [];
    }

    private static bool PathsEqual(string firstPath, string secondPath)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstPath)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondPath)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithinDirectory(string parentDirectory, string candidatePath)
    {
        var parentRoot = Path.EndsInDirectorySeparator(parentDirectory)
            ? Path.GetFullPath(parentDirectory)
            : Path.GetFullPath(parentDirectory) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(parentRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryCreateDirectorySymbolicLinkOrIgnore(string path, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(path, targetPath);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("Creating directory symbolic links is not permitted on this machine.");
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("Directory symbolic links are not supported on this machine.");
        }
    }

    [TestCase("backup-test")]
    [TestCase("backup-20260727010203004-00000000000000000000000000000000")]
    [TestCase("backup-20260727010203004-0123456789ABCDEF0123456789ABCDEF")]
    public void RestoreBackup_InvalidCandidateNameIsRejectedBeforeClear(
        string candidateName)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var backupDirectory = Directory.CreateDirectory(
                Path.Combine(
                    UpdateInstaller.GetBackupRootDirectory(
                        owned.Workspace.UpdateDirectory),
                    candidateName)).FullName;
            File.WriteAllText(
                Path.Combine(backupDirectory, "AssetEditor.CN.exe"),
                "old executable");
            var copyCount = 0;

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.RestoreBackup(
                    installationDirectory,
                    owned.Workspace,
                    backupDirectory,
                    (_, _) => copyCount++,
                    owned.LocalRoot,
                    owned.CommonRoot));

            Assert.Multiple(() =>
            {
                Assert.That(copyCount, Is.Zero);
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        installationDirectory,
                        "old-only.txt")),
                    Is.EqualTo("old file"));
                Assert.That(Directory.Exists(backupDirectory), Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void RestoreBackup_ReparseCandidateIsRejectedBeforeClear()
    {
        var root = CreateTemporaryDirectory();
        string? backupLink = null;
        try
        {
            var owned = CreateOwnedLocalWorkspace(root);
            var installationDirectory = CreateInstallation(root);
            var externalBackup = Directory.CreateDirectory(
                Path.Combine(root, "external-backup")).FullName;
            var externalSentinel = Path.Combine(externalBackup, "preserve.txt");
            File.WriteAllText(externalSentinel, "preserve");
            var backupRoot = Directory.CreateDirectory(
                UpdateInstaller.GetBackupRootDirectory(
                    owned.Workspace.UpdateDirectory)).FullName;
            backupLink = Path.Combine(
                backupRoot,
                $"backup-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
            TryCreateDirectorySymbolicLinkOrIgnore(backupLink, externalBackup);
            var copyCount = 0;

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.RestoreBackup(
                    installationDirectory,
                    owned.Workspace,
                    backupLink,
                    (_, _) => copyCount++,
                    owned.LocalRoot,
                    owned.CommonRoot));

            Assert.Multiple(() =>
            {
                Assert.That(copyCount, Is.Zero);
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        installationDirectory,
                        "old-only.txt")),
                    Is.EqualTo("old file"));
                Assert.That(File.ReadAllText(externalSentinel), Is.EqualTo("preserve"));
            });
        }
        finally
        {
            if (backupLink != null)
                DeleteDirectoryLinkIfPresent(backupLink);
            Directory.Delete(root, true);
        }
    }

    private static void TryCreateJunctionOrIgnore(string path, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start junction creation.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Assert.Ignore(
                $"Creating directory junctions is not permitted on this machine: {process.StandardError.ReadToEnd()}");
        }
    }

    private static void DeleteDirectoryLinkIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path);
    }

    private static string ToExtendedDosPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + fullPath[2..]
            : @"\\?\" + fullPath;
    }

    private sealed record OwnedLocalWorkspace(
        string LocalRoot,
        string CommonRoot,
        UpdaterWorkspace Workspace);

    private sealed class GatedReadStream(byte[] contents) : Stream
    {
        private readonly MemoryStream inner = new(contents);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool hasWaited;

        internal TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Release()
        {
            release.TrySetResult();
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return inner.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!hasWaited)
            {
                hasWaited = true;
                ReadStarted.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return await inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
