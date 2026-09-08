using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

[SupportedOSPlatform("windows")]
internal sealed class WindowsImageProcess : IImageChildProcess
{
    private readonly WindowsImageProfile profile;
    private WindowsWorkerJob? job;
    private CancellationTokenRegistration cancellation;
    private SafeProcessHandle? processHandle;
    private Process? process;
    private AnonymousPipeServerStream? input;
    private AnonymousPipeServerStream? output;
    private AnonymousPipeServerStream? error;
    private readonly object cleanupLock = new();
    private Task? cleanup;
    public Process Process => process ?? throw new InvalidOperationException("Image process has not started.");
    public AnonymousPipeServerStream Input => input ?? throw new InvalidOperationException("Image input has not started.");
    public AnonymousPipeServerStream Output => output ?? throw new InvalidOperationException("Image output has not started.");
    public AnonymousPipeServerStream Error => error ?? throw new InvalidOperationException("Image diagnostics have not started.");
    Stream IImageChildProcess.Input => Input;
    Stream IImageChildProcess.Output => Output;
    Stream IImageChildProcess.Error => Error;
    public int ExitCode => processHandle is not null && Native.GetExitCodeProcess(processHandle, out var code)
        ? unchecked((int)code) : throw new ReadOnlyWorkerException("preview_exit_status_unavailable", Marshal.GetLastPInvokeError());
    public Task WaitForExitAsync(CancellationToken cancellationToken) => Process.WaitForExitAsync(cancellationToken);

    private WindowsImageProcess(WindowsImageProfile profile, CancellationToken startupToken, CancellationToken lifetimeToken)
    {
        this.profile = profile;
        try
        {
            startupToken.ThrowIfCancellationRequested();
            job = WindowsWorkerJob.Create(512U * 1024 * 1024, TimeSpan.FromSeconds(3).Ticks, 1);
            cancellation = lifetimeToken.Register(static state => ((WindowsWorkerJob)state!).Terminate(), job);
            using var startupCancellation = startupToken.Register(static state => ((WindowsWorkerJob)state!).Terminate(), job);
            input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            nint[] handles = [input.ClientSafePipeHandle.DangerousGetHandle(), output.ClientSafePipeHandle.DangerousGetHandle(), error.ClientSafePipeHandle.DangerousGetHandle()];
            using var startup = new WindowsImageStartup(profile.Sid, job.Handle.DangerousGetHandle(), handles);
            startupToken.ThrowIfCancellationRequested();
            var native = startup.Start(profile, handles, startupToken);
            processHandle = new SafeProcessHandle(native.Process, ownsHandle: true);
            using var threadHandle = new SafeFileHandle(native.Thread, ownsHandle: true);
            profile.RecordProcess(native.ProcessId, native.Process);
            startupToken.ThrowIfCancellationRequested();
            process = Process.GetProcessById(checked((int)native.ProcessId));
            // JOB_LIST binds at creation, eliminating the otherwise unowned suspended-process window.
            if (!Native.IsProcessInJob(processHandle, job.Handle, out var assigned) || !assigned)
            {
                job.Terminate();
                throw new ReadOnlyWorkerException("preview_job_assignment_failed", Marshal.GetLastPInvokeError());
            }

            input.DisposeLocalCopyOfClientHandle();
            output.DisposeLocalCopyOfClientHandle();
            error.DisposeLocalCopyOfClientHandle();
            startupToken.ThrowIfCancellationRequested();
            if (Native.ResumeThread(threadHandle) == uint.MaxValue)
            {
                throw new ReadOnlyWorkerException("preview_resume_failed", Marshal.GetLastPInvokeError());
            }
        }
        catch (Exception failure)
        {
            try { DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (ImageChildCleanupPendingException pending)
            {
                WindowsImageProfile.PreserveCleanupFailure(failure, pending.InnerException ?? pending);
                throw new ImageChildCleanupPendingException(pending.Completion, failure);
            }
            throw;
        }
    }

    public static WindowsImageProcess Start(string executable, string stateDirectory, CancellationToken token)
    {
        using var startupDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        startupDeadline.CancelAfter(TimeSpan.FromSeconds(15));
        var profile = WindowsImageProfile.Create(executable, stateDirectory, startupDeadline.Token);
        return new WindowsImageProcess(profile, startupDeadline.Token, token);
    }

    // The broker retains its bounded admission slot until this task and any late cleanup finish.
    // Do not race it against cancellation here and abandon the non-cancellable Win32 API work.
    public static Task<WindowsImageProcess> StartAsync(string executable, string stateDirectory, CancellationToken token)
        => Task.Run(() => Start(executable, stateDirectory, token));

    internal static WindowsImageProcess Start(WindowsImageProfile profile, CancellationToken token)
        => new(profile, token, token);

    public async ValueTask DisposeAsync()
    {
        Task completion;
        lock (cleanupLock) completion = cleanup ??= CleanupAsync();
        try { await completion.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (Exception failure)
        {
            // Completion owns every original resource even if the caller's cleanup deadline expires.
            throw new ImageChildCleanupPendingException(completion, failure);
        }
    }

    private async Task CleanupAsync()
    {
        await cancellation.DisposeAsync().ConfigureAwait(false);
        job?.Terminate();
        var exited = processHandle is null;
        try
        {
            if (processHandle is not null)
            {
                exited = WindowsImageProcessIdentity.WaitForExit(processHandle, 0);
                if (!exited)
                {
                    _ = Native.TerminateProcess(processHandle, 1);
                    // No timeout may turn an unconfirmed exit into successful cleanup. The Core
                    // deadline returns to HTTP while retaining this operation's admission lease.
                    exited = await Task.Run(() => WindowsImageProcessIdentity.WaitForExit(processHandle, uint.MaxValue)).ConfigureAwait(false);
                }
                if (!exited) throw new ReadOnlyWorkerException("preview_process_cleanup_unconfirmed");
            }
        }
        finally
        {
            if (exited)
            {
                input?.Dispose();
                output?.Dispose();
                error?.Dispose();
                process?.Dispose();
                processHandle?.Dispose();
                job?.Dispose();
            }
        }
        profile.Dispose();
    }

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(SafeProcessHandle process, uint code);

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
