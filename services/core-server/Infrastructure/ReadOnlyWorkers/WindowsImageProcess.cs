using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

[SupportedOSPlatform("windows")]
internal sealed class WindowsImageProcess : IAsyncDisposable
{
    private readonly WindowsImageProfile profile;
    private readonly WindowsWorkerJob job;
    private readonly CancellationTokenRegistration cancellation;
    private readonly SafeProcessHandle processHandle;
    private bool disposed;
    public Process Process { get; }
    public AnonymousPipeServerStream Input { get; }
    public AnonymousPipeServerStream Output { get; }
    public AnonymousPipeServerStream Error { get; }
    public int ExitCode => Native.GetExitCodeProcess(processHandle, out var code)
        ? unchecked((int)code) : throw new ReadOnlyWorkerException("preview_exit_status_unavailable", Marshal.GetLastPInvokeError());

    private WindowsImageProcess(WindowsImageProfile profile, WindowsWorkerJob job, Process process, SafeProcessHandle processHandle,
        AnonymousPipeServerStream input, AnonymousPipeServerStream output, AnonymousPipeServerStream error,
        CancellationToken token)
    {
        this.profile = profile;
        this.job = job;
        Process = process;
        this.processHandle = processHandle;
        Input = input;
        Output = output;
        Error = error;
        // Anonymous-pipe I/O can be synchronous. Termination independently unblocks it at the deadline.
        cancellation = token.Register(static state => ((WindowsWorkerJob)state!).Terminate(), job);
    }

    public static WindowsImageProcess Start(string executable, string stateDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var profile = WindowsImageProfile.Create(executable, stateDirectory);
        WindowsWorkerJob? job = null;
        AnonymousPipeServerStream? input = null;
        AnonymousPipeServerStream? output = null;
        AnonymousPipeServerStream? error = null;
        Process? process = null;
        SafeProcessHandle? processHandle = null;
        try
        {
            job = WindowsWorkerJob.Create(512U * 1024 * 1024, TimeSpan.FromSeconds(3).Ticks, 1);
            input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            nint[] handles = [input.ClientSafePipeHandle.DangerousGetHandle(), output.ClientSafePipeHandle.DangerousGetHandle(), error.ClientSafePipeHandle.DangerousGetHandle()];
            using var startup = new WindowsImageStartup(profile.Sid, job.Handle.DangerousGetHandle(), handles);
            token.ThrowIfCancellationRequested();
            var native = startup.Start(profile, handles);
            processHandle = new SafeProcessHandle(native.Process, ownsHandle: true);
            using var threadHandle = new SafeFileHandle(native.Thread, ownsHandle: true);
            process = Process.GetProcessById(checked((int)native.ProcessId));
            // JOB_LIST binds at creation, eliminating the otherwise unowned suspended-process window.
            if (!Native.IsProcessInJob(processHandle, job.Handle, out var assigned) || !assigned)
            {
                job.Terminate();
                throw new ReadOnlyWorkerException("preview_job_assignment_failed");
            }

            input.DisposeLocalCopyOfClientHandle();
            output.DisposeLocalCopyOfClientHandle();
            error.DisposeLocalCopyOfClientHandle();
            token.ThrowIfCancellationRequested();
            if (Native.ResumeThread(threadHandle) == uint.MaxValue)
            {
                throw new ReadOnlyWorkerException("preview_resume_failed");
            }

            return new WindowsImageProcess(profile, job, process, processHandle, input, output, error, token);
        }
        catch
        {
            job?.Terminate();
            if (process is not null)
            {
                _ = process.WaitForExit(2000);
                process.Dispose();
            }
            input?.Dispose();
            output?.Dispose();
            error?.Dispose();
            processHandle?.Dispose();
            job?.Dispose();
            profile.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await cancellation.DisposeAsync().ConfigureAwait(false);
        job.Terminate();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            job.Dispose();
            Input.Dispose();
            Output.Dispose();
            Error.Dispose();
            Process.Dispose();
            processHandle.Dispose();
            profile.Dispose();
        }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsProcessInJob(SafeProcessHandle process, SafeFileHandle job,
            [MarshalAs(UnmanagedType.Bool)] out bool result);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint ResumeThread(SafeFileHandle thread);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    }
}
