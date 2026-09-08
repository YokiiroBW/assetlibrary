using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

[SupportedOSPlatform("linux")]
internal static class LinuxImageSourceHandle
{
    private static readonly Encoding PathEncoding = new UTF8Encoding(false, true);

    public static SafeFileHandle Open(SafeFileHandle? parent, string component, bool directory)
    {
        // O_NONBLOCK prevents a FIFO from hanging before its type can be rejected.
        const int noFollowCloseOnExec = 0x20000 | 0x80000;
        var flags = noFollowCloseOnExec | (directory ? 0x10000 : 0x800);
        var descriptor = Native.OpenAt(parent is null ? -100 : parent.DangerousGetHandle().ToInt32(),
            PathEncoding.GetBytes(component + '\0'), flags);
        if (descriptor < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new ReadOnlyWorkerException(error is 2 or 20 or 40 ? "preview_source_changed" : "preview_source_unavailable", error);
        }
        var handle = new SafeFileHandle(descriptor, ownsHandle: true);
        try
        {
            if (Observe(handle).IsDirectory != directory)
            {
                throw new ReadOnlyWorkerException("preview_source_changed");
            }

            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public static ImageSourceStamp Observe(SafeFileHandle handle)
    {
        // statx has a fixed Linux ABI; do not guess the libc-specific struct stat layout.
        const uint required = 0x1 | 0x40 | 0x80 | 0x100 | 0x200;
        if (Native.Stat(handle, [0], 0x1000 | 0x100, required, out var stat) != 0
            || (stat.Mask & required) != required || (stat.Mode & 0xf000) is not (0x8000 or 0x4000)
            || stat.ModifiedNanoseconds >= 1_000_000_000 || stat.ChangeNanoseconds >= 1_000_000_000)
        {
            throw new ReadOnlyWorkerException("preview_source_unavailable");
        }

        return new ImageSourceStamp(((ulong)stat.DeviceMajor << 32) | stat.DeviceMinor, stat.Inode,
            checked((long)stat.Size), DateTimeOffset.FromUnixTimeSeconds(stat.ModifiedSeconds).AddTicks(stat.ModifiedNanoseconds / 100),
            stat.ChangeSeconds, stat.ChangeNanoseconds, stat.ModifiedNanoseconds, (stat.Mode & 0xf000) == 0x4000);
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Statx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(96)] public long ChangeSeconds;
        [FieldOffset(104)] public uint ChangeNanoseconds;
        [FieldOffset(112)] public long ModifiedSeconds;
        [FieldOffset(120)] public uint ModifiedNanoseconds;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "openat", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int OpenAt(int directory, [In] byte[] name, int flags);

        [DllImport("libc", EntryPoint = "statx", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int Stat(SafeFileHandle handle, [In] byte[] name,
            int flags, uint mask, out Statx information);
    }
}
