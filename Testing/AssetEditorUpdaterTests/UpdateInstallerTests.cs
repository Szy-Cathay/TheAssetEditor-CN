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
    public void Install_ValidZip_ReplacesInstallationAndKeepsExternalBackup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateUpdateZip(root,
                (@"AssetEditor.CN\data\config.json", "new config"));

            var backupDirectory = UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory);
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
                Assert.That(File.Exists(Path.Combine(backupRoot, UpdateInstaller.BackupRootMarkerFileName)), Is.True);
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
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateUpdateZip(root, (unsafeEntry, "unsafe"));

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory));

            AssertInstallationWasNotTouched(installationDirectory, updateDirectory);
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
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateZip(root, ("AssetEditor.CN/readme.txt", "missing executable"));

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory));

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
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateZip(root, ("AssetEditor.CN/AssetEditor.CN.exe", "new executable"));

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory));

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
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateUpdateZip(root,
                ($"AssetEditor.CN/{backupDirectoryName}/injected.txt", "injected"));

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory));

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
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateZipWithDirectory(root, "AnotherRoot/");

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory));

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
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateTarWithZipExtension(root);

            Assert.That(
                () => UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory),
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
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateUpdateZip(root,
                ("AssetEditor.CN/UPDATE~1/injected.txt", "payload file"));

            var backupDirectory = UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory);

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
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateUpdateZip(root, ("AssetEditor.CN/new-only.txt", "new file"));
            var stagingDirectory = Path.Combine(updateDirectory, "staging");
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
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory, CopyWithInstallFailure));

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.SameAs(installFailure));
                Assert.That(rollbackWasAttempted, Is.True);
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(File.Exists(Path.Combine(installationDirectory, "partial-new.txt")), Is.False);
                Assert.That(File.Exists(Path.Combine(installationDirectory, "new-only.txt")), Is.False);
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
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var firstArchivePath = CreateUpdateZip(root);
            var existingBackup = UpdateInstaller.Install(firstArchivePath, installationDirectory, updateDirectory);
            File.WriteAllText(Path.Combine(installationDirectory, "current-only.txt"), "current file");

            var archivePath = CreateUpdateZip(root, ("AssetEditor.CN/next-only.txt", "next file"));
            var stagingDirectory = Path.Combine(updateDirectory, "staging");
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
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory, CopyWithBothFailures));

            var backupCandidates = GetBackupCandidates(updateDirectory);
            var failedTransactionBackup = backupCandidates.Single(path => !PathsEqual(path, existingBackup));
            Assert.Multiple(() =>
            {
                Assert.That(exception!.InnerExceptions, Is.EqualTo(new Exception[] { installFailure, rollbackFailure }));
                Assert.That(File.Exists(Path.Combine(installationDirectory, "partial-new.txt")), Is.False);
                Assert.That(backupCandidates, Has.Count.EqualTo(2));
                Assert.That(File.ReadAllText(Path.Combine(existingBackup, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(File.ReadAllText(Path.Combine(failedTransactionBackup, "current-only.txt")), Is.EqualTo("current file"));
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
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateUpdateZip(root);
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
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory, CopyWithBackupFailure));

            var backupCandidate = GetBackupCandidates(updateDirectory).Single();
            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.SameAs(backupFailure));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(File.ReadAllText(Path.Combine(backupCandidate, "partial-backup.txt")), Is.EqualTo("partial backup"));
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
            var transactionRoot = Path.Combine(root, "transactions");
            string installationDirectory;
            string updateDirectory;
            switch (layout)
            {
                case "update-inside-installation":
                    installationDirectory = CreateInstallationAt(Path.Combine(root, "installation"));
                    updateDirectory = Directory.CreateDirectory(Path.Combine(installationDirectory, "Update")).FullName;
                    break;
                case "installation-inside-update":
                    updateDirectory = Directory.CreateDirectory(Path.Combine(transactionRoot, "Update")).FullName;
                    installationDirectory = CreateInstallationAt(Path.Combine(updateDirectory, "installation"));
                    break;
                case "installation-inside-backup":
                    updateDirectory = Directory.CreateDirectory(Path.Combine(transactionRoot, "Update")).FullName;
                    var backupRoot = Directory.CreateDirectory(
                        UpdateInstaller.GetBackupRootDirectory(updateDirectory)).FullName;
                    installationDirectory = CreateInstallationAt(Path.Combine(backupRoot, "installation"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(layout));
            }

            var archiveRoot = Directory.CreateDirectory(Path.Combine(root, "archives")).FullName;
            var archivePath = CreateUpdateZip(archiveRoot);

            Assert.Throws<InvalidOperationException>(() =>
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
                Assert.That(Directory.Exists(Path.Combine(updateDirectory, "staging")), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_UnownedBackupRoot_RejectsWithoutDeletingUserData()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var backupRoot = Directory.CreateDirectory(
                UpdateInstaller.GetBackupRootDirectory(updateDirectory)).FullName;
            var userDataPath = Path.Combine(backupRoot, "user-data.txt");
            File.WriteAllText(userDataPath, "do not delete");
            var archivePath = CreateUpdateZip(root);

            Assert.Throws<InvalidOperationException>(() =>
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(userDataPath), Is.EqualTo("do not delete"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
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
            var installationDirectory = Directory.CreateDirectory(Path.Combine(root, "installation")).FullName;
            var backupDirectory = Directory.CreateDirectory(
                Path.Combine(root, "UpdateBackups", "backup-test")).FullName;
            File.WriteAllText(Path.Combine(backupDirectory, "AssetEditor.CN.exe"), "old executable");
            File.WriteAllText(Path.Combine(backupDirectory, "old-only.txt"), "old file");
            File.WriteAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe"), "partial executable");
            File.WriteAllText(Path.Combine(installationDirectory, "partial-new.txt"), "partial file");

            UpdateInstaller.RestoreBackup(installationDirectory, backupDirectory);

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
}
