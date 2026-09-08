using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

[SupportedOSPlatform("windows")]
internal static class WindowsImageSourceHandle
{
    public static SafeFileHandle Open(string path, bool directory)
    {
        // Retaining every ancestor without write/delete sharing closes path replacement races.
        var handle = Native.Open(path, 0x80000000, 1, nint.Zero, 3, 0x02200000, nint.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new ReadOnlyWorkerException("preview_source_unavailable");
        }

        try
        {
            var stamp = Observe(handle);
            if (stamp.IsDirectory != directory)
            {
                throw new ReadOnlyWorkerException("preview_source_unavailable");
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
        if (!Native.Information(handle, out var information) || Native.Type(handle) != 1
            || (information.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
        {
            throw new ReadOnlyWorkerException("preview_source_unavailable");
        }

        return new ImageSourceStamp(information.Volume, Join(information.IndexHigh, information.IndexLow),
            checked((long)Join(information.LengthHigh, information.LengthLow)),
            DateTimeOffset.FromFileTime(checked((long)Join(information.ModifiedHigh, information.ModifiedLow))),
            checked((long)Join(information.CreatedHigh, information.CreatedLow)), 0, 0,
            (information.Attributes & (uint)FileAttributes.Directory) != 0);
    }

    private static ulong Join(uint high, uint low) => ((ulong)high << 32) | low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public uint CreatedLow;
        public uint CreatedHigh;
        public uint AccessedLow;
        public uint AccessedHigh;
        public uint ModifiedLow;
        public uint ModifiedHigh;
        public uint Volume;
        public uint LengthHigh;
        public uint LengthLow;
        public uint LinkCount;
        public uint IndexHigh;
        public uint IndexLow;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern SafeFileHandle Open(string path, uint access, uint sharing, nint security,
            uint disposition, uint flags, nint template);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Information(SafeFileHandle handle, out FileInformation information);

        [DllImport("kernel32.dll", EntryPoint = "GetFileType", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint Type(SafeFileHandle handle);
    }
}
