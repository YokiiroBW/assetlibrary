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
        if (!Native.OpenProcessToken(new nint(-1), 8 | 2, out var token)) return false;
        using (token)
        {
            return FirstInteger(token, 29) == 1 // TokenIsAppContainer
                && HasExactCompatibilityCapability(token) // Only lpacCom for .NET's mandatory finalizer initialization.
                && IgnoresAllApplicationPackages(token)
                && WindowsWorkerJob.CurrentProcessHasLimits(512U * 1024 * 1024, TimeSpan.FromSeconds(3).Ticks, 1);
        }
    }

    private static bool IgnoresAllApplicationPackages(SafeAccessTokenHandle token)
    {
        // Some supported Windows builds reject information class 46. Test its security meaning:
        // an explicit package grant must work, while ALL_APPLICATION_PACKAGES must not grant access.
        var user = ReadSid(token, 1);
        var package = ReadSid(token, 31);
        if (user is null || package is null
            || !Native.DuplicateTokenEx(token, 8, nint.Zero, 2, 2, out var impersonation)) return false;
        using (impersonation)
        {
            return CheckGrant(impersonation, user, package, expected: true)
                && CheckGrant(impersonation, user, "S-1-15-2-1", expected: false);
        }
    }

    private static string? ReadSid(SafeAccessTokenHandle token, int informationClass)
    {
        _ = Native.GetTokenInformation(token, informationClass, nint.Zero, 0, out var required);
        if (required < nint.Size || required > 16384) return null;
        var buffer = Marshal.AllocHGlobal(checked((int)required));
        nint text = nint.Zero;
        try
        {
            if (!Native.GetTokenInformation(token, informationClass, buffer, required, out _)) return null;
            var sid = Marshal.ReadIntPtr(buffer);
            if (sid == nint.Zero || !Native.ConvertSidToStringSidW(sid, out text)) return null;
            return Marshal.PtrToStringUni(text);
        }
        finally
        {
            if (text != nint.Zero) _ = Native.LocalFree(text);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool CheckGrant(SafeAccessTokenHandle token, string user, string package, bool expected)
    {
        // Bit 1 is an object-specific read bit, not an implicit owner/privilege right.
        var sddl = $"O:SYG:SYD:(A;;0x1;;;{user})(A;;0x1;;;{package})";
        if (!Native.ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var descriptor, nint.Zero)) return false;
        nint privileges = nint.Zero;
        try
        {
            privileges = Marshal.AllocHGlobal(512);
            uint bytes = 512;
            var mapping = new GenericMapping { Read = 1, Write = 2, Execute = 4, All = 7 };
            return Native.AccessCheck(descriptor, token, 1, ref mapping, privileges, ref bytes, out var granted, out var allowed)
                && allowed == expected && granted == (expected ? 1U : 0U);
        }
        finally
        {
            if (privileges != nint.Zero) Marshal.FreeHGlobal(privileges);
            _ = Native.LocalFree(descriptor);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GenericMapping
    {
        public uint Read;
        public uint Write;
        public uint Execute;
        public uint All;
    }

    private static bool HasExactCompatibilityCapability(SafeAccessTokenHandle token)
    {
        _ = Native.GetTokenInformation(token, 30, nint.Zero, 0, out var required);
        if (required is < 24 or > 16384) return false;
        var buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!Native.GetTokenInformation(token, 30, buffer, required, out _) || Marshal.ReadInt32(buffer) != 1) return false;
            using var expected = WindowsImageCapability.Create();
            return expected.Matches(Marshal.ReadIntPtr(buffer, 8)) && (Marshal.ReadInt32(buffer, 16) & 4) != 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
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
        public static extern bool DuplicateTokenEx(SafeAccessTokenHandle existing, uint access, nint attributes,
            int level, int type, out SafeAccessTokenHandle token);

        [DllImport("advapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision,
            out nint descriptor, nint size);

        [DllImport("advapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ConvertSidToStringSidW(nint sid, out nint text);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AccessCheck(nint descriptor, SafeAccessTokenHandle token, uint desired,
            ref GenericMapping mapping, nint privileges, ref uint bytes, out uint granted,
            [MarshalAs(UnmanagedType.Bool)] out bool allowed);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern nint LocalFree(nint memory);

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
