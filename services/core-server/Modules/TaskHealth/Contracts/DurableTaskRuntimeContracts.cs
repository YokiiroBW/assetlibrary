namespace AssetLibrary.Modules.TaskHealth.Contracts;

public interface IDurableTaskCoordinator
{
    ValueTask<DurableTaskEnqueueResult> EnqueueAsync(DurableTaskEnqueueRequest request, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<DurableTaskLease>> ClaimAsync(DurableTaskClaimRequest request, CancellationToken cancellationToken);
    ValueTask<DurableTaskHeartbeatResult> HeartbeatAsync(DurableTaskHeartbeatRequest request, CancellationToken cancellationToken);
    ValueTask<DurableTaskFinishResult> FinishAsync(DurableTaskFinishRequest request, CancellationToken cancellationToken);
    ValueTask<DurableTaskCancellationResult> RequestCancellationAsync(DurableTaskId taskId, CancellationToken cancellationToken);
    ValueTask<int> ReclaimExpiredAsync(int batchSize, CancellationToken cancellationToken);
}

public sealed record DurableTaskDetails(DurableTaskSnapshot Snapshot, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, string? FailureCode);

public interface IDurableTaskInspector
{
    ValueTask<DurableTaskDetails?> FindAsync(DurableTaskId taskId, CancellationToken cancellationToken);
    ValueTask<bool> ReconcileCommittedAsync(DurableTaskId taskId, CancellationToken cancellationToken);
}

public interface IDurableTaskCommitGuard
{
    ValueTask<int> CommitAsync(DurableTaskHeartbeatRequest request,
        Func<CancellationToken, ValueTask<int>> commit, CancellationToken cancellationToken);
}

public sealed class TaskCommitLeaseException(string code) : InvalidOperationException("The task commit lease is no longer available.")
{
    public string Code { get; } = code;
}
