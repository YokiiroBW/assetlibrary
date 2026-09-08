using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AssetLibrary.ImagePreview.Worker;

[SupportedOSPlatform("linux")]
internal static class LinuxImageIsolation
{
    private const uint Allow = 0x7fff0000;
    private const uint Deny = 0x00050001;

    public static bool Enter()
    {
        // Only the fixed NativeAOT executable has a small, measurable address-space budget.
        if (RuntimeFeature.IsDynamicCodeSupported || RuntimeInformation.ProcessArchitecture != Architecture.X64 || !Unprivileged())
        {
            return false;
        }

        var parentId = Native.GetParentPid();
        if (Native.Prctl(1, 9, 0, 0, 0) != 0 || Native.GetParentPid() != parentId
            || !Limit(9, 512UL * 1024 * 1024) || !Limit(0, 3) || !Limit(1, 0) || !Limit(4, 0) || !Limit(6, 256)
            || Native.Prctl(38, 1, 0, 0, 0) != 0)
        {
            return false;
        }

        var instructions = Policy(Native.GetPid());
        var pinned = GCHandle.Alloc(instructions, GCHandleType.Pinned);
        try
        {
            var program = new FilterProgram { Length = checked((ushort)instructions.Length), Instructions = pinned.AddrOfPinnedObject() };
            // A positive TSYNC result identifies an unsynchronizable thread and is also failure.
            return Native.Seccomp(317, 1, 1, ref program) == 0;
        }
        finally
        {
            pinned.Free();
        }
    }

    internal static int CurrentMode() => Native.Prctl(21, 0, 0, 0, 0);
    internal static int NoNewPrivileges() => Native.Prctl(39, 0, 0, 0, 0);

    private static bool Unprivileged()
    {
        if (Native.GetUserId() == 0 || Native.GetEffectiveUserId() == 0) return false;
        var header = new CapabilityHeader { Version = 0x20080522 };
        return Native.Capabilities(125, ref header, out var capabilities) == 0
            && (capabilities.EffectiveLow | capabilities.PermittedLow | capabilities.InheritableLow
                | capabilities.EffectiveHigh | capabilities.PermittedHigh | capabilities.InheritableHigh) == 0;
    }

    private static bool Limit(int resource, ulong ceiling)
    {
        if (Native.GetLimit(resource, out var previous) != 0) return false;
        var limit = new ResourceLimit { Current = Math.Min(previous.Current, ceiling), Maximum = Math.Min(previous.Maximum, ceiling) };
        return Native.SetLimit(resource, ref limit) == 0 && Native.GetLimit(resource, out var applied) == 0
            && applied.Current <= ceiling && applied.Maximum <= ceiling;
    }

    private static FilterInstruction[] Policy(int processId)
    {
        List<FilterInstruction> rules =
        [
            Load(4), Jump(0xc000003e, 1, 0), Return(0x80000000),
            Load(0), new(0x45, 0, 1, 0x40000000), Return(0x80000000),
        ];
        OnlyArgument(rules, 0, 16, [0]); // read: only the supplied input pipe
        OnlyArgument(rules, 1, 16, [1, 2]); // write and writev: only bounded parent-owned output pipes
        OnlyArgument(rules, 20, 16, [1, 2]);
        OnlyArgument(rules, 5, 16, [0, 1, 2]);
        OnlyArgument(rules, 8, 16, [0, 1, 2]);
        OnlyArgument(rules, 62, 16, [checked((uint)processId)]); // kill(self)
        OnlyArgument(rules, 234, 16, [checked((uint)processId)]); // tgkill(self group, thread)
        OnlyArgument(rules, 157, 16, [21, 39]); // Read back seccomp/no_new_privs, never change process policy.
        DescriptorControl(rules);
        AnonymousMemoryOnly(rules);
        QueryLimitsOnly(rules, processId);
        ThreadCloneOnly(rules);
        rules.Add(Jump(435, 0, 1)); // clone3's pointer arguments cannot be inspected; let libc use clone.
        rules.Add(Return(0x00050026)); // ENOSYS

        // No open/stat-by-path, network, ioctl, exec, cross-process access, namespace or io_uring calls.
        foreach (var number in new uint[]
        {
            3, 10, 11, 12, 13, 14, 15, 24, 25, 28, 35, 39, 60, 63, 96, 97,
            102, 104, 107, 108, 131, 158, 186, 201, 202, 218, 228, 230, 231, 273, 318, 334,
        })
        {
            rules.Add(Jump(number, 0, 1));
            rules.Add(Return(Allow));
        }

        rules.Add(Return(Deny));
        return [.. rules];
    }

