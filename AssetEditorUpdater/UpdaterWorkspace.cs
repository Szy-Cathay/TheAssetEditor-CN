using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace AssetEditorUpdater;

internal sealed record UpdaterWorkspace(
    string TransactionRoot,
    string UpdateDirectory,
    bool IsProtected);

internal sealed record UpdaterTransactionPaths(
    string TransactionRoot,
    string UpdateDirectory,
    string StagingDirectory,
    string BackupRootDirectory,
    string MarkerPath);

internal sealed record UpdaterWorkspaceLayout(
    string ApprovedRoot,
    string TransactionRoot,
    string UpdateDirectory,
    bool IsProtected);

internal static class UpdaterWorkspaceFactory
{
    internal const string TransactionMarkerFileName =
        ".asset-editor-cn-updater-transaction";

    private const string ProductDirectoryName = "AssetEditor.CN";
    private const string UpdateDirectoryName = "Update";
    private const string StagingDirectoryName = "staging";
    private const string BackupRootDirectoryName = "UpdateBackups";
    private const string UpdaterTransactionsDirectoryName = "UpdaterTransactions";
    private const string TransactionMarkerHeader =
        "AssetEditor.CN updater transaction v1\n";

    internal static bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static UpdaterWorkspaceLayout GetLayout(
        bool isElevated,
        Guid transactionId,
        string? localApplicationDataRoot = null,
        string? commonApplicationDataRoot = null)
    {
        if (!isElevated)
        {
            var localRoot = GetCanonicalRoot(
                localApplicationDataRoot,
                Environment.SpecialFolder.LocalApplicationData);
            var transactionRoot = Path.Combine(localRoot, ProductDirectoryName, "Temp");
            return new UpdaterWorkspaceLayout(
                localRoot,
                transactionRoot,
                Path.Combine(transactionRoot, UpdateDirectoryName),
                false);
        }

        if (transactionId == Guid.Empty)
            throw new ArgumentException("An elevated workspace requires a non-empty transaction ID.", nameof(transactionId));

        var commonRoot = GetCanonicalRoot(
            commonApplicationDataRoot,
            Environment.SpecialFolder.CommonApplicationData);
        var protectedTransactionRoot = Path.Combine(
            commonRoot,
            ProductDirectoryName,
            UpdaterTransactionsDirectoryName,
            transactionId.ToString("N"));
        return new UpdaterWorkspaceLayout(
            commonRoot,
            protectedTransactionRoot,
            Path.Combine(protectedTransactionRoot, UpdateDirectoryName),
            true);
    }

    internal static UpdaterTransactionPaths GetTransactionPaths(string updateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateDirectory);

