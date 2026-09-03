using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.TaskHealth.Application;

public interface IDurableTaskStore
{
    ValueTask<DurableTaskEnqueueResult> EnqueueAsync(
        DurableTaskEnqueueRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<DurableTaskLease>> ClaimAsync(
        DurableTaskClaimRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<DurableTaskHeartbeatResult> HeartbeatAsync(
        DurableTaskHeartbeatRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<DurableTaskFinishResult> FinishAsync(
        DurableTaskFinishRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<DurableTaskCancellationResult> RequestCancellationAsync(
        DurableTaskId taskId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<int> ReclaimExpiredAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken);
}

public interface IOutboxStore
{
    ValueTask<OutboxEnqueueResult> EnqueueAsync(
        OutboxEnqueueRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<OutboxEventLease>> ClaimAsync(
        OutboxClaimRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<OutboxMutationResult> MarkPublishedAsync(
        OutboxPublishRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<OutboxMutationResult> ReleaseAsync(
        OutboxReleaseRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<int> ReclaimExpiredAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken);
}

public interface IHealthStatusStore
{
    ValueTask<HealthStatusWriteResult> WriteAsync(
        HealthStatusUpdate update,
        DateTimeOffset writtenAt,
        CancellationToken cancellationToken);
}
