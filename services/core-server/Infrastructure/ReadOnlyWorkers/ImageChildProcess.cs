using System.ComponentModel;
using System.Diagnostics;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

internal interface IImageChildProcess : IAsyncDisposable
{
    Stream Input { get; }
    Stream Output { get; }
    Stream Error { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal sealed class ImageChildProcess : IImageChildProcess
{
    private readonly Process process;
    private readonly WindowsWorkerJob? job;
    private readonly CancellationTokenRegistration cancellation;
    private bool disposed;
    private int? terminationError;
    public Stream Input => process.StandardInput.BaseStream;
    public Stream Output => process.StandardOutput.BaseStream;
    public Stream Error => process.StandardError.BaseStream;
    public int ExitCode => process.ExitCode;

    private ImageChildProcess(Process process, WindowsWorkerJob? job, CancellationToken token)
    {
        this.process = process;
        this.job = job;
        cancellation = token.Register(static state => ((ImageChildProcess)state!).Terminate(), this);
    }

    public static ImageChildProcess Start(ProcessStartInfo start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var job = OperatingSystem.IsWindows() ? WindowsWorkerJob.Create(256U * 1024 * 1024, TimeSpan.FromSeconds(10).Ticks, 1) : null;
        var process = new Process { StartInfo = start };
        var started = false;
        try
        {
            if (!process.Start()) throw new ReadOnlyWorkerException("preview_process_start_failed");
            started = true;
            if (OperatingSystem.IsWindows()) job!.Assign(process.SafeHandle);
            return new ImageChildProcess(process, job, token);
        }
        catch (Exception primary)
        {
            if (OperatingSystem.IsWindows()) job?.Terminate();
            if (started)
            {
                // Assignment can fail before the child belongs to the job. The original process
                // handle and closed request pipe, not the empty job alone, own this recovery.
                try { process.StandardInput.Dispose(); }
                catch (IOException failure) { primary.Data["image_child_pipe_close"] = failure.GetType().Name; }
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
                {
                    primary.Data["image_child_termination"] = failure is Win32Exception native ? native.NativeErrorCode : "already_exited";
                }
                if (!process.WaitForExit(2000))
                {
                    throw new ImageChildCleanupPendingException(ReapUnassignedAsync(process, job), primary);
                }
                primary.Data["image_child_cleanup"] = "terminated_and_reaped";
            }
            process.Dispose();
            job?.Dispose();
            throw;
        }
    }

    private static async Task ReapUnassignedAsync(Process process, WindowsWorkerJob? job)
    {
        try { await process.WaitForExitAsync().ConfigureAwait(false); }
        finally { process.Dispose(); job?.Dispose(); }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);

    private void Terminate()
    {
        if (OperatingSystem.IsWindows())
        {
            job!.Terminate();
            return;
        }
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* The original owned process can already have exited. */ }
        catch (Win32Exception failure) { terminationError = failure.NativeErrorCode; }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await cancellation.DisposeAsync().ConfigureAwait(false);
        Terminate();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (terminationError is not null)
        {
            throw new ReadOnlyWorkerException("preview_termination_failed", terminationError);
        }
        finally { process.Dispose(); job?.Dispose(); }
    }
}

internal sealed class ImageChildCleanupPendingException(Task completion, Exception primary)
    : IOException("The owned child is still being reaped after failed startup.", primary)
{
    public Task Completion { get; } = completion;
}
