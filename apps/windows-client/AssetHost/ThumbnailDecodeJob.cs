using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Windows.AssetHost;

[SupportedOSPlatform("windows")]
internal sealed class ThumbnailDecodeJob : IDisposable
{
    internal SafeFileHandle Handle { get; }
    internal ThumbnailDecodeJob()
    {
        if (!Environment.Is64BitProcess) { throw new IOException("Thumbnail helper requires x64."); }
        Handle = CreateJobObject(IntPtr.Zero, IntPtr.Zero);
        if (Handle.IsInvalid) { Handle.Dispose(); throw new IOException("Thumbnail job creation failed."); }
        var limits = new Limits { Flags = 0x2308, Processes = 1, ProcessBytes = 128 * 1024 * 1024, JobBytes = 128 * 1024 * 1024 };
        if (!SetInformationJobObject(Handle, 9, in limits, 144)) { Handle.Dispose(); throw new IOException("Thumbnail job limits failed."); }
    }
    internal static bool CurrentIsBounded() => QueryInformationJobObject(IntPtr.Zero, 9, out var limits, 144, IntPtr.Zero)
        && (limits.Flags & 0x2308) == 0x2308 && limits.Processes == 1
        && limits.ProcessBytes is > 0 and <= 128 * 1024 * 1024 && limits.JobBytes is > 0 and <= 128 * 1024 * 1024;
    internal void Terminate() => _ = TerminateJobObject(Handle, 2);
    internal static bool SuccessfulExit(SafeProcessHandle process) => GetExitCodeProcess(process, out var code) && code == 0;
    internal static async Task WaitForExitAsync(SafeProcessHandle process, CancellationToken token)
    {
        while (true)
        {
            var state = WaitForSingleObject(process, 0);
            if (state == 0) { return; }
            if (state != 258) { throw new IOException("Image child wait failed."); }
            await Task.Delay(10, token).ConfigureAwait(false);
        }
    }
    public void Dispose() => Handle.Dispose();

    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION layout for the frozen Windows x64 target.
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct Limits
    {
        [FieldOffset(16)] internal uint Flags;
        [FieldOffset(40)] internal uint Processes;
        [FieldOffset(112)] internal ulong ProcessBytes;
        [FieldOffset(120)] internal ulong JobBytes;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, IntPtr name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, in Limits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(IntPtr job, int kind, out Limits limits, uint length, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint timeout);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
}
