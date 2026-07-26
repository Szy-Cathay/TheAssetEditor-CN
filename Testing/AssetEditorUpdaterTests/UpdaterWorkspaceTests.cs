using System.Security.AccessControl;
using System.Security.Principal;
using AssetEditorUpdater;
using UpdaterProgram = AssetEditorUpdater.AssetEditorUpdater;

namespace AssetEditorUpdaterTests;

public class UpdaterWorkspaceTests
{
    [Test]
    public void ParseInvocation_ZeroArgumentsUsesCurrentUpdaterParentAsInstallationDirectory()
    {
        var currentDirectory = Path.Combine(
            Path.GetTempPath(),
            "AssetEditor.CN",
            "Updater",
            string.Empty);

        var invocation = UpdaterProgram.ParseInvocation(currentDirectory, []);

        Assert.Multiple(() =>
        {
            Assert.That(invocation.IsInitialLaunch, Is.True);
            Assert.That(
                invocation.InstallationDirectory,
                Is.EqualTo(Path.GetFullPath(Path.Combine(currentDirectory, ".."))));
            Assert.That(invocation.UpdateDirectory, Is.Null);
        });
    }

    [Test]
    public void ParseInvocation_TwoArgumentsUsesInstallationThenUpdateDirectory()
    {
        var installationDirectory = Path.Combine(Path.GetTempPath(), "installation");
        var updateDirectory = Path.Combine(Path.GetTempPath(), "transaction", "Update");

        var invocation = UpdaterProgram.ParseInvocation(
            Path.Combine(Path.GetTempPath(), "ignored"),
            [installationDirectory, updateDirectory]);

        Assert.Multiple(() =>
        {
            Assert.That(invocation.IsInitialLaunch, Is.False);
            Assert.That(invocation.InstallationDirectory, Is.EqualTo(installationDirectory));
            Assert.That(invocation.UpdateDirectory, Is.EqualTo(updateDirectory));
        });
    }

    [TestCase(1)]
    [TestCase(3)]
    public void ParseInvocation_AnyOtherArgumentCountIsRejected(int argumentCount)
    {
        var args = Enumerable.Range(0, argumentCount)
            .Select(index => $"argument-{index}")
            .ToArray();

        Assert.Throws<ArgumentException>(() =>
            UpdaterProgram.ParseInvocation(Path.GetTempPath(), args));
    }

