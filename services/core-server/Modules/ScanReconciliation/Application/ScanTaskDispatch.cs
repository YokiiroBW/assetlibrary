using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

internal sealed class ScanTaskDispatch(IScanRequestStore store, IDurableTaskCoordinator tasks, IDurableTaskInspector inspector)
{
    public async ValueTask DispatchAsync(ScanRequestRecord request, CancellationToken token)
    {
        if (!request.Dispatched)
        {
            await tasks.EnqueueAsync(new DurableTaskEnqueueRequest(request.TaskId,
                new TaskIdempotencyKey("initial-scan:" + request.TaskId.Value.ToString("D")), InitialScanExecutionOptions.TaskType,
                new JsonObjectPayload($"{{\"library_id\":\"{request.LibraryId.Value:D}\",\"version\":1}}"),
                TaskPriority.P4, 3, request.CreatedAt), token).ConfigureAwait(false);
            await store.MarkDispatchedAsync(request.TaskId, token).ConfigureAwait(false);
        }

        if (request.CancellationRequested)
        {
            await tasks.RequestCancellationAsync(request.TaskId, token).ConfigureAwait(false);
        }
    }

    public async ValueTask ReconcileTerminalAsync(ScanRequestRecord request, CancellationToken token)
    {
        var details = await inspector.FindAsync(request.TaskId, token).ConfigureAwait(false);
        if (details?.Snapshot.State is DurableTaskState.Succeeded or DurableTaskState.Failed or DurableTaskState.Cancelled)
        {
            await store.MarkTerminalAsync(request.TaskId, token).ConfigureAwait(false);
        }
    }

    public async ValueTask<ScanTaskView> ViewAsync(ScanRequestRecord request, CancellationToken token)
    {
        var details = await inspector.FindAsync(request.TaskId, token).ConfigureAwait(false);
        var state = details?.Snapshot.State ?? DurableTaskState.Queued;
        var runs = await store.RunsAsync(request.TaskId, token).ConfigureAwait(false);
        var run = runs.Count > 0 ? runs[0] : null;
        var terminal = state is DurableTaskState.Succeeded or DurableTaskState.Failed or DurableTaskState.Cancelled;
        return new ScanTaskView(request.TaskId.Value, run?.ScanId, request.LibraryId, state,
            details?.Snapshot.CancellationRequested ?? request.CancellationRequested,
            run?.ObservedEntries ?? 0, run?.CommittedEntries ?? 0, run?.StartedAt,
            terminal ? run?.FinishedAt ?? details?.UpdatedAt : null,
            FailureCode(state, details, run),
            !terminal, state is DurableTaskState.Failed or DurableTaskState.Cancelled);
    }

    private static string? FailureCode(DurableTaskState state, DurableTaskDetails? details, ScanRunRecord? run) =>
        state is DurableTaskState.Failed or DurableTaskState.Cancelled ? details?.FailureCode ?? run?.FailureCode : null;
}
