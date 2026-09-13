using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.ImageSupervisor;

[SupportedOSPlatform("linux")]
internal static class LinuxSupervisorFiles
{
    internal const string DirectoryPath = "/run/assetlibrary-image";
    internal const string SocketPath = DirectoryPath + "/decoder.sock";
    internal const string StatePath = DirectoryPath + "/supervisor-state.bin";
    private const string GuardPath = DirectoryPath + "/supervisor-lock.bin";
    private const string NextPath = DirectoryPath + "/supervisor-state.next";
    internal static bool DirectoryIsPrivate() => Native.Stat(DirectoryPath, out var value) == 0
        && value.Mode == (0x4000 | 456) && value.User == 0 && value.Group == 1654;
    internal static bool SocketIsPrivate() => Native.Stat(SocketPath, out var value) == 0
        && value.Mode == (0xc000 | 384) && value.User == 1654;
    internal static void RemoveOwnedSocket()
    {
        if (Native.Stat(SocketPath, out var value) != 0)
        {
            if (Marshal.GetLastPInvokeError() == 2) return;
            throw new IOException("Socket inspection failed.");
        }
        if (value.Mode != (0xc000 | 384) || value.User != 1654) throw new IOException("Unexpected socket object.");
        if (Native.Unlink(SocketPath) != 0) throw new IOException("Socket removal failed.");
    }
    internal static void SecureNewSocket()
    {
        if (Native.Chmod(SocketPath, 384) != 0 || Native.ChangeOwner(SocketPath, 1654, 1654) != 0 || !SocketIsPrivate())
            throw new IOException("Socket identity initialization failed.");
    }
    internal static FileStream OpenState(bool write) => Open(StatePath, write, exclusive: false);
    internal static FileStream OpenGuard() => Open(GuardPath, write: true, exclusive: false);
    internal static FileStream CreateNext() => Open(NextPath, write: true, exclusive: true);
    internal static void RecoverTemporary()
    {
        if (Native.Stat(NextPath, out var value) != 0)
        {
            if (Marshal.GetLastPInvokeError() == 2) return;
            throw new IOException("Circuit temporary inspection failed.");
        }
        if (!ValidState(value) || Native.Unlink(NextPath) != 0) throw new IOException("Circuit temporary recovery rejected.");
    }
    internal static void CommitNext()
    {
        if (Native.Rename(NextPath, StatePath) != 0) throw new IOException("Circuit atomic replacement failed.");
        using var directory = new SafeFileHandle(Native.Open(DirectoryPath, 0x10000 | 0x20000 | 0x80000, 0), ownsHandle: true);
        if (directory.IsInvalid || Native.Sync(directory) != 0) throw new IOException("Circuit directory persistence failed.");
    }
    private static FileStream Open(string path, bool write, bool exclusive)
    {
        var descriptor = Native.Open(path, (write ? 2 | 64 : 0) | (exclusive ? 128 : 0) | 0x20000 | 0x80000, 384);
        if (descriptor < 0) throw new IOException("Circuit state open failed.");
        var handle = new SafeFileHandle(descriptor, ownsHandle: true);
        if (Native.StatHandle(handle, out var value) != 0 || !ValidState(value))
        { handle.Dispose(); throw new IOException("Circuit state identity rejected."); }
        return new FileStream(handle, write ? FileAccess.ReadWrite : FileAccess.Read, 8, isAsync: false);
    }
    private static bool ValidState(Stat value) => value.Mode == (0x8000 | 384) && value.User == 0 && value.Links == 1 && value.Size is >= 0 and <= 8;
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct Stat
    {
        [FieldOffset(16)] internal ulong Links;
        [FieldOffset(24)] internal uint Mode;
        [FieldOffset(28)] internal uint User;
        [FieldOffset(32)] internal uint Group;
        [FieldOffset(48)] internal long Size;
    }
    private static class Native
    {
        [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Rename(string from, string to);
        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Sync(SafeFileHandle file);
        [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Stat(string path, out Stat value);
        [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int StatHandle(SafeFileHandle handle, out Stat value);
        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Open(string path, int flags, uint mode);
        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Chmod(string path, uint mode);
        [DllImport("libc", EntryPoint = "lchown", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int ChangeOwner(string path, uint user, uint group);
        [DllImport("libc", EntryPoint = "unlink", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Unlink(string path);
    }
}
