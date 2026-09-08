using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AssetLibrary.Infrastructure.ReadOnlyWorkers;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.ImagePreview.Worker;

[SupportedOSPlatform("windows")]
internal static class WindowsImageIsolation
{
    public static bool IsEnforced()
    {
        if (!Native.OpenProcessToken(new nint(-1), 8, out var token)) return false;
        using (token)
        {
            return FirstInteger(token, 29) == 1 // TokenIsAppContainer
                && FirstInteger(token, 30) == 0 // No declared network or other capabilities
                && FirstInteger(token, 46) == 1 // TokenIsLessPrivilegedAppContainer
                && WindowsWorkerJob.CurrentProcessHasLimits(512U * 1024 * 1024, TimeSpan.FromSeconds(3).Ticks, 1);
        }
    }

    private static int FirstInteger(SafeAccessTokenHandle token, int informationClass)
    {
        _ = Native.GetTokenInformation(token, informationClass, nint.Zero, 0, out var required);
        if (required is < 4 or > 16384) return -1;
        var buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            return Native.GetTokenInformation(token, informationClass, buffer, required, out _)
                ? Marshal.ReadInt32(buffer) : -1;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static class Native
    {
        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
            nint information, uint informationLength, out uint returnedLength);
    }
}