    [Test]
    public void CreateUpdaterProcessStartInfo_PassesInstallationAndUpdateDirectoriesInOrder()
    {
        var updateDirectory = Path.Combine(Path.GetTempPath(), "transaction", "Update");
        var workingDirectory = Path.Combine(updateDirectory, ".");
        var updaterPath = Path.Combine(updateDirectory, "AssetEditor.CN.Updater.exe");
        var installationDirectory = Path.Combine(Path.GetTempPath(), "installation");

        var startInfo = UpdaterProgram.CreateUpdaterProcessStartInfo(
            updaterPath,
            workingDirectory,
            installationDirectory,
            updateDirectory);

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.FileName, Is.EqualTo(updaterPath));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(workingDirectory));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(
                startInfo.ArgumentList,
                Is.EqualTo(new[] { installationDirectory, updateDirectory }));
        });
    }

    [Test]
    public void ValidateDirectoryLayout_NonElevatedAcceptsOnlyFixedLocalApplicationDataPath()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var installationDirectory = Directory.CreateDirectory(Path.Combine(root, "installation")).FullName;
            var layout = UpdaterWorkspaceFactory.GetLayout(
                false,
                Guid.Empty,
                localRoot,
                commonRoot);
            Directory.CreateDirectory(layout.UpdateDirectory);

            Assert.DoesNotThrow(() =>
                UpdateInstaller.ValidateDirectoryLayout(
                    installationDirectory,
                    layout.UpdateDirectory,
                    false,
                    localRoot,
                    commonRoot));

            var lookalikeUpdateDirectory = Directory.CreateDirectory(
                Path.Combine(root, "lookalike", "AssetEditor.CN", "Temp", "Update")).FullName;
            var sentinelPath = Path.Combine(lookalikeUpdateDirectory, "preserve.txt");
            File.WriteAllText(sentinelPath, "preserve");

            Assert.Throws<ArgumentException>(() =>
                UpdateInstaller.ValidateDirectoryLayout(
                    installationDirectory,
                    lookalikeUpdateDirectory,
                    false,
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
    public void ValidateDirectoryLayout_RejectsInstallationInsideFixedTransactionRoot()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var layout = UpdaterWorkspaceFactory.GetLayout(
                false,
                Guid.Empty,
                localRoot,
                commonRoot);
            Directory.CreateDirectory(layout.UpdateDirectory);
            var installationDirectory = Directory.CreateDirectory(
                Path.Combine(layout.TransactionRoot, "installation")).FullName;

            Assert.Throws<InvalidOperationException>(() =>
                UpdateInstaller.ValidateDirectoryLayout(
                    installationDirectory,
                    layout.UpdateDirectory,
                    false,
                    localRoot,
                    commonRoot));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void ValidateDirectoryLayout_ElevatedRejectsLocalAndLookalikeProtectedPaths()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var installationDirectory = Directory.CreateDirectory(Path.Combine(root, "installation")).FullName;
            var localLayout = UpdaterWorkspaceFactory.GetLayout(
                false,
                Guid.Empty,
                localRoot,
                commonRoot);
            Directory.CreateDirectory(localLayout.UpdateDirectory);
            var lookalikeUpdateDirectory = Directory.CreateDirectory(
                Path.Combine(
                    root,
                    "lookalike",
                    "AssetEditor.CN",
                    "UpdaterTransactions",
                    Guid.NewGuid().ToString("N"),
                    "Update")).FullName;

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() =>
                    UpdateInstaller.ValidateDirectoryLayout(
                        installationDirectory,
                        localLayout.UpdateDirectory,
                        true,
                        localRoot,
                        commonRoot));
                Assert.Throws<ArgumentException>(() =>
                    UpdateInstaller.ValidateDirectoryLayout(
                        installationDirectory,
                        lookalikeUpdateDirectory,
                        true,
                        localRoot,
                        commonRoot));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void ValidateDirectoryLayout_ElevatedRejectsOrdinaryAclInProtectedFamily()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var installationDirectory = Directory.CreateDirectory(Path.Combine(root, "installation")).FullName;
            var layout = UpdaterWorkspaceFactory.GetLayout(
                true,
                Guid.NewGuid(),
                localRoot,
                commonRoot);
            Directory.CreateDirectory(layout.UpdateDirectory);

            Assert.Throws<InvalidOperationException>(() =>
                UpdateInstaller.ValidateDirectoryLayout(
                    installationDirectory,
                    layout.UpdateDirectory,
                    true,
                    localRoot,
                    commonRoot));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void GetLayout_SelectsLocalRootForNormalProcessAndCommonRootForElevatedProcess()
    {
        var root = Path.Combine(Path.GetTempPath(), $"UpdaterWorkspaceTests-{Guid.NewGuid():N}");
        var localRoot = Path.Combine(root, "local", "nested", "..");
        var commonRoot = Path.Combine(root, "common", "nested", "..");
        var transactionId = Guid.NewGuid();

        var normal = UpdaterWorkspaceFactory.GetLayout(
            false,
            Guid.Empty,
            localRoot,
            commonRoot);
        var elevated = UpdaterWorkspaceFactory.GetLayout(
            true,
            transactionId,
            localRoot,
            commonRoot);

        var expectedLocalRoot = Path.GetFullPath(localRoot);
        var expectedCommonRoot = Path.GetFullPath(commonRoot);
        Assert.Multiple(() =>
        {
            Assert.That(
                normal.TransactionRoot,
                Is.EqualTo(Path.Combine(expectedLocalRoot, "AssetEditor.CN", "Temp")));
            Assert.That(normal.UpdateDirectory, Is.EqualTo(Path.Combine(normal.TransactionRoot, "Update")));
            Assert.That(normal.IsProtected, Is.False);

            Assert.That(elevated.TransactionRoot, Does.StartWith(expectedCommonRoot));
            Assert.That(elevated.TransactionRoot, Does.Not.StartWith(expectedLocalRoot));
            Assert.That(elevated.UpdateDirectory, Is.EqualTo(Path.Combine(elevated.TransactionRoot, "Update")));
            Assert.That(elevated.IsProtected, Is.True);
            Assert.That(Path.GetFileName(elevated.TransactionRoot), Is.EqualTo(transactionId.ToString("N")));
        });
    }

    [Test]
    public void CreateProtectedDescriptor_AllowsOnlyAdministratorsAndSystemFullControl()
    {
        var security = UpdaterWorkspaceSecurity.CreateProtectedDescriptor();
        var administrators = CreateSid(WellKnownSidType.BuiltinAdministratorsSid);
        var system = CreateSid(WellKnownSidType.LocalSystemSid);
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        var rules = security
            .GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(security.AreAccessRulesProtected, Is.True);
            Assert.That(owner, Is.EqualTo(administrators));
            Assert.That(rules, Has.Length.EqualTo(2));
            Assert.That(rules.Select(rule => rule.IdentityReference), Is.EquivalentTo(new[] { administrators, system }));
            Assert.That(rules.All(IsInheritableFullControlAllow), Is.True);
        });
    }

    [Test]
    public void CreateProtectedDescriptor_DoesNotAllowGeneralOrCurrentUsers()
    {
        var security = UpdaterWorkspaceSecurity.CreateProtectedDescriptor();
        var allowedSids = security
            .GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .ToArray();
        var forbiddenSids = new[]
        {
            WindowsIdentity.GetCurrent().User!,
            CreateSid(WellKnownSidType.BuiltinUsersSid),
            CreateSid(WellKnownSidType.AuthenticatedUserSid),
            CreateSid(WellKnownSidType.WorldSid)
        };

        Assert.That(forbiddenSids.Any(forbidden => allowedSids.Contains(forbidden)), Is.False);
    }

    [Test]
    public void Create_NonProtectedLayoutCreatesUpdateDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var layout = UpdaterWorkspaceFactory.GetLayout(
                false,
                Guid.Empty,
                Path.Combine(root, "local"),
                Path.Combine(root, "common"));

            var workspace = UpdaterWorkspaceFactory.Create(layout);

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(workspace.TransactionRoot), Is.True);
                Assert.That(Directory.Exists(workspace.UpdateDirectory), Is.True);
                Assert.That(workspace.TransactionRoot, Is.EqualTo(layout.TransactionRoot));
                Assert.That(workspace.UpdateDirectory, Is.EqualTo(layout.UpdateDirectory));
                Assert.That(workspace.IsProtected, Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Create_ProtectedLayoutRejectsPreExistingTransactionDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var layout = UpdaterWorkspaceFactory.GetLayout(
                true,
                Guid.NewGuid(),
                Path.Combine(root, "local"),
                Path.Combine(root, "common"));
            Directory.CreateDirectory(layout.TransactionRoot);

            Assert.Throws<IOException>(() => UpdaterWorkspaceFactory.Create(layout));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Create_ProtectedLayoutRejectsUnprotectedExistingProductRoot()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var productRoot = Directory.CreateDirectory(Path.Combine(commonRoot, "AssetEditor.CN")).FullName;
            var sentinelPath = Path.Combine(productRoot, "existing-user-data.txt");
            File.WriteAllText(sentinelPath, "preserve");
            var layout = UpdaterWorkspaceFactory.GetLayout(
                true,
                Guid.NewGuid(),
                Path.Combine(root, "local"),
                commonRoot);

            Assert.Throws<InvalidOperationException>(() => UpdaterWorkspaceFactory.Create(layout));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(sentinelPath), Is.EqualTo("preserve"));
                Assert.That(Directory.Exists(Path.Combine(productRoot, "UpdaterTransactions")), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Create_ProtectedLayoutRejectsReparsePointApprovedRoot()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var targetRoot = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var linkedRoot = Path.Combine(root, "linked");
            try
            {
                Directory.CreateSymbolicLink(linkedRoot, targetRoot);
            }
            catch (UnauthorizedAccessException)
            {
                Assert.Ignore("Creating directory symbolic links is not permitted on this machine.");
            }

            var layout = UpdaterWorkspaceFactory.GetLayout(
                true,
                Guid.NewGuid(),
                Path.Combine(root, "local"),
                linkedRoot);

            Assert.Throws<InvalidOperationException>(() => UpdaterWorkspaceFactory.Create(layout));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void ValidateProtectedDirectory_RejectsOrdinaryDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                UpdaterWorkspaceSecurity.ValidateProtectedDirectory(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Create_ProtectedLayoutAppliesAndValidatesDescriptor()
    {
        if (!UpdaterWorkspaceFactory.IsProcessElevated())
            Assert.Ignore("Protected workspace creation requires an elevated Windows token.");

        var root = CreateTemporaryDirectory();
        try
        {
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var layout = UpdaterWorkspaceFactory.GetLayout(
                true,
                Guid.NewGuid(),
                Path.Combine(root, "local"),
                commonRoot);

            var workspace = UpdaterWorkspaceFactory.Create(layout);

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(workspace.UpdateDirectory), Is.True);
                Assert.That(workspace.IsProtected, Is.True);
                Assert.DoesNotThrow(() =>
                    UpdateInstaller.ValidateDirectoryLayout(
                        Path.Combine(root, "installation"),
                        workspace.UpdateDirectory,
                        true,
                        Path.Combine(root, "local"),
                        commonRoot));
                Assert.DoesNotThrow(() =>
                    UpdaterWorkspaceSecurity.ValidateProtectedDirectory(workspace.TransactionRoot));
                Assert.DoesNotThrow(() =>
                    UpdaterWorkspaceSecurity.ValidateProtectedDirectory(workspace.UpdateDirectory));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static bool IsInheritableFullControlAllow(FileSystemAccessRule rule)
    {
        return rule.AccessControlType == AccessControlType.Allow
               && rule.FileSystemRights == FileSystemRights.FullControl
               && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)
               && rule.PropagationFlags == PropagationFlags.None
               && !rule.IsInherited;
    }

    private static SecurityIdentifier CreateSid(WellKnownSidType sidType)
    {
        return new SecurityIdentifier(sidType, null);
    }

    private static string CreateTemporaryDirectory()
    {
        return Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"UpdaterWorkspaceTests-{Guid.NewGuid():N}")).FullName;
    }
}
