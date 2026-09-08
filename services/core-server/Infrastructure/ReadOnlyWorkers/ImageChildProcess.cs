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
        try
        {
            if (!process.Start()) throw new ReadOnlyWorkerException("preview_process_start_failed");
            if (OperatingSystem.IsWindows()) job!.Assign(process.SafeHandle);
            return new ImageChildProcess(process, job, token);
        }
        catch
        {
            if (OperatingSystem.IsWindows()) job?.Terminate();
            process.Dispose();
            job?.Dispose();
            throw;
        }
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