        var fullUpdateDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(updateDirectory));
        var transactionRoot = Path.GetDirectoryName(fullUpdateDirectory);
        if (transactionRoot == null
            || !string.Equals(
                Path.GetFileName(fullUpdateDirectory),
                UpdateDirectoryName,
                StringComparison.OrdinalIgnoreCase)
            || !PathsEqual(
                fullUpdateDirectory,
                Path.Combine(transactionRoot, UpdateDirectoryName)))
        {
            throw new ArgumentException(
                "The update directory must be directly inside the transaction root.",
                nameof(updateDirectory));
        }

        return new UpdaterTransactionPaths(
            transactionRoot,
            fullUpdateDirectory,
            Path.Combine(transactionRoot, StagingDirectoryName),
            Path.Combine(transactionRoot, BackupRootDirectoryName),
            Path.Combine(transactionRoot, TransactionMarkerFileName));
    }

    internal static UpdaterWorkspace Create(UpdaterWorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        layout = ValidateLayout(layout);
        var paths = GetTransactionPaths(layout.UpdateDirectory);

        if (!layout.IsProtected)
        {
            EnsureLocalWorkspace(layout, paths);
            return new UpdaterWorkspace(
                paths.TransactionRoot,
                paths.UpdateDirectory,
                false);
        }

        if (PathEntryExists(paths.TransactionRoot))
        {
            throw new IOException(
                $"The updater transaction path already exists: {paths.TransactionRoot}");
        }

        var protectedParent = Path.GetDirectoryName(paths.TransactionRoot)
            ?? throw new ArgumentException("The transaction root must have a parent directory.", nameof(layout));
        EnsureProtectedParent(layout.ApprovedRoot, protectedParent);

        var descriptor = UpdaterWorkspaceSecurity.CreateProtectedDescriptor();
        var transactionCreated = false;
        var markerCreated = false;
        try
        {
            CreateDirectoryAtomically(paths.TransactionRoot, descriptor);
            transactionCreated = true;
            UpdaterWorkspaceSecurity.ValidateProtectedDirectory(paths.TransactionRoot);

            CreateTransactionMarker(layout, paths.MarkerPath);
            markerCreated = true;

            CreateDirectoryAtomically(paths.UpdateDirectory, descriptor);
            UpdaterWorkspaceSecurity.ValidateProtectedDirectory(paths.UpdateDirectory);
        }
        catch
        {
            if (transactionCreated && markerCreated)
            {
                try
                {
                    CleanupFreshProtectedTransaction(layout);
                }
                catch (Exception cleanupException)
                {
                    Console.Error.WriteLine(
                        $"Updater cleanup failed; preserving the original failure: {cleanupException}");
                }
            }

            throw;
        }

        return new UpdaterWorkspace(
            paths.TransactionRoot,
            paths.UpdateDirectory,
            true);
    }

    internal static UpdaterWorkspace ValidateExisting(
        bool isElevated,
        string updateDirectory,
        string? localApplicationDataRoot = null,
        string? commonApplicationDataRoot = null)
    {
        var layout = GetExistingLayout(
            isElevated,
            updateDirectory,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        ValidateOwnedTransactionRoot(layout, layout.IsProtected);

        return new UpdaterWorkspace(
            layout.TransactionRoot,
            layout.UpdateDirectory,
            layout.IsProtected);
    }

    internal static void ValidateOwnedTransactionRoot(
        string updateDirectory,
        bool requireProtectedAcl)
    {
        ValidateOwnedTransactionRoot(
            updateDirectory,
            requireProtectedAcl,
            null,
            null);
    }

    internal static void ValidateOwnedTransactionRoot(
        string updateDirectory,
        bool requireProtectedAcl,
        string? localApplicationDataRoot,
        string? commonApplicationDataRoot)
    {
        var layout = GetExistingLayout(
            requireProtectedAcl,
            updateDirectory,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        ValidateOwnedTransactionRoot(layout, requireProtectedAcl);
    }

    internal static UpdaterWorkspace ValidateExisting(UpdaterWorkspaceLayout layout)
    {
        layout = ValidateLayout(layout);
        ValidateOwnedTransactionRoot(layout, layout.IsProtected);
        return new UpdaterWorkspace(
            layout.TransactionRoot,
            layout.UpdateDirectory,
            layout.IsProtected);
    }

    private static string GetCanonicalRoot(string? configuredRoot, Environment.SpecialFolder specialFolder)
    {
        var root = configuredRoot ?? Environment.GetFolderPath(specialFolder);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException($"Unable to resolve {specialFolder}.");

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    internal static UpdaterWorkspaceLayout GetExistingLayout(
        bool isElevated,
        string updateDirectory,
        string? localApplicationDataRoot,
        string? commonApplicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateDirectory);

        var fullUpdateDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(updateDirectory));
        if (!isElevated)
        {
            var expectedLayout = GetLayout(
                false,
                Guid.Empty,
                localApplicationDataRoot,
                commonApplicationDataRoot);
            if (!PathsEqual(fullUpdateDirectory, expectedLayout.UpdateDirectory))
            {
                throw new ArgumentException(
                    "A non-elevated updater must use the fixed LocalApplicationData update directory.",
                    nameof(updateDirectory));
            }

            return expectedLayout;
        }

        var transactionRoot = Path.GetDirectoryName(fullUpdateDirectory);
        var transactionName = transactionRoot == null
            ? null
            : Path.GetFileName(transactionRoot);
        if (!string.Equals(
                Path.GetFileName(fullUpdateDirectory),
                UpdateDirectoryName,
                StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(transactionName, "N", out var transactionId)
            || transactionId == Guid.Empty
            || !string.Equals(
                transactionName,
                transactionId.ToString("N"),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An elevated updater must use a GUID transaction Update directory.",
                nameof(updateDirectory));
        }

        var protectedLayout = GetLayout(
            true,
            transactionId,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        if (!PathsEqual(fullUpdateDirectory, protectedLayout.UpdateDirectory))
        {
            throw new ArgumentException(
                "An elevated updater must use the fixed CommonApplicationData transaction family.",
                nameof(updateDirectory));
        }

        return protectedLayout;
    }

    private static UpdaterWorkspaceLayout ValidateLayout(UpdaterWorkspaceLayout layout)
    {
        var approvedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(layout.ApprovedRoot));
        var transactionRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(layout.TransactionRoot));
        var updateDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(layout.UpdateDirectory));

        var expectedTransactionRoot = layout.IsProtected
            ? GetExpectedProtectedTransactionRoot(approvedRoot, transactionRoot)
            : Path.Combine(approvedRoot, ProductDirectoryName, "Temp");
        if (!PathsEqual(transactionRoot, expectedTransactionRoot)
            || !PathsEqual(
                updateDirectory,
                Path.Combine(transactionRoot, UpdateDirectoryName)))
        {
            throw new ArgumentException(
                "The updater workspace layout is outside its approved path family.",
                nameof(layout));
        }

        return new UpdaterWorkspaceLayout(
            approvedRoot,
            transactionRoot,
            updateDirectory,
            layout.IsProtected);
    }

    private static string GetExpectedProtectedTransactionRoot(
        string approvedRoot,
        string transactionRoot)
    {
        var transactionName = Path.GetFileName(transactionRoot);
        if (!Guid.TryParseExact(transactionName, "N", out var transactionId)
            || transactionId == Guid.Empty
            || !string.Equals(
                transactionName,
                transactionId.ToString("N"),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The protected transaction directory must have a non-empty GUID name.");
        }

        return Path.Combine(
            approvedRoot,
            ProductDirectoryName,
            UpdaterTransactionsDirectoryName,
            transactionName);
    }

    private static void EnsureProtectedParent(
        string approvedRoot,
        string protectedParent)
    {
        var productRoot = Path.GetDirectoryName(protectedParent)
            ?? throw new ArgumentException("The protected updater parent must have a product directory.");
        if (!PathsEqual(
                productRoot,
                Path.Combine(approvedRoot, ProductDirectoryName))
            || !PathsEqual(
                protectedParent,
                Path.Combine(productRoot, UpdaterTransactionsDirectoryName)))
        {
            throw new ArgumentException(
                "The protected updater parent is outside its approved path family.");
        }

        Directory.CreateDirectory(approvedRoot);
        ValidateExistingDirectory(approvedRoot);
        RejectReparsePoint(productRoot);
        RejectReparsePoint(protectedParent);

        var descriptor = UpdaterWorkspaceSecurity.CreateProtectedDescriptor();
        if (!Directory.Exists(productRoot))
            CreateDirectoryAtomically(productRoot, descriptor);

        UpdaterWorkspaceSecurity.ValidateProtectedDirectory(productRoot);

        if (!Directory.Exists(protectedParent))
            CreateDirectoryAtomically(protectedParent, descriptor);

        UpdaterWorkspaceSecurity.ValidateProtectedDirectory(protectedParent);
    }

    private static void EnsureLocalWorkspace(
        UpdaterWorkspaceLayout layout,
        UpdaterTransactionPaths paths)
    {
        var productRoot = Path.Combine(layout.ApprovedRoot, ProductDirectoryName);

        EnsureLocalDirectory(layout.ApprovedRoot);
        EnsureLocalDirectory(productRoot);
        EnsureLocalDirectory(paths.TransactionRoot);

        if (PathEntryExists(paths.UpdateDirectory))
            ValidateExistingDirectory(paths.UpdateDirectory);
        ValidateOptionalDerivedDirectories(paths, requireProtectedAcl: false);

        if (PathEntryExists(paths.MarkerPath))
            ValidateTransactionMarker(layout, paths.MarkerPath);
        else
            CreateTransactionMarker(layout, paths.MarkerPath);

        EnsureLocalDirectory(paths.UpdateDirectory);
    }

    private static void EnsureLocalDirectory(string path)
    {
        if (PathEntryExists(path) && !Directory.Exists(path))
            throw new IOException($"The updater workspace path is not a directory: {path}");

        Directory.CreateDirectory(path);
        ValidateExistingDirectory(path);
    }

    private static void CleanupFreshProtectedTransaction(
        UpdaterWorkspaceLayout layout)
    {
        ValidateOwnedTransactionRoot(
            layout,
            requireProtectedAcl: true,
            requireUpdateDirectory: false,
            validateDerivedDirectories: false);
        var paths = GetTransactionPaths(layout.UpdateDirectory);
        var allowedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            paths.MarkerPath,
            paths.UpdateDirectory
        };
        var entries = Directory
            .EnumerateFileSystemEntries(paths.TransactionRoot)
            .Select(Path.GetFullPath)
            .ToArray();
        if (entries.Any(entry => !allowedPaths.Contains(entry)))
        {
            throw new InvalidDataException(
                "The fresh updater transaction contains an unexpected cleanup entry.");
        }

        if (PathEntryExists(paths.UpdateDirectory))
        {
            UpdaterWorkspaceSecurity.ValidateProtectedDirectory(
                paths.UpdateDirectory);
            if (Directory.EnumerateFileSystemEntries(paths.UpdateDirectory).Any())
            {
                throw new InvalidDataException(
                    "The fresh updater Update directory is not empty.");
            }

            Directory.Delete(paths.UpdateDirectory, recursive: false);
        }

        ValidateTransactionMarker(layout, paths.MarkerPath);
        File.Delete(paths.MarkerPath);
        Directory.Delete(paths.TransactionRoot, recursive: false);
    }

    private static void ValidateOwnedTransactionRoot(
        UpdaterWorkspaceLayout layout,
        bool requireProtectedAcl,
        bool requireUpdateDirectory = true,
        bool validateDerivedDirectories = true)
    {
        layout = ValidateLayout(layout);
        var paths = GetTransactionPaths(layout.UpdateDirectory);
        var productRoot = Path.Combine(layout.ApprovedRoot, ProductDirectoryName);

        ValidateExistingDirectory(layout.ApprovedRoot);
        ValidateExistingDirectory(productRoot);
        if (layout.IsProtected)
        {
            var protectedParent = Path.Combine(
                productRoot,
                UpdaterTransactionsDirectoryName);
            ValidateExistingDirectory(protectedParent);
            ValidateExistingDirectory(paths.TransactionRoot);

            if (requireProtectedAcl)
            {
                UpdaterWorkspaceSecurity.ValidateProtectedDirectory(productRoot);
                UpdaterWorkspaceSecurity.ValidateProtectedDirectory(protectedParent);
                UpdaterWorkspaceSecurity.ValidateProtectedDirectory(paths.TransactionRoot);
            }
        }
        else
        {
            ValidateExistingDirectory(paths.TransactionRoot);
        }

        if (requireUpdateDirectory)
        {
            ValidateExistingDirectory(paths.UpdateDirectory);
            if (requireProtectedAcl)
            {
                UpdaterWorkspaceSecurity.ValidateProtectedDirectory(
                    paths.UpdateDirectory);
            }
        }

        ValidateTransactionMarker(layout, paths.MarkerPath);

        if (validateDerivedDirectories)
            ValidateOptionalDerivedDirectories(paths, requireProtectedAcl);
    }

    private static void ValidateOptionalDerivedDirectories(
        UpdaterTransactionPaths paths,
        bool requireProtectedAcl)
    {
        foreach (var path in new[]
                 {
                     paths.StagingDirectory,
                     paths.BackupRootDirectory
                 })
        {
            if (!PathEntryExists(path))
                continue;

            ValidateExistingDirectory(path);
            if (requireProtectedAcl)
                UpdaterWorkspaceSecurity.ValidateProtectedDirectory(path);
        }
    }

    private static void CreateTransactionMarker(
        UpdaterWorkspaceLayout layout,
        string markerPath)
    {
        var markerBytes = GetExpectedTransactionMarker(layout);
        using var markerStream = new FileStream(
            markerPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        markerStream.Write(markerBytes);
        markerStream.Flush(flushToDisk: true);
    }

    private static void ValidateTransactionMarker(
        UpdaterWorkspaceLayout layout,
        string markerPath)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(markerPath);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
            or DirectoryNotFoundException)
        {
            throw new InvalidDataException(
                $"Updater transaction ownership marker not found: {markerPath}",
                exception);
        }

        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException(
                $"Updater transaction ownership marker must be an ordinary file: {markerPath}");
        }

        var expectedBytes = GetExpectedTransactionMarker(layout);
        using var markerStream = new FileStream(
            markerPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        if (markerStream.Length != expectedBytes.Length)
            throw CreateInvalidMarkerException(markerPath);

        var actualBytes = new byte[expectedBytes.Length];
        markerStream.ReadExactly(actualBytes);
        if (!actualBytes.AsSpan().SequenceEqual(expectedBytes))
            throw CreateInvalidMarkerException(markerPath);
    }

    private static byte[] GetExpectedTransactionMarker(
        UpdaterWorkspaceLayout layout)
    {
        var mode = layout.IsProtected ? "protected" : "local";
        var transactionId = layout.IsProtected
            ? Path.GetFileName(layout.TransactionRoot)
            : Guid.Empty.ToString("N");
        return Encoding.UTF8.GetBytes(
            TransactionMarkerHeader
            + $"mode={mode}\n"
            + $"id={transactionId}\n");
    }

    private static InvalidDataException CreateInvalidMarkerException(
        string markerPath)
    {
        return new InvalidDataException(
            $"Updater transaction ownership marker is invalid: {markerPath}");
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

    private static void RejectReparsePoint(string path)
    {
        if (File.Exists(path) && !Directory.Exists(path))
            throw new IOException($"The updater workspace path is not a directory: {path}");

        if (Directory.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"The updater workspace path cannot be a reparse point: {path}");
        }
    }

    private static void ValidateExistingDirectory(string path)
    {
        RejectReparsePoint(path);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Updater workspace directory not found: {path}");

        using var identity = WindowsPathIdentity.OpenExistingDirectory(
            path,
            nameof(path));
    }

    private static void CreateDirectoryAtomically(string path, DirectorySecurity descriptor)
    {
        var binaryDescriptor = descriptor.GetSecurityDescriptorBinaryForm();
        var descriptorHandle = GCHandle.Alloc(binaryDescriptor, GCHandleType.Pinned);
        try
        {
            var securityAttributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptorHandle.AddrOfPinnedObject()
            };
            if (CreateDirectory(path, ref securityAttributes))
                return;

            var error = Marshal.GetLastWin32Error();
            throw new IOException(
                $"Unable to create the updater workspace directory: {path}",
                new Win32Exception(error));
        }
        finally
        {
            descriptorHandle.Free();
        }
    }

    private static bool PathsEqual(string firstPath, string secondPath)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstPath)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondPath)),
            StringComparison.OrdinalIgnoreCase);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr SecurityDescriptor;

        [MarshalAs(UnmanagedType.Bool)]
        internal bool InheritHandle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectory(
        string path,
        ref SecurityAttributes securityAttributes);
}

