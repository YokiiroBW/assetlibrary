using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AssetLibrary.ImagePreview.Isolation;

namespace AssetLibrary.ImagePreview.Worker;

[SupportedOSPlatform("linux")]
internal static class LinuxContainerIsolation
{
    internal static bool Enter() => !RuntimeFeature.IsDynamicCodeSupported && RuntimeInformation.ProcessArchitecture == Architecture.X64
        && LinuxContainerIdentity.Decoder() && LinuxImageIsolation.AddressSpaceFits()
        && LinuxImageIsolation.Limit(9, 512UL * 1024 * 1024) && LinuxImageIsolation.Limit(0, 3)
        && LinuxImageIsolation.Limit(1, 0) && LinuxImageIsolation.Limit(4, 0)
        && LinuxImageIsolation.Limit(6, 1) && LinuxImageIsolation.Limit(8, 0)
        && DescriptorsArePrivate() && RingCreationError() == 12;

    internal static int RingCreationError()
    {
        // Valid zero-initialized io_uring_params: this must fail due to MEMLOCK=0, not malformed arguments.
        var parameters = Marshal.AllocHGlobal(120);
        try
        {
            Marshal.Copy(new byte[120], 0, parameters, 120);
            var descriptor = Native.Call(425, 1, parameters, 0);
            var error = Marshal.GetLastPInvokeError();
            if (descriptor >= 0) { _ = Native.Call(3, descriptor, 0, 0); return 0; }
            return error;
        }
        finally { Marshal.FreeHGlobal(parameters); }
    }
    private static bool DescriptorsArePrivate()
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
        {
            var target = new FileInfo(entry).LinkTarget;
            if (target is null) continue; // The enumeration descriptor can close before its own readlink.
            if (target.StartsWith("socket:", StringComparison.Ordinal) || target.Contains("io_uring", StringComparison.Ordinal)) return false;
        }
        return Enumerable.Range(0, 3).All(fd => new FileInfo("/proc/self/fd/" + fd).LinkTarget?.StartsWith("pipe:", StringComparison.Ordinal) == true);
    }
    internal static int DeniedFork()
    {
        var child = Native.Call(57, 0, 0, 0); var error = Marshal.GetLastPInvokeError();
        if (child == 0) Native.Exit(99);
        if (child > 0) { _ = Native.Call(61, child, 0, 0); return 0; }
        return error;
    }
    internal static int ParentSignalError()
    {
        var result = Native.Call(62, 1, 0, 0);
        return result == -1 ? Marshal.GetLastPInvokeError() : 0;
    }
    private static class Native
    {
        [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern nint Call(nint number, nint a, nint b, nint c);
        [DllImport("libc", EntryPoint = "_exit")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern void Exit(int code);
    }
}
