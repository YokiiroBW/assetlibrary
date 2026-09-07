using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

internal sealed class ScanTaskCompletion(IScanRequestStore store, IDurableTaskCoordinator tasks)
{
    public async ValueTask FinishAsync(DurableTaskLease lease, DurableTaskFinishKind kind, string? code, CancellationToken token)
    {
        var result = await tasks.FinishAsync(new DurableTaskFinishRequest(lease.TaskId, lease.Identity, kind,
            code is null ? null : new FailureCode(code), kind == DurableTaskFinishKind.RetryableFailure ? TimeSpan.FromSeconds(10) : null), token)
            .ConfigureAwait(false);
        if (result.State is DurableTaskState.Succeeded or DurableTaskState.Cancelled or DurableTaskState.Failed)
        {
            await store.MarkTerminalAsync(lease.TaskId, token).ConfigureAwait(false);
        }
    }
}
