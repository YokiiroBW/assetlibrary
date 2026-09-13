using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AssetLibrary.ImagePreview.Isolation;

[SupportedOSPlatform("linux")]
internal static class LinuxContainerIdentity
{
    internal const uint DecoderId = 1655;
    internal const uint CoreId = 1654;
    private const uint SupervisorCapabilities = 1u | (1u << 5) | (1u << 6) | (1u << 7);
    internal static bool Supervisor() => Native.GetPid() == 1 && Native.GetUid() == 0 && Native.GetEuid() == 0
        && Native.Prctl(39, 0, 0, 0, 0) == 1 && Capabilities(SupervisorCapabilities);
    internal static bool DropDecoderIdentity()
    {
        if (Native.Prctl(27, 0, 0, 0, 0) != 0 || Native.GetParentPid() != 1 || Native.GetUid() != 0 || Native.GetEuid() != 0
            || Native.Prctl(39, 0, 0, 0, 0) != 1 || !Capabilities(SupervisorCapabilities)) return false;
        if (Native.SetGroups(0, IntPtr.Zero) != 0 || Native.SetResGid(DecoderId, DecoderId, DecoderId) != 0
            || Native.SetResUid(DecoderId, DecoderId, DecoderId) != 0) return false;
        var header = new CapabilityHeader { Version = 0x20080522 };
        var empty = new CapabilityData();
        return Native.SetCapabilities(ref header, ref empty) == 0 && Native.Prctl(4, 0, 0, 0, 0) == 0 && Decoder();
    }
    internal static bool Decoder() => Native.GetParentPid() == 1
        && Native.GetResUid(out var real, out var effective, out var saved) == 0 && real == DecoderId && effective == DecoderId && saved == DecoderId
        && Native.GetResGid(out var group, out var effectiveGroup, out var savedGroup) == 0 && group == DecoderId && effectiveGroup == DecoderId && savedGroup == DecoderId
        && Native.GetGroups(0, IntPtr.Zero) == 0 && Native.Prctl(39, 0, 0, 0, 0) == 1 && Capabilities(0);
    private static bool Capabilities(uint expected)
    {
        var header = new CapabilityHeader { Version = 0x20080522 };
        return Native.GetCapabilities(ref header, out var value) == 0 && value.EffectiveLow == expected && value.PermittedLow == expected
            && (value.EffectiveHigh | value.PermittedHigh | value.InheritableLow | value.InheritableHigh) == 0;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CapabilityHeader { internal uint Version; internal int ProcessId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CapabilityData
    {
        internal uint EffectiveLow; internal uint PermittedLow; internal uint InheritableLow;
        internal uint EffectiveHigh; internal uint PermittedHigh; internal uint InheritableHigh;
    }
    private static class Native
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "getpid")]
        internal static extern int GetPid();
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "getppid")]
        internal static extern int GetParentPid();
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "getuid")]
        internal static extern uint GetUid();
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "geteuid")]
        internal static extern uint GetEuid();
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "getresuid")]
        internal static extern int GetResUid(out uint real, out uint effective, out uint saved);
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "getresgid")]
        internal static extern int GetResGid(out uint real, out uint effective, out uint saved);
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "setresuid")]
        internal static extern int SetResUid(uint real, uint effective, uint saved);
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "setresgid")]
        internal static extern int SetResGid(uint real, uint effective, uint saved);
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "setgroups")]
        internal static extern int SetGroups(nuint size, IntPtr groups);
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "getgroups")]
        internal static extern int GetGroups(int size, IntPtr groups);
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "capget")]
        internal static extern int GetCapabilities(ref CapabilityHeader header, out CapabilityData data);
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "capset")]
        internal static extern int SetCapabilities(ref CapabilityHeader header, ref CapabilityData data);
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("libc", EntryPoint = "prctl")]
        internal static extern int Prctl(int option, nuint a, nuint b, nuint c, nuint d);
    }
}
