using System.IO.Compression;
using System.Formats.Tar;
using AssetEditorUpdater;
using UpdaterProgram = AssetEditorUpdater.AssetEditorUpdater;

namespace AssetEditorUpdaterTests;

public class UpdateInstallerTests
{
    [Test]
    public void Install_ValidZip_ReplacesInstallationAndKeepsBackup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateZip(root,
                ("AssetEditor.CN/AssetEditor.CN.exe", "new executable"),
                (@"AssetEditor.CN\data\config.json", "new config"));

            UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("new executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "data", "config.json")), Is.EqualTo("new config"));
                Assert.That(File.Exists(Path.Combine(installationDirectory, "old-only.txt")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "UpdateBackup", "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "UpdateBackup", "old-only.txt")), Is.EqualTo("old file"));
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
    public void Install_UnsafeEntry_RejectsBeforeChangingInstallation(string unsafeEntry)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var installationDirectory = CreateInstallation(root);
            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateZip(root,
                ("AssetEditor.CN/AssetEditor.CN.exe", "new executable"),
                (unsafeEntry, "unsafe"));

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory));

            AssertInstallationWasNotTouched(installationDirectory);
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

            AssertInstallationWasNotTouched(installationDirectory);
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
            var archivePath = CreateZip(root,
                ("AssetEditor.CN/AssetEditor.CN.exe", "new executable"),
                ($"AssetEditor.CN/{backupDirectoryName}/injected.txt", "injected"));

            Assert.Throws<InvalidDataException>(() =>
                UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory));

            AssertInstallationWasNotTouched(installationDirectory);
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

            AssertInstallationWasNotTouched(installationDirectory);
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

            AssertInstallationWasNotTouched(installationDirectory);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Install_BackupAndPartialRestoreFailure_ThrowsBothErrors()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var installationDirectory = CreateInstallation(root);
            var restoreLockedPath = Path.Combine(installationDirectory, "00-restore-locked.txt");
            var backupLockedPath = Path.Combine(installationDirectory, "zz-backup-locked.txt");
            File.WriteAllText(restoreLockedPath, "restore lock");
            File.WriteAllText(backupLockedPath, "backup lock");

            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            var archivePath = CreateZip(root, ("AssetEditor.CN/AssetEditor.CN.exe", "new executable"));

            using (new FileStream(restoreLockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete))
            using (new FileStream(backupLockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var exception = Assert.Throws<AggregateException>(() =>
                    UpdateInstaller.Install(archivePath, installationDirectory, updateDirectory));

                Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(Directory.Exists(Path.Combine(installationDirectory, "UpdateBackup")), Is.True);
            }
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
    public void CopyUpdaterPayload_CopiesNestedFilesAndRemovesOldPayload()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var payloadDirectory = Directory.CreateDirectory(Path.Combine(root, "installation", "Updater")).FullName;
            Directory.CreateDirectory(Path.Combine(payloadDirectory, "runtimes", "win-x64"));
            File.WriteAllText(Path.Combine(payloadDirectory, "AssetEditor.CN.Updater.exe"), "updater");
            File.WriteAllText(Path.Combine(payloadDirectory, "runtimes", "win-x64", "native.dll"), "dependency");

            var updateDirectory = Directory.CreateDirectory(Path.Combine(root, "update")).FullName;
            File.WriteAllText(Path.Combine(updateDirectory, "obsolete.dll"), "old payload");

            UpdaterProgram.CopyUpdaterPayload(payloadDirectory, updateDirectory);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(updateDirectory, "AssetEditor.CN.Updater.exe")), Is.EqualTo("updater"));
                Assert.That(File.ReadAllText(Path.Combine(updateDirectory, "runtimes", "win-x64", "native.dll")), Is.EqualTo("dependency"));
                Assert.That(File.Exists(Path.Combine(updateDirectory, "obsolete.dll")), Is.False);
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
            var backupDirectory = Directory.CreateDirectory(Path.Combine(installationDirectory, "UpdateBackup")).FullName;
            File.WriteAllText(Path.Combine(backupDirectory, "AssetEditor.CN.exe"), "old executable");
            File.WriteAllText(Path.Combine(backupDirectory, "old-only.txt"), "old file");
            File.WriteAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe"), "partial executable");
            File.WriteAllText(Path.Combine(installationDirectory, "partial-new.txt"), "partial file");

            UpdateInstaller.RestoreBackup(installationDirectory);

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
        var installationDirectory = Directory.CreateDirectory(Path.Combine(root, "installation")).FullName;
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

    private static string CreateZipWithDirectory(string root, string directoryPath)
    {
        var archivePath = Path.Combine(root, $"update-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        archive.CreateEntry(directoryPath);

        var executableEntry = archive.CreateEntry("AssetEditor.CN/AssetEditor.CN.exe");
        using var writer = new StreamWriter(executableEntry.Open());
        writer.Write("new executable");
        return archivePath;
    }

    private static string CreateTarWithZipExtension(string root)
    {
        var payloadRoot = Directory.CreateDirectory(Path.Combine(root, "tar-payload")).FullName;
        var archiveRoot = Directory.CreateDirectory(Path.Combine(payloadRoot, "AssetEditor.CN")).FullName;
        File.WriteAllText(Path.Combine(archiveRoot, "AssetEditor.CN.exe"), "new executable");

        var archivePath = Path.Combine(root, $"update-{Guid.NewGuid():N}.zip");
        TarFile.CreateFromDirectory(payloadRoot, archivePath, false);
        return archivePath;
    }

    private static void AssertInstallationWasNotTouched(string installationDirectory)
    {
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "AssetEditor.CN.exe")), Is.EqualTo("old executable"));
            Assert.That(File.ReadAllText(Path.Combine(installationDirectory, "old-only.txt")), Is.EqualTo("old file"));
            Assert.That(Directory.Exists(Path.Combine(installationDirectory, "UpdateBackup")), Is.False);
        });
    }
}
