using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
namespace AssetLibrary.Modules.ScanReconciliation.Application;

public sealed class InitialReadOnlyScanService(
    ILibraryScanTargetQuery libraryTargets,
    IReadOnlyFileDiscovery discovery,
    IAssetObservationSink observations,
    IScanRunJournal journal,
    TimeProvider timeProvider,
    InitialScanLogger logger)
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    public async Task<InitialScanResult> ExecuteAsync(
        InitialScanRequest request,
        CancellationToken cancellationToken)
    {
        InitialScanRequestValidator.Validate(request);
        var target = await libraryTargets.FindAsync(request.LibraryId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return new InitialScanResult(null, InitialScanStatus.LibraryNotFound, 0, 0, "library_not_found");
        }

        if (target.LibraryId != request.LibraryId)
        {
            throw new InvalidOperationException("The scan target does not match the requested library.");
        }

        if (target.Availability == StorageAvailability.Offline)
        {
            return new InitialScanResult(null, InitialScanStatus.StorageOffline, 0, 0, "storage_offline");
        }

        var scanId = Guid.NewGuid();
        var startedAt = timeProvider.GetUtcNow();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);

        await journal.StartAsync(scanId, request.LibraryId, startedAt, timeout.Token).ConfigureAwait(false);
        IAssetObservationSession session;
        try
        {
            session = await observations
                .BeginInitialScanAsync(scanId, request.LibraryId, startedAt, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await FinishWithoutSessionAsync(scanId, ScanRunTerminalState.TimedOut, "scan_timed_out")
                .ConfigureAwait(false);
            return new InitialScanResult(scanId, InitialScanStatus.TimedOut, 0, 0, "scan_timed_out");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FinishWithoutSessionAsync(scanId, ScanRunTerminalState.Cancelled, "scan_cancelled")
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await FinishWithoutSessionAsync(scanId, ScanRunTerminalState.DiscoveryFailed, "scan_initialization_failed")
                .ConfigureAwait(false);
            logger.Failed(scanId, "scan_initialization_failed", exception);
            throw;
        }

        await using var ownedSession = session;
        logger.Started(scanId, request.LibraryId.Value);

        var observedCount = 0;
        int committedCount;
        DateTimeOffset finishedAt;
        try
        {
            var batch = new List<AssetObservation>(request.BatchSize);
            await foreach (var entry in discovery
                .DiscoverAsync(target, timeout.Token)
                .WithCancellation(timeout.Token)
                .ConfigureAwait(false))
            {
                batch.Add(ToObservation(entry));
                observedCount++;
                if (batch.Count == request.BatchSize)
                {
                    await session.StageAsync(batch, timeout.Token).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await session.StageAsync(batch, timeout.Token).ConfigureAwait(false);
            }

            finishedAt = timeProvider.GetUtcNow();
            committedCount = await session.CompleteAsync(finishedAt, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await AbortAsync(session, scanId, ScanRunTerminalState.TimedOut, observedCount, "scan_timed_out")
                .ConfigureAwait(false);
            logger.Failed(scanId, "scan_timed_out", null);
            return new InitialScanResult(scanId, InitialScanStatus.TimedOut, observedCount, 0, "scan_timed_out");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AbortAsync(session, scanId, ScanRunTerminalState.Cancelled, observedCount, "scan_cancelled")
                .ConfigureAwait(false);
            throw;
        }
        catch (FileDiscoveryException exception)
        {
            await AbortAsync(
                session,
                scanId,
                ScanRunTerminalState.DiscoveryFailed,
                observedCount,
                exception.FailureCode).ConfigureAwait(false);
            logger.Failed(scanId, exception.FailureCode, exception);
            return new InitialScanResult(
                scanId,
                InitialScanStatus.DiscoveryFailed,
                observedCount,
                0,
                exception.FailureCode);
        }
        catch (Exception exception)
        {
            await AbortAsync(
                session,
                scanId,
                ScanRunTerminalState.DiscoveryFailed,
                observedCount,
                "scan_internal_failure").ConfigureAwait(false);
            logger.Failed(scanId, "scan_internal_failure", exception);
            throw;
        }

        logger.Completed(scanId, committedCount);
        try
        {
            using var completion = new CancellationTokenSource(CleanupTimeout);
            await journal.FinishAsync(
                scanId,
                ScanRunTerminalState.Completed,
                observedCount,
                committedCount,
                null,
                finishedAt,
                completion.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.Failed(scanId, "scan_journal_finalization_failed", exception);
            throw;
        }

        return new InitialScanResult(
            scanId,
            InitialScanStatus.Completed,
            observedCount,
            committedCount,
            null);
    }

    private static AssetObservation ToObservation(Domain.DiscoveredEntry entry) =>
        new AssetObservation(
            StableEntryId.New(),
            entry.RelativePath,
            entry.Kind,
            entry.ContentLength,
            entry.LastWriteTimeUtc).Validate();

    private async ValueTask AbortAsync(
        IAssetObservationSession session,
        Guid scanId,
        ScanRunTerminalState state,
        int observedCount,
        string failureCode)
    {
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        await session.AbortAsync(cleanup.Token).ConfigureAwait(false);
        await journal.FinishAsync(
            scanId,
            state,
            observedCount,
            0,
            failureCode,
            timeProvider.GetUtcNow(),
            cleanup.Token).ConfigureAwait(false);
    }

    private async ValueTask FinishWithoutSessionAsync(
        Guid scanId,
        ScanRunTerminalState state,
        string failureCode)
    {
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        await journal.FinishAsync(
            scanId,
            state,
            0,
            0,
            failureCode,
            timeProvider.GetUtcNow(),
            cleanup.Token).ConfigureAwait(false);
    }

}
