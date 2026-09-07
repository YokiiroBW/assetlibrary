using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

internal sealed class WindowsWorkerJob(SafeFileHandle handle) : IDisposable
{
    private const uint KillOnJobClose = 0x00002000;
    private const int ExtendedLimitInformationClass = 9;

    [SupportedOSPlatform("windows")]
    public static WindowsWorkerJob Create()
    {
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
    }
}
