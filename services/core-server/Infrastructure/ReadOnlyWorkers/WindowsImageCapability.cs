using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

[SupportedOSPlatform("windows")]
internal sealed class WindowsImageCapability(nint sid) : IDisposable
{
    public nint Sid { get; private set; } = sid;

    public static WindowsImageCapability Create()
    {
        if (!Native.DeriveCapabilitySidsFromName("lpacCom", out var groups, out var groupCount, out var capabilities, out var count))
        {
            throw new ReadOnlyWorkerException("preview_compatibility_sid_failed", Marshal.GetLastPInvokeError());
        }
        nint selected = nint.Zero;
        try
        {
            if (count != 1 || groupCount > 16 || capabilities == nint.Zero)
            {
                throw new ReadOnlyWorkerException("preview_compatibility_sid_failed");
            }
            selected = Marshal.ReadIntPtr(capabilities);
            return new WindowsImageCapability(selected);
        }
        finally
        {
            FreeCollection(groups, groupCount, nint.Zero);
            FreeCollection(capabilities, count, selected);
        }
    }

    public bool Matches(nint candidate) => Native.EqualSid(Sid, candidate);

    private static void FreeCollection(nint values, uint count, nint retained)
    {
        if (values == nint.Zero) return;
        for (var index = 0; index < Math.Min(count, 16); index++)
        {
            var value = Marshal.ReadIntPtr(values, checked((int)index * nint.Size));
            if (value != nint.Zero && value != retained) _ = Native.LocalFree(value);
        }
        _ = Native.LocalFree(values);
    }

    public void Dispose()
    {
        if (Sid != nint.Zero) _ = Native.LocalFree(Sid);
        Sid = nint.Zero;
        GC.SuppressFinalize(this);
    }

    private static class Native
    {
        [DllImport("kernelbase.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeriveCapabilitySidsFromName(string name, out nint groups, out uint groupCount,
            out nint capabilities, out uint count);

        [DllImport("advapi32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EqualSid(nint first, nint second);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern nint LocalFree(nint memory);
    }
}
