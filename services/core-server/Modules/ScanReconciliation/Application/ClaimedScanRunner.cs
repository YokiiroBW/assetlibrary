using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

internal sealed class ClaimedScanRunner(IScanRequestStore store, IDurableTaskCoordinator tasks, InitialScanTaskExecutor executor, InitialScanExecutionOptions options)
{
    private readonly LeaseOwner worker = new("scan-" + Guid.NewGuid().ToString("N"));
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        var leases = await tasks.ClaimAsync(new DurableTaskClaimRequest(worker, options.LeaseDuration, 1), cancellationToken).ConfigureAwait(false);
        if (leases.Count == 0)
        {
            return false;
        }

        var lease = leases[0];
        var request = await store.FindAsync(lease.TaskId, cancellationToken).ConfigureAwait(false);
        if (request is null || lease.TaskType != InitialScanExecutionOptions.TaskType)
        {
            await tasks.FinishAsync(new DurableTaskFinishRequest(lease.TaskId, lease.Identity, DurableTaskFinishKind.PermanentFailure,
                new FailureCode("scan_request_missing")), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await executor.ExecuteAsync(request, lease, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

}
