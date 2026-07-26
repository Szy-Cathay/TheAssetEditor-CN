using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AssetEditorUpdater;

internal sealed record UpdaterWorkspace(
    string TransactionRoot,
    string UpdateDirectory,
    bool IsProtected);

internal sealed record UpdaterWorkspaceLayout(
    string TransactionRoot,
    string UpdateDirectory,
    bool IsProtected);

internal static class UpdaterWorkspaceFactory
{
    private const string ProductDirectoryName = "AssetEditor.CN";
    private const string UpdateDirectoryName = "Update";
    private const string UpdaterTransactionsDirectoryName = "UpdaterTransactions";

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
            protectedTransactionRoot,
            Path.Combine(protectedTransactionRoot, UpdateDirectoryName),
            true);
    }

    internal static UpdaterWorkspace Create(UpdaterWorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var transactionRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(layout.TransactionRoot));
        var updateDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(layout.UpdateDirectory));
        var expectedUpdateDirectory = Path.Combine(transactionRoot, UpdateDirectoryName);
        if (!PathsEqual(updateDirectory, expectedUpdateDirectory))
            throw new ArgumentException("The update directory must be directly inside the transaction root.", nameof(layout));

        if (!layout.IsProtected)
        {
            Directory.CreateDirectory(updateDirectory);
            return new UpdaterWorkspace(transactionRoot, updateDirectory, false);
        }

        ValidateProtectedLayout(transactionRoot);
        if (Directory.Exists(transactionRoot) || File.Exists(transactionRoot))
            throw new IOException($"The updater transaction path already exists: {transactionRoot}");

        var protectedParent = Path.GetDirectoryName(transactionRoot)
            ?? throw new ArgumentException("The transaction root must have a parent directory.", nameof(layout));
        EnsureProtectedParent(protectedParent);

        var descriptor = UpdaterWorkspaceSecurity.CreateProtectedDescriptor();
        var transactionCreated = false;
        try
        {
            CreateDirectoryAtomically(transactionRoot, descriptor);
            transactionCreated = true;
            UpdaterWorkspaceSecurity.ValidateProtectedDirectory(transactionRoot);

            CreateDirectoryAtomically(updateDirectory, descriptor);
            UpdaterWorkspaceSecurity.ValidateProtectedDirectory(updateDirectory);
        }
        catch
        {
            if (transactionCreated)
                Directory.Delete(transactionRoot, true);
            throw;
        }

        return new UpdaterWorkspace(transactionRoot, updateDirectory, true);
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
        var transactionRoot = layout.TransactionRoot;
        var productRoot = Path.GetDirectoryName(transactionRoot)
            ?? throw new ArgumentException("The updater transaction root must have a product directory.");
        var approvedRoot = Path.GetDirectoryName(productRoot)
            ?? throw new ArgumentException("The updater product directory must have an approved root.");

        if (layout.IsProtected)
        {
            var protectedParent = productRoot;
            productRoot = Path.GetDirectoryName(protectedParent)
                ?? throw new ArgumentException("The protected updater parent must have a product directory.");
            approvedRoot = Path.GetDirectoryName(productRoot)
                ?? throw new ArgumentException("The protected updater product directory must have an approved root.");

            ValidateExistingDirectory(approvedRoot);
            UpdaterWorkspaceSecurity.ValidateProtectedDirectory(productRoot);
            UpdaterWorkspaceSecurity.ValidateProtectedDirectory(protectedParent);
            UpdaterWorkspaceSecurity.ValidateProtectedDirectory(transactionRoot);
            UpdaterWorkspaceSecurity.ValidateProtectedDirectory(layout.UpdateDirectory);
        }
        else
        {
            ValidateExistingDirectory(approvedRoot);
            ValidateExistingDirectory(productRoot);
            ValidateExistingDirectory(transactionRoot);
            ValidateExistingDirectory(layout.UpdateDirectory);
        }

        return new UpdaterWorkspace(
            transactionRoot,
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

    private static UpdaterWorkspaceLayout GetExistingLayout(
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
            || transactionId == Guid.Empty)
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

    private static void ValidateProtectedLayout(string transactionRoot)
    {
        var transactionName = Path.GetFileName(transactionRoot);
        if (!Guid.TryParseExact(transactionName, "N", out var transactionId) || transactionId == Guid.Empty)
            throw new ArgumentException("The protected transaction directory must have a non-empty GUID name.");

        var protectedParent = Path.GetDirectoryName(transactionRoot);
        var productRoot = protectedParent == null ? null : Path.GetDirectoryName(protectedParent);
        if (protectedParent == null
            || productRoot == null
            || !string.Equals(
                Path.GetFileName(protectedParent),
                UpdaterTransactionsDirectoryName,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetFileName(productRoot),
                ProductDirectoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The protected transaction directory has an invalid layout.");
        }
    }

    private static void EnsureProtectedParent(string protectedParent)
    {
        var productRoot = Path.GetDirectoryName(protectedParent)
            ?? throw new ArgumentException("The protected updater parent must have a product directory.");
        var approvedRoot = Path.GetDirectoryName(productRoot)
            ?? throw new ArgumentException("The protected updater parent must have an approved root.");

        Directory.CreateDirectory(approvedRoot);
        RejectReparsePoint(approvedRoot);
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
