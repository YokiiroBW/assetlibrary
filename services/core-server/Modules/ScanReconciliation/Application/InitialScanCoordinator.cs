using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

public sealed class InitialScanCoordinator(IScanRequestStore store, ILibraryScanTargetQuery targets,
    ILibraryAvailability availability, IAssetIndexSnapshotQuery snapshots, IDurableTaskCoordinator tasks, IDurableTaskInspector inspector,
    ScanTaskRecovery recovery, InitialScanTaskExecutor executor, InitialScanExecutionOptions options) : IInitialScanCoordinator
{
    private readonly ScanTaskDispatch dispatch = new(store, tasks, inspector);
    private readonly ClaimedScanRunner runner = new(store, tasks, executor, options);

    public async ValueTask<ScanTaskView> StartAsync(LibraryId libraryId, ManagementOperation operation, CancellationToken cancellationToken)
    {
        operation.Validate();
        var previous = await store.FindOperationAsync(libraryId, operation, cancellationToken).ConfigureAwait(false);
        if (previous is not null)
        {
            await dispatch.DispatchAsync(previous, cancellationToken).ConfigureAwait(false);
            return await dispatch.ViewAsync(previous, cancellationToken).ConfigureAwait(false);
        }

        _ = await targets.FindAsync(libraryId, cancellationToken).ConfigureAwait(false)
            ?? throw new ReadOnlyTrialException("library_not_found");
        if (await snapshots.FindAsync(libraryId, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new ReadOnlyTrialException("already_indexed");
        }

        if (await availability.RefreshAsync(libraryId, cancellationToken).ConfigureAwait(false) != StorageAvailability.Online)
        {
            throw new ReadOnlyTrialException("storage_unavailable");
        }

        if (await store.LatestAsync(libraryId, cancellationToken).ConfigureAwait(false) is { } latest)
        {
            await dispatch.ReconcileTerminalAsync(latest, cancellationToken).ConfigureAwait(false);
        }

        var accepted = await store.AcceptAsync(libraryId, operation, cancellationToken).ConfigureAwait(false);
        await dispatch.DispatchAsync(accepted, cancellationToken).ConfigureAwait(false);
        return await dispatch.ViewAsync(accepted, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ScanTaskView?> GetAsync(LibraryId libraryId, CancellationToken cancellationToken)
    {
        if (await targets.FindAsync(libraryId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new ReadOnlyTrialException("library_not_found");
        }

        var request = await store.LatestAsync(libraryId, cancellationToken).ConfigureAwait(false);
        return request is null ? null : await dispatch.ViewAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ScanTaskView> CancelAsync(LibraryId libraryId, Guid taskId, ManagementOperation operation, CancellationToken cancellationToken)
    {
        operation.Validate();
        var id = new DurableTaskId(taskId);
        if (!await store.CancelAsync(libraryId, id, operation, cancellationToken).ConfigureAwait(false))
        {
            throw new ReadOnlyTrialException("scan_not_found");
        }

        var request = (await store.FindAsync(id, cancellationToken).ConfigureAwait(false))!;
        await dispatch.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        await tasks.RequestCancellationAsync(id, cancellationToken).ConfigureAwait(false);
        await dispatch.ReconcileTerminalAsync(request, cancellationToken).ConfigureAwait(false);
        return await dispatch.ViewAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> RunNextAsync(CancellationToken cancellationToken) => runner.RunNextAsync(cancellationToken);

    public async ValueTask<int> RecoverAsync(CancellationToken cancellationToken)
    {
        await tasks.ReclaimExpiredAsync(32, cancellationToken).ConfigureAwait(false);
        var requests = await store.RecoverBatchAsync(cancellationToken).ConfigureAwait(false);
        foreach (var request in requests)
        {
            await dispatch.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
            if (!await recovery.ReconcileCommittedAsync(request, null, cancellationToken).ConfigureAwait(false))
            {
                await dispatch.ReconcileTerminalAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        return requests.Count;
    }

}
