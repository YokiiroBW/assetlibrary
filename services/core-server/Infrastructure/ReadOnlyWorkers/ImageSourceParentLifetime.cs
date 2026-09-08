using System.Runtime.InteropServices;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

internal static class ImageSourceParentLifetime
{
    public static void Require()
    {
        if (OperatingSystem.IsWindows()) return;
        if (!OperatingSystem.IsLinux()) throw new ReadOnlyWorkerException("preview_source_platform_unavailable");
        var parent = Native.GetParentPid();
        if (Native.Prctl(1, 9, 0, 0, 0) != 0 || Native.GetParentPid() != parent)
        {
            throw new ReadOnlyWorkerException("preview_source_lifetime_unavailable");
        }
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "getppid", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int GetParentPid();

        [DllImport("libc", EntryPoint = "prctl", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int Prctl(int option, nuint first, nuint second, nuint third, nuint fourth);
    }
}
