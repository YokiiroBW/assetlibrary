using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

public sealed class InitialScanTaskExecutor(IScanRequestStore store, ILibraryScanTargetQuery targets,
    ILibraryAvailability availability, IReadOnlyFileDiscovery discovery, IAssetObservationSink sink,
    IDurableTaskCoordinator tasks, IDurableTaskCommitGuard guard, ScanTaskRecovery recovery,
    TimeProvider timeProvider, InitialScanExecutionOptions options, InitialScanLogger logger)
{
    private readonly ScanTaskCompletion completion = new(store, tasks);
    public async Task ExecuteAsync(ScanRequestRecord request, DurableTaskLease lease, CancellationToken cancellationToken)
    {
        if (await recovery.ReconcileCommittedAsync(request, lease, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        using var workCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var execution = new ScanLeaseExecution(lease, tasks, guard, options, timeProvider, workCancellation);
        var monitor = execution.MonitorAsync();
        try
        {
            await execution.ConfirmAsync(workCancellation.Token).ConfigureAwait(false);
            await recovery.AbandonPreviousRunsAsync(request, lease.Attempt, workCancellation.Token).ConfigureAwait(false);
            await availability.RefreshAsync(request.LibraryId, workCancellation.Token).ConfigureAwait(false);
            var service = new InitialReadOnlyScanService(targets, discovery, new TrackedScanSink(sink, store, execution),
                new TrackedScanJournal(store, request, lease.Attempt), timeProvider, logger);
            var result = await service.ExecuteAsync(new InitialScanRequest(request.LibraryId, options.ScanTimeout, options.BatchSize),
                workCancellation.Token).ConfigureAwait(false);
            if (result.Status == InitialScanStatus.Completed)
            {
                await completion.FinishAsync(lease, DurableTaskFinishKind.Succeeded, null, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await completion.FinishAsync(lease, execution.CancellationRequested ? DurableTaskFinishKind.Cancelled : DurableTaskFinishKind.PermanentFailure,
                    execution.CancellationRequested ? null : result.FailureCode ?? "scan_failed", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (await recovery.ReconcileCommittedAsync(request, lease, cleanup.Token).ConfigureAwait(false))
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var kind = execution.CancellationRequested ? DurableTaskFinishKind.Cancelled :
                execution.LeaseLost || exception is ReadOnlyTrialException { Code: "scan_already_running" }
                    ? DurableTaskFinishKind.RetryableFailure : DurableTaskFinishKind.PermanentFailure;
            var code = kind == DurableTaskFinishKind.Cancelled ? null : execution.LeaseLost ? "scan_lease_lost" : "scan_execution_failed";
            logger.Failed(lease.TaskId.Value, code ?? "scan_cancelled", null);
            await completion.FinishAsync(lease, kind, code, cleanup.Token).ConfigureAwait(false);
        }
        finally
        {
            await workCancellation.CancelAsync().ConfigureAwait(false);
            await monitor.ConfigureAwait(false);
        }
    }

}