    private static void DescriptorControl(List<FilterInstruction> rules)
    {
        FilterInstruction[] block =
        [
            Load(16), new(0x25, 0, 1, 2), Return(Deny),
            Load(24), Jump(1, 0, 1), Return(Allow), Jump(2, 0, 1), Return(Allow),
            Jump(3, 0, 1), Return(Allow), Jump(4, 0, 1), Return(Allow), Return(Deny),
        ];
        rules.Add(Jump(72, 0, checked((byte)block.Length)));
        rules.AddRange(block);
    }

    private static void AnonymousMemoryOnly(List<FilterInstruction> rules)
    {
        FilterInstruction[] block = [Load(40), new(0x45, 1, 0, 0x20), Return(Deny), Return(Allow)];
        rules.Add(Jump(9, 0, checked((byte)block.Length)));
        rules.AddRange(block);
    }

    private static void OnlyArgument(List<FilterInstruction> rules, uint syscall, uint offset, uint[] values)
    {
        rules.Add(Jump(syscall, 0, checked((byte)(2 * values.Length + 2))));
        rules.Add(Load(offset));
        foreach (var value in values)
        {
            rules.Add(Jump(value, 0, 1));
            rules.Add(Return(Allow));
        }

        rules.Add(Return(Deny));
    }

    private static void QueryLimitsOnly(List<FilterInstruction> rules, int processId)
    {
        FilterInstruction[] block =
        [
            Load(32), Jump(0, 1, 0), Return(Deny),
            Load(36), Jump(0, 1, 0), Return(Deny), // new_limit must be NULL, both halves
            Load(16), Jump(0, 0, 1), Return(Allow), Jump(checked((uint)processId), 0, 1), Return(Allow), Return(Deny),
        ];
        rules.Add(Jump(302, 0, checked((byte)block.Length)));
        rules.AddRange(block);
    }

    private static void ThreadCloneOnly(List<FilterInstruction> rules)
    {
        const uint allowedFlags = 0x003d0f00;
        const uint requiredFlags = 0x00010900;
        FilterInstruction[] block =
        [
            Load(20), Jump(0, 1, 0), Return(Deny),
            Load(16), new(0x54, 0, 0, ~allowedFlags), Jump(0, 1, 0), Return(Deny),
            Load(16), new(0x54, 0, 0, requiredFlags), Jump(requiredFlags, 1, 0), Return(Deny), Return(Allow),
        ];
        rules.Add(Jump(56, 0, checked((byte)block.Length)));
        rules.AddRange(block);
    }

    private static FilterInstruction Load(uint offset) => new(0x20, 0, 0, offset);
    private static FilterInstruction Jump(uint value, byte yes, byte no) => new(0x15, yes, no, value);
    private static FilterInstruction Return(uint action) => new(0x06, 0, 0, action);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FilterInstruction(ushort code, byte yes, byte no, uint value)
    {
        public readonly ushort Code = code;
        public readonly byte Yes = yes;
        public readonly byte No = no;
        public readonly uint Value = value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterProgram
    {
        public ushort Length;
        public nint Instructions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ResourceLimit
    {
        public ulong Current;
        public ulong Maximum;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CapabilityHeader
    {
        public uint Version;
        public int ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CapabilityData
    {
        public uint EffectiveLow;
        public uint PermittedLow;
        public uint InheritableLow;
        public uint EffectiveHigh;
        public uint PermittedHigh;
        public uint InheritableHigh;
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "getpid", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int GetPid();

        [DllImport("libc", EntryPoint = "getppid", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int GetParentPid();

        [DllImport("libc", EntryPoint = "getuid", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern uint GetUserId();

        [DllImport("libc", EntryPoint = "geteuid", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern uint GetEffectiveUserId();

        [DllImport("libc", EntryPoint = "syscall", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern nint Capabilities(nint number, ref CapabilityHeader header, out CapabilityData capabilities);

        [DllImport("libc", EntryPoint = "prctl", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int Prctl(int option, nuint a, nuint b, nuint c, nuint d);

        [DllImport("libc", EntryPoint = "syscall", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern nint Seccomp(nint number, uint operation, uint flags, ref FilterProgram program);

        [DllImport("libc", EntryPoint = "setrlimit", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int SetLimit(int resource, ref ResourceLimit limit);

        [DllImport("libc", EntryPoint = "getrlimit", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int GetLimit(int resource, out ResourceLimit limit);
    }
}
