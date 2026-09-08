using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

internal sealed class WindowsWorkerJob(SafeFileHandle handle) : IDisposable
{
    internal SafeFileHandle Handle => handle;
    private const uint KillOnJobClose = 0x00002000;
    private const int ExtendedLimitInformationClass = 9;

    [SupportedOSPlatform("windows")]
    public static WindowsWorkerJob Create(nuint memoryBytes = 0, long cpuTicks = 0, uint activeProcesses = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cpuTicks);
        // An unnamed, non-inheritable handle makes parent process lifetime the job's lifetime.
        var job = NativeMethods.CreateJobObjectW(nint.Zero, nint.Zero);
        if (job.IsInvalid)
        {
            job.Dispose();
            throw new ReadOnlyWorkerException("worker_job_creation_failed");
        }

        try
        {
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
            if (memoryBytes != 0)
            {
                limits.Basic.LimitFlags |= 0x100;
                limits.ProcessMemoryLimit = memoryBytes;
            }
            if (cpuTicks != 0)
            {
                limits.Basic.LimitFlags |= 2;
                limits.Basic.PerProcessUserTimeLimit = cpuTicks;
            }
            if (activeProcesses != 0)
            {
                limits.Basic.LimitFlags |= 8;
                limits.Basic.ActiveProcessLimit = activeProcesses;
            }
            if (!NativeMethods.SetInformationJobObject(job, ExtendedLimitInformationClass,
                ref limits, checked((uint)Marshal.SizeOf<ExtendedLimits>())))
            {
                throw new ReadOnlyWorkerException("worker_job_configuration_failed");
            }

            return new WindowsWorkerJob(job);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    public static bool CurrentProcessHasLimits(nuint maximumMemory, long maximumCpuTicks, uint maximumProcesses)
    {
        const uint requiredFlags = KillOnJobClose | 0x100 | 2 | 8;
        return NativeMethods.QueryInformationJobObject(nint.Zero, ExtendedLimitInformationClass,
            out var limits, checked((uint)Marshal.SizeOf<ExtendedLimits>()), nint.Zero)
            && (limits.Basic.LimitFlags & requiredFlags) == requiredFlags
            && limits.ProcessMemoryLimit > 0 && limits.ProcessMemoryLimit <= maximumMemory
            && limits.Basic.PerProcessUserTimeLimit > 0 && limits.Basic.PerProcessUserTimeLimit <= maximumCpuTicks
            && limits.Basic.ActiveProcessLimit > 0 && limits.Basic.ActiveProcessLimit <= maximumProcesses;
    }

    [SupportedOSPlatform("windows")]
    public void Assign(SafeProcessHandle process)
    {
        if (!NativeMethods.AssignProcessToJobObject(handle, process))
        {
            throw new ReadOnlyWorkerException("worker_job_assignment_failed");
        }
    }

    public void Dispose()
    {
        handle.Dispose();
        GC.SuppressFinalize(this);
    }

    [SupportedOSPlatform("windows")]
    public void Terminate() => _ = NativeMethods.TerminateJobObject(handle, 1);

    // Same Windows SDK layout already exercised by M0-007 windows_isolation_probe.py.
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern SafeFileHandle CreateJobObjectW(nint attributes, nint name);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass,
            ref ExtendedLimits information, uint informationLength);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryInformationJobObject(nint job, int informationClass,
            out ExtendedLimits information, uint informationLength, nint returnedLength);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    }
}
