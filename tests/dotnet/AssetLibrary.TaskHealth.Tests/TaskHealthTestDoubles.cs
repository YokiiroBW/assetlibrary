using AssetLibrary.Modules.TaskHealth.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.TaskHealth.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class StubDurableTaskStore : IDurableTaskStore
{
    public int CallCount { get; private set; }

    public bool BlockEnqueue { get; set; }

    public DurableTaskEnqueueRequest? LastEnqueueRequest { get; private set; }

    public DateTimeOffset LastNow { get; private set; }

    public DurableTaskEnqueueResult? EnqueueResult { get; set; }

    public IReadOnlyList<DurableTaskLease> ClaimResult { get; set; } = [];

    public DurableTaskHeartbeatResult HeartbeatResult { get; set; } =
        new(TaskLeaseMutationStatus.NotCurrent, false, null);

    public DurableTaskFinishResult FinishResult { get; set; } =
        new(TaskLeaseMutationStatus.NotCurrent, null);

    public DurableTaskCancellationResult CancellationResult { get; set; } =
        new(TaskCancellationStatus.NotFound);

    public int ReclaimResult { get; set; }

    public ValueTask<DurableTaskEnqueueResult> EnqueueAsync(
        DurableTaskEnqueueRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Called(now);
        LastEnqueueRequest = request;
        return BlockEnqueue
            ? new ValueTask<DurableTaskEnqueueResult>(WaitForCancellationAsync(cancellationToken))
            : ValueTask.FromResult(EnqueueResult ?? new(request.TaskId, DurableTaskEnqueueStatus.Created));
    }

    public ValueTask<IReadOnlyList<DurableTaskLease>> ClaimAsync(
        DurableTaskClaimRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Called(now);
        return ValueTask.FromResult(ClaimResult);
    }

    public ValueTask<DurableTaskHeartbeatResult> HeartbeatAsync(
        DurableTaskHeartbeatRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Called(now);
        return ValueTask.FromResult(HeartbeatResult);
    }

    public ValueTask<DurableTaskFinishResult> FinishAsync(
        DurableTaskFinishRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Called(now);
        return ValueTask.FromResult(FinishResult);
    }

    public ValueTask<DurableTaskCancellationResult> RequestCancellationAsync(
        DurableTaskId taskId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Called(now);
        return ValueTask.FromResult(CancellationResult);
    }

    public ValueTask<int> ReclaimExpiredAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        Called(now);
        return ValueTask.FromResult(ReclaimResult);
    }

    private static async Task<DurableTaskEnqueueResult> WaitForCancellationAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The cancellation delay returned unexpectedly.");
    }

    private void Called(DateTimeOffset now)
    {
        CallCount++;
        LastNow = now;
    }
}

internal sealed class StubOutboxStore : IOutboxStore
{
    public int CallCount { get; private set; }

    public OutboxEnqueueRequest? LastEnqueueRequest { get; private set; }

    public DateTimeOffset LastNow { get; private set; }

    public OutboxEnqueueResult? EnqueueResult { get; set; }

    public IReadOnlyList<OutboxEventLease> ClaimResult { get; set; } = [];

    public OutboxMutationResult PublishedResult { get; set; } =
        new(TaskLeaseMutationStatus.NotCurrent, null);

    public OutboxMutationResult ReleasedResult { get; set; } =
        new(TaskLeaseMutationStatus.NotCurrent, null);

    public int ReclaimResult { get; set; }

    public ValueTask<OutboxEnqueueResult> EnqueueAsync(
        OutboxEnqueueRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Called(now);
        LastEnqueueRequest = request;
        return ValueTask.FromResult(EnqueueResult ?? new(request.EventId, OutboxEnqueueStatus.Created));
    }

    public ValueTask<IReadOnlyList<OutboxEventLease>> ClaimAsync(
        OutboxClaimRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Called(now);
        return ValueTask.FromResult(ClaimResult);
    }

    public ValueTask<OutboxMutationResult> MarkPublishedAsync(
        OutboxPublishRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Called(now);
        return ValueTask.FromResult(PublishedResult);
    }

    public ValueTask<OutboxMutationResult> ReleaseAsync(
        OutboxReleaseRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Called(now);
        return ValueTask.FromResult(ReleasedResult);
    }

    public ValueTask<int> ReclaimExpiredAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        Called(now);
        return ValueTask.FromResult(ReclaimResult);
    }

    private void Called(DateTimeOffset now)
    {
        CallCount++;
        LastNow = now;
    }
}

internal sealed class StubHealthStatusStore : IHealthStatusStore
{
    public int CallCount { get; private set; }

    public HealthStatusUpdate? LastUpdate { get; private set; }

    public DateTimeOffset LastWrittenAt { get; private set; }

    public HealthStatusWriteResult Result { get; set; } = new(HealthStatusWriteStatus.Applied);

    public ValueTask<HealthStatusWriteResult> WriteAsync(
        HealthStatusUpdate update,
        DateTimeOffset writtenAt,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastUpdate = update;
        LastWrittenAt = writtenAt;
        return ValueTask.FromResult(Result);
    }
}