internal static class UpdaterWorkspaceSecurity
{
    private const FileSystemRights RequiredRights = FileSystemRights.FullControl;
    private const InheritanceFlags RequiredInheritance =
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    internal static DirectorySecurity CreateProtectedDescriptor()
    {
        var administrators = CreateSid(WellKnownSidType.BuiltinAdministratorsSid);
        var system = CreateSid(WellKnownSidType.LocalSystemSid);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(administrators);
        security.AddAccessRule(CreateFullControlRule(administrators));
        security.AddAccessRule(CreateFullControlRule(system));
        return security;
    }

    internal static void ValidateProtectedDirectory(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Protected updater directory not found: {fullPath}");

        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Protected updater directory cannot be a reparse point: {fullPath}");

        var security = FileSystemAclExtensions.GetAccessControl(
            new DirectoryInfo(fullPath),
            AccessControlSections.Access | AccessControlSections.Owner);
        var administrators = CreateSid(WellKnownSidType.BuiltinAdministratorsSid);
        if (!administrators.Equals(security.GetOwner(typeof(SecurityIdentifier))))
            throw new InvalidOperationException($"Protected updater directory has an invalid owner: {fullPath}");

        if (!security.AreAccessRulesProtected || !security.AreAccessRulesCanonical)
            throw new InvalidOperationException($"Protected updater directory has an invalid ACL: {fullPath}");

        var rules = security
            .GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        var expectedSids = new[]
        {
            administrators,
            CreateSid(WellKnownSidType.LocalSystemSid)
        };
        if (rules.Length != expectedSids.Length
            || rules.Any(rule => !IsRequiredRule(rule))
            || expectedSids.Any(expected => rules.Count(rule => expected.Equals(rule.IdentityReference)) != 1))
        {
            throw new InvalidOperationException($"Protected updater directory grants unexpected access: {fullPath}");
        }
    }

    private static FileSystemAccessRule CreateFullControlRule(SecurityIdentifier sid)
    {
        return new FileSystemAccessRule(
            sid,
            RequiredRights,
            RequiredInheritance,
            PropagationFlags.None,
            AccessControlType.Allow);
    }

    private static bool IsRequiredRule(FileSystemAccessRule rule)
    {
        return rule.AccessControlType == AccessControlType.Allow
               && rule.FileSystemRights == RequiredRights
               && rule.InheritanceFlags == RequiredInheritance
               && rule.PropagationFlags == PropagationFlags.None
               && !rule.IsInherited;
    }

    private static SecurityIdentifier CreateSid(WellKnownSidType sidType)
    {
        return new SecurityIdentifier(sidType, null);
    }
}
