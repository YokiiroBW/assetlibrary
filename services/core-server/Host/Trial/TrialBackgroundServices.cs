using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Contracts;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal sealed class TrialScanWorker(IInitialScanCoordinator scans, ILogger<TrialScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await scans.RecoverAsync(stoppingToken).ConfigureAwait(false);
                if (await scans.RunNextAsync(stoppingToken).ConfigureAwait(false))
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                TrialBackgroundLog.Unavailable(logger, "scan");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

internal sealed class TrialAvailabilityWorker(ILibraryAvailability availability, ILogger<TrialAvailabilityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await availability.RefreshBatchAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    TrialBackgroundLog.Unavailable(logger, "availability");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The periodic probe belongs to this Host and stops with it.
        }
    }
}

internal static partial class TrialBackgroundLog
{
    [LoggerMessage(EventId = 4600, Level = LogLevel.Warning, Message = "Read-only trial background component {Component} is temporarily unavailable.")]
    public static partial void Unavailable(ILogger logger, string component);
}
