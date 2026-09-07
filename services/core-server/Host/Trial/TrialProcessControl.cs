using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal sealed partial class TrialProcessControl(
    TrialConfiguration configuration,
    IHostApplicationLifetime lifetime,
    ILogger<TrialProcessControl> logger) : BackgroundService
{
    private const string ProcessFile = ".trial-process.json";
    private const string StopFile = ".trial-stop.json";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var record = TrialProcessRecord.Create(configuration.DeploymentId);
        await TrialProcessFiles.WriteAsync(configuration.StatePath, ProcessFile, record, stoppingToken).ConfigureAwait(false);
        var rejectedReported = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TrialProcessRecord? requested = null;
                try
                {
                    requested = await TrialProcessFiles.ReadIfPresentAsync(configuration.StatePath, StopFile, stoppingToken).ConfigureAwait(false);
                    rejectedReported = false;
                }
                catch (Exception exception) when (exception is JsonException or IOException or TrialConfigurationException)
                {
                    if (!rejectedReported)
                    {
                        RequestRejected(logger);
                        rejectedReported = true;
                    }
                }
                if (requested is not null && record.SameGeneration(requested))
                {
                    lifetime.StopApplication();
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown cancels this local watcher before certificate disposal.
        }
        catch (Exception)
        {
            ControlFailed(logger);
            lifetime.StopApplication();
        }
        finally
        {
            await DeleteOwnAsync(ProcessFile, record).ConfigureAwait(false);
            await DeleteOwnAsync(StopFile, record).ConfigureAwait(false);
        }
    }

    public static async Task<int> RequestStopAsync(string configurationPath)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var configured = await TrialConfiguration.LoadAsync(configurationPath, deadline.Token).ConfigureAwait(false);
            var record = await TrialProcessFiles.ReadIfPresentAsync(configured.StatePath, ProcessFile, deadline.Token).ConfigureAwait(false);
            while (record is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token).ConfigureAwait(false);
                record = await TrialProcessFiles.ReadIfPresentAsync(configured.StatePath, ProcessFile, deadline.Token).ConfigureAwait(false);
            }

            if (!record.Valid || record.DeploymentId != configured.DeploymentId || !record.MatchesRunningProcess())
            {
                return await StopErrorAsync().ConfigureAwait(false);
            }

            await TrialProcessFiles.WriteAsync(configured.StatePath, StopFile, record, deadline.Token).ConfigureAwait(false);
            await Console.Out.WriteLineAsync("{\"status\":\"stop_requested\"}").ConfigureAwait(false);
            return (int)CoreServerExitCode.Success;
        }
        catch (Exception)
        {
            return await StopErrorAsync().ConfigureAwait(false);
        }
    }

    private async Task DeleteOwnAsync(string filename, TrialProcessRecord record)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var existing = await TrialProcessFiles.ReadIfPresentAsync(configuration.StatePath, filename, deadline.Token).ConfigureAwait(false);
            if (existing is not null && record.SameGeneration(existing))
            {
                File.Delete(Path.Combine(configuration.StatePath, filename));
            }
        }
        catch (Exception)
        {
            CleanupFailed(logger);
        }
    }

    private static async Task<int> StopErrorAsync()
    {
        await Console.Error.WriteLineAsync("{\"code\":\"trial_stop_request_rejected\"}").ConfigureAwait(false);
        return (int)CoreServerExitCode.Unavailable;
    }

    [LoggerMessage(EventId = 4130, Level = LogLevel.Error, Message = "Local trial process control failed; stopping the host.")]
    private static partial void ControlFailed(ILogger logger);

    [LoggerMessage(EventId = 4131, Level = LogLevel.Warning, Message = "Local trial process control cleanup could not be completed.")]
    private static partial void CleanupFailed(ILogger logger);

    [LoggerMessage(EventId = 4132, Level = LogLevel.Warning, Message = "An invalid local trial stop request was ignored.")]
    private static partial void RequestRejected(ILogger logger);

}

internal sealed record TrialProcessRecord(int FormatVersion, Guid DeploymentId, Guid Generation, int Pid,
    string ProcessPath, long StartedTicks, string StopNonce)
{
    [JsonIgnore]
    public bool Valid => FormatVersion == 1 && DeploymentId != Guid.Empty && Generation != Guid.Empty && Pid > 0
        && StartedTicks > 0 && !string.IsNullOrWhiteSpace(ProcessPath) && Path.IsPathFullyQualified(ProcessPath)
        && StopNonce is { Length: 64 } && StopNonce.All(char.IsAsciiHexDigit);

    public static TrialProcessRecord Create(Guid deploymentId)
    {
        using var process = Process.GetCurrentProcess();
        return new TrialProcessRecord(1, deploymentId, Guid.NewGuid(), process.Id,
            Environment.ProcessPath ?? throw new TrialConfigurationException("trial_process_path_unavailable"),
            process.StartTime.ToUniversalTime().Ticks, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    public bool MatchesRunningProcess()
    {
        using var process = Process.GetProcessById(Pid);
        return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == StartedTicks
            && string.Equals(process.MainModule?.FileName, ProcessPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Environment.ProcessPath, ProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    public bool SameGeneration(TrialProcessRecord actual) => actual.Valid && DeploymentId == actual.DeploymentId
        && Generation == actual.Generation && Pid == actual.Pid && StartedTicks == actual.StartedTicks
        && string.Equals(ProcessPath, actual.ProcessPath, StringComparison.OrdinalIgnoreCase)
        && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(StopNonce), Convert.FromHexString(actual.StopNonce));

    public override string ToString() => "[private trial process control]";
}

internal static class TrialProcessFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 3,
    };

    public static async Task<TrialProcessRecord?> ReadIfPresentAsync(string state, string filename, CancellationToken cancellationToken)
    {
        var path = Path.Combine(state, filename);
        TrialPrivateState.RequireContained(state, path);
        TrialPrivateState.RequireSafePath(path);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = await TrialPrivateState.ReadTextAsync(path, 4096, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<TrialProcessRecord>(text, JsonOptions);
    }

    public static async Task WriteAsync(string state, string filename, TrialProcessRecord record, CancellationToken cancellationToken)
    {
        TrialPrivateState.RequireDirectory(state);
        var path = Path.Combine(state, filename);
        TrialPrivateState.RequireContained(state, path);
        TrialPrivateState.RequireSafePath(path);
        if (File.Exists(path))
        {
            TrialPrivateState.RequirePrivateFile(path);
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        try
        {
            if (bytes.Length > 4096)
            {
                throw new TrialConfigurationException("trial_process_record_too_large");
            }

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var stream = new FileStream(temporary, options))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            TrialPrivateState.RequirePrivateFile(temporary);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

}
