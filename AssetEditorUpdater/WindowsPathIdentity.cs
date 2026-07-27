using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AssetEditorUpdater;

internal sealed class WindowsDirectoryIdentityLease : IDisposable
{
    private readonly IReadOnlyList<SafeFileHandle> handles;
    private readonly byte[] fileId;
    private bool isDisposed;

    internal WindowsDirectoryIdentityLease(
        string inputPath,
        string finalVolumePath,
        ulong volumeSerialNumber,
        byte[] fileId,
        IReadOnlyList<SafeFileHandle> handles)
    {
        InputPath = inputPath;
        FinalVolumePath = finalVolumePath;
        VolumeSerialNumber = volumeSerialNumber;
        this.fileId = fileId;
        this.handles = handles;
    }

    internal string InputPath { get; }

    internal string FinalVolumePath { get; }

    internal ulong VolumeSerialNumber { get; }

    internal ReadOnlyMemory<byte> FileId => fileId;

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        for (var index = handles.Count - 1; index >= 0; index--)
            handles[index].Dispose();
    }
}

internal static class WindowsPathIdentity
{
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint OpenExisting = 3;
    private const uint VolumeNameGuid = 0x1;
    private const FileShare IdentityShare =
        FileShare.Read | FileShare.Write;

    internal static WindowsDirectoryIdentityLease OpenExistingDirectory(
        string path,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var handles = new List<SafeFileHandle>();
        try
        {
            foreach (var component in GetDirectoryComponents(fullPath))
            {
                var componentHandle = OpenDirectory(
                    component,
                    FileFlagBackupSemantics | FileFlagOpenReparsePoint);
                handles.Add(componentHandle);
                ValidateOrdinaryDirectory(componentHandle, component);
            }

            var identityHandle = OpenDirectory(fullPath, FileFlagBackupSemantics);
            handles.Add(identityHandle);
            ValidateOrdinaryDirectory(identityHandle, fullPath);

            var finalVolumePath = GetFinalVolumePath(identityHandle, fullPath);
            var fileIdInfo = GetFileIdInfo(identityHandle, fullPath);
            var fileId = new byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(
                fileId.AsSpan(0, sizeof(ulong)),
                fileIdInfo.FileId.Low);
            BinaryPrimitives.WriteUInt64LittleEndian(
                fileId.AsSpan(sizeof(ulong), sizeof(ulong)),
                fileIdInfo.FileId.High);

            return new WindowsDirectoryIdentityLease(
                fullPath,
                finalVolumePath,
                fileIdInfo.VolumeSerialNumber,
                fileId,
                handles);
        }
        catch
        {
            for (var index = handles.Count - 1; index >= 0; index--)
                handles[index].Dispose();
            throw;
        }
    }

    internal static bool IsSameDirectory(
        WindowsDirectoryIdentityLease first,
        WindowsDirectoryIdentityLease second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        first.ThrowIfDisposed();
        second.ThrowIfDisposed();

        return first.VolumeSerialNumber == second.VolumeSerialNumber
               && first.FileId.Span.SequenceEqual(second.FileId.Span);
    }

    internal static bool IsSameOrAncestor(
        WindowsDirectoryIdentityLease possibleAncestor,
        WindowsDirectoryIdentityLease candidate)
    {
        ArgumentNullException.ThrowIfNull(possibleAncestor);
        ArgumentNullException.ThrowIfNull(candidate);
        possibleAncestor.ThrowIfDisposed();
        candidate.ThrowIfDisposed();

        if (IsSameDirectory(possibleAncestor, candidate))
            return true;

        var ancestorRoot = Path.EndsInDirectorySeparator(possibleAncestor.FinalVolumePath)
            ? possibleAncestor.FinalVolumePath
            : possibleAncestor.FinalVolumePath + Path.DirectorySeparatorChar;
        return candidate.FinalVolumePath.StartsWith(
            ancestorRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static void RequireDisjoint(
        WindowsDirectoryIdentityLease first,
        WindowsDirectoryIdentityLease second,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (IsSameOrAncestor(first, second)
            || IsSameOrAncestor(second, first))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static IEnumerable<string> GetDirectoryComponents(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidDataException($"Unable to resolve the updater directory root: {fullPath}");

        yield return root;

        var current = root;
        var relativePath = fullPath[root.Length..];
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            yield return current;
        }
    }

    private static SafeFileHandle OpenDirectory(string path, uint flags)
    {
        var handle = CreateFile(
            path,
            0,
            IdentityShare,
            IntPtr.Zero,
            OpenExisting,
            flags,
            IntPtr.Zero);
        if (!handle.IsInvalid)
            return handle;

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new IOException(
            $"Unable to open updater directory identity: {path}",
            new Win32Exception(error));
    }

    private static void ValidateOrdinaryDirectory(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandleEx(
                handle,
                FileInfoByHandleClass.FileAttributeTagInfo,
                out FileAttributeTagInfo information,
                (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
        {
            throw CreateQueryException(path);
        }

        if ((information.FileAttributes & FileAttributes.Directory) == 0)
            throw new InvalidDataException($"Updater identity path is not a directory: {path}");

        if ((information.FileAttributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Updater identity path cannot contain a reparse point: {path}");
    }

    private static FileIdInfo GetFileIdInfo(SafeFileHandle handle, string path)
    {
        if (GetFileInformationByHandleEx(
                handle,
                FileInfoByHandleClass.FileIdInfo,
                out FileIdInfo information,
                (uint)Marshal.SizeOf<FileIdInfo>()))
        {
            return information;
        }

        throw CreateQueryException(path);
    }

    private static string GetFinalVolumePath(SafeFileHandle handle, string path)
    {
        var capacity = 512;
        while (true)
        {
            var buffer = new StringBuilder(capacity);
            var length = GetFinalPathNameByHandle(
                handle,
                buffer,
                (uint)buffer.Capacity,
                VolumeNameGuid);
            if (length == 0)
                throw CreateQueryException(path);

            if (length < buffer.Capacity)
            {
                var finalPath = buffer.ToString().Replace(
                    Path.AltDirectorySeparatorChar,
                    Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(Path.GetPathRoot(finalPath)))
                    throw new InvalidDataException($"Updater directory final path is invalid: {path}");

                return Path.TrimEndingDirectorySeparator(finalPath);
            }

            capacity = checked((int)length + 1);
        }
    }

    private static IOException CreateQueryException(string path)
    {
        var error = Marshal.GetLastWin32Error();
        return new IOException(
            $"Unable to query updater directory identity: {path}",
            new Win32Exception(error));
    }

    private enum FileInfoByHandleClass
    {
        FileAttributeTagInfo = 9,
        FileIdInfo = 18
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        internal FileAttributes FileAttributes;
        internal uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        internal ulong VolumeSerialNumber;
        internal FileId128 FileId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileId128
    {
        internal ulong Low;
        internal ulong High;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        FileInfoByHandleClass fileInformationClass,
        out FileAttributeTagInfo fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        FileInfoByHandleClass fileInformationClass,
        out FileIdInfo fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);
}
