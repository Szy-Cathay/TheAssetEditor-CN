using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Shared.Core.Misc;
using Shared.Core.ModAssistant;

namespace AssetEditor.ModAssistant;

public sealed class AssistantSecretStore : IAssistantSecretStore
{
    private readonly string _path;
    public AssistantSecretStore() : this(Path.Combine(DirectoryHelper.LocalApplicationDirectory, "Assistant", "key.dpapi")) { }
    public AssistantSecretStore(string path) => _path = path;
    public bool HasKey => File.Exists(_path);
    public string Read()
    {
        if (!HasKey) return "";
        var bytes = Transform(File.ReadAllBytes(_path), false);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Save(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 512 || key.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException(AssistantJson.Text("InvalidKey"));
        var bytes = Encoding.UTF8.GetBytes(key.Trim());
        try
        {
            var encrypted = Transform(bytes, true);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, encrypted);
                File.Move(temporary, _path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Clear() { if (HasKey) File.Delete(_path); }

    private static byte[] Transform(byte[] data, bool protect)
    {
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        var input = new DataBlob { Length = data.Length, Data = pinned.AddrOfPinnedObject() };
        DataBlob output = default;
        try
        {
            var success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), AssistantJson.Text("KeyStoreError"));
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
            {
                for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
            pinned.Free();
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
