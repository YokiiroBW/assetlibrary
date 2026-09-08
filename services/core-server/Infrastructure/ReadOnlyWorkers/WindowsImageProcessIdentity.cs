using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

[SupportedOSPlatform("windows")]
internal static class WindowsImageProcessIdentity
{
    public static bool WaitForExit(SafeProcessHandle process, uint milliseconds)
    {
        var result = Native.WaitForSingleObject(process, milliseconds);
        if (result is not 0 and not 258)
            throw new ReadOnlyWorkerException("preview_process_cleanup_unconfirmed", Marshal.GetLastPInvokeError());
        return result == 0;
    }

    public static long CreationTime(nint handle)
    {
        if (!Native.GetProcessTimes(handle, out var creation, out _, out _, out _))
            throw new ReadOnlyWorkerException("preview_process_identity_unavailable", Marshal.GetLastPInvokeError());
        return creation;
    }

    public static bool HasExited(uint processId, long processCreation, nint sid)
    {
        if (processId != 0)
        {
            using var handle = Native.OpenProcess(0x1000 | 0x00100000, false, processId);
            if (handle.IsInvalid)
            {
                return Marshal.GetLastPInvokeError() == 87;
            }
            return Native.GetProcessTimes(handle.DangerousGetHandle(), out var created, out _, out _, out _)
                && (created != processCreation || Native.WaitForSingleObject(handle, 0) == 0);
        }
        // A crash can occur after native creation but before journaling the PID. Only inspect
        // bounded matching worker processes, compare their package SID, and never terminate them.
        var workers = Process.GetProcessesByName("AssetLibrary.ImagePreview.Worker");
        try
        {
            if (workers.Length > 64) return false;
            foreach (var worker in workers)
            {
                using var handle = Native.OpenProcess(0x1000 | 0x00100000, false, checked((uint)worker.Id));
                if (handle.IsInvalid)
                {
                    if (Marshal.GetLastPInvokeError() == 87) continue;
                    return false;
                }
                if (Native.WaitForSingleObject(handle, 0) == 0) continue;
                if (!Native.OpenProcessToken(handle, 8, out var token)) return false;
                using (token)
                {
                    _ = Native.GetTokenInformation(token, 31, nint.Zero, 0, out var size);
                    if (size < nint.Size || size > 16384) return false;
                    var buffer = Marshal.AllocHGlobal(checked((int)size));
                    try
                    {
                        if (!Native.GetTokenInformation(token, 31, buffer, size, out _)) return false;
                        var candidate = Marshal.ReadIntPtr(buffer);
                        if (candidate != nint.Zero && Native.EqualSid(sid, candidate)) return false;
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            return true;
        }
        finally { foreach (var worker in workers) worker.Dispose(); }
    }


    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, nint data, uint length, out uint returned);

        [DllImport("advapi32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EqualSid(nint first, nint second);

    }
}
