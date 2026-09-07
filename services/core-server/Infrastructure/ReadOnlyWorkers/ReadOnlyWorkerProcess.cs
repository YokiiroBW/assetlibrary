using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Infrastructure.ReadOnlyWorkers;

internal sealed class ReadOnlyWorkerProcess(ReadOnlyWorkerProcessOptions options)
{
    public async IAsyncEnumerable<WorkerFrame> ReadAsync(
        string mode,
        WorkerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var job = OperatingSystem.IsWindows() ? WindowsWorkerJob.Create() : null;
        using var process = new Process { StartInfo = CreateStartInfo(mode) };
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!process.Start())
        {
            throw new ReadOnlyWorkerException("worker_start_failed");
        }

        var stderr = DrainErrorsAsync(process.StandardError, lifetime.Token);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // The child cannot start filesystem work before assignment succeeds and stdin is sent.
                job!.Assign(process.SafeHandle);
            }

            var line = JsonSerializer.Serialize(request, ReadOnlyWorkerJsonContext.Default.WorkerRequest);
            if (Encoding.UTF8.GetByteCount(line) > ReadOnlyWorkerProtocol.RequestLimit)
            {
                throw new ReadOnlyWorkerException("worker_request_limit");
            }

            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                startup.CancelAfter(options.ProbeTimeout);
                try
                {
                    await process.StandardInput.WriteLineAsync(line.AsMemory(), startup.Token).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(startup.Token).ConfigureAwait(false);
                    process.StandardInput.Close();
                }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
                {
                    throw new ReadOnlyWorkerException("worker_startup_timed_out");
                }
            }
            var reader = new BoundedNdjsonReader(process.StandardOutput, ReadOnlyWorkerProtocol.FrameLimit);
            while (true)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(mode == "probe" ? options.ProbeTimeout : options.ScanInactivityTimeout);
                string? frameLine;
                try
                {
                    frameLine = await reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
                {
                    throw new ReadOnlyWorkerException("worker_timed_out");
                }

                if (stderr.IsFaulted)
                {
                    await stderr.ConfigureAwait(false);
                }

                if (frameLine is null)
                {
                    break;
                }

                var frame = JsonSerializer.Deserialize(frameLine, ReadOnlyWorkerJsonContext.Default.WorkerFrame);
                if (frame is null || frame.Version != 1)
                {
                    throw new ReadOnlyWorkerException("worker_protocol_invalid");
                }

                yield return frame;
            }

            using var exitDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            exitDeadline.CancelAfter(options.TerminationTimeout);
            await process.WaitForExitAsync(exitDeadline.Token).ConfigureAwait(false);
            await stderr.WaitAsync(exitDeadline.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new ReadOnlyWorkerException("worker_exit_failed");
            }
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The process can finish between the exit check and termination.
                }
                using var termination = new CancellationTokenSource(options.TerminationTimeout);
                await process.WaitForExitAsync(termination.Token).ConfigureAwait(false);
            }

            try
            {
                await stderr.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                // Cancellation terminates the bounded diagnostic pipe together with the worker.
            }
        }
    }

    private ProcessStartInfo CreateStartInfo(string mode)
    {
        var start = new ProcessStartInfo(options.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false, true),
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true),
        };
        // Workers need the runtime and OS environment, never Host/DB/provider credentials.
        start.Environment.Clear();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64", "TEMP", "TMP", "LANG" })
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                start.Environment[name] = value;
            }
        }

        foreach (var argument in options.PrefixArguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.ArgumentList.Add("--read-only-worker");
        start.ArgumentList.Add(mode);
        return start;
    }

    private static async Task DrainErrorsAsync(TextReader reader, CancellationToken token)
    {
        var buffer = new char[1024];
        var count = 0;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            count += read;
            if (count > 4096)
            {
                throw new ReadOnlyWorkerException("worker_diagnostics_limit");
            }
        }
    }
}
