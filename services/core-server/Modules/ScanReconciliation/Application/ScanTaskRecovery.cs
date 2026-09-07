using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

public sealed class ScanTaskRecovery(IScanRequestStore store, IAssetIndexSnapshotQuery snapshots,
    IInitialScanStageMaintenance stages, IDurableTaskCoordinator tasks, IDurableTaskInspector inspector, TimeProvider timeProvider)
{
    public async ValueTask<bool> ReconcileCommittedAsync(ScanRequestRecord request, DurableTaskLease? lease, CancellationToken token)
    {
        var snapshot = await snapshots.FindAsync(request.LibraryId, token).ConfigureAwait(false);
        if (snapshot is null)
        {
            return false;
        }

        var runs = await store.RunsAsync(request.TaskId, token).ConfigureAwait(false);
        if (!runs.Any(run => run.ScanId == snapshot.ScanId))
        {
            return false;
        }

        await store.FinishRunAsync(snapshot.ScanId, ScanRunTerminalState.Completed,
            snapshot.EntryCount, snapshot.EntryCount, null, snapshot.ObservedAt, token).ConfigureAwait(false);
        var repaired = lease is not null
            && (await tasks.FinishAsync(new DurableTaskFinishRequest(request.TaskId, lease.Identity, DurableTaskFinishKind.Succeeded), token)
                .ConfigureAwait(false)).Status == TaskLeaseMutationStatus.Accepted;
        if (!repaired)
        {
            repaired = await inspector.ReconcileCommittedAsync(request.TaskId, token).ConfigureAwait(false);
        }

        if (repaired)
        {
            await store.MarkTerminalAsync(request.TaskId, token).ConfigureAwait(false);
        }

        return true;
    }

    public async ValueTask AbandonPreviousRunsAsync(ScanRequestRecord request, int currentAttempt, CancellationToken token)
    {
        foreach (var run in await store.RunsAsync(request.TaskId, token).ConfigureAwait(false))
        {
            if (run.Attempt >= currentAttempt || run.CommittedEntries > 0)
            {
                continue;
            }

            await stages.AbortAsync(run.ScanId, request.LibraryId, token).ConfigureAwait(false);
            if (run.FinishedAt is null)
            {
                await store.FinishRunAsync(run.ScanId, ScanRunTerminalState.DiscoveryFailed,
                    run.ObservedEntries, 0, "scan_interrupted", timeProvider.GetUtcNow(), token).ConfigureAwait(false);
            }
        }
    }
}
