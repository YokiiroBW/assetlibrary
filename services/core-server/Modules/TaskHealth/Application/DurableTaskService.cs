using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.TaskHealth.Application;

public sealed class DurableTaskService(
    IDurableTaskStore store,
    TimeProvider timeProvider,
    TaskHealthExecutionLimits limits,
    TaskHealthLogger logger) : IDurableTaskCoordinator
{
    public async ValueTask<DurableTaskEnqueueResult> EnqueueAsync(
        DurableTaskEnqueueRequest request,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.Validate(request);
        var now = timeProvider.GetUtcNow();
        var normalized = request with
        {
            NotBefore = (request.NotBefore ?? now).ToUniversalTime(),
        };
        var result = await BoundedTaskHealthOperation.RunAsync(
            "task_enqueue",
            limits.OperationTimeout,
            token => store.EnqueueAsync(normalized, now, token),
            cancellationToken).ConfigureAwait(false);
        DurableTaskStoreResultValidator.Validate(result, request);
        logger.Enqueued(result);
        return result;
    }

    public async ValueTask<IReadOnlyList<DurableTaskLease>> ClaimAsync(
        DurableTaskClaimRequest request,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.Validate(request, limits);
        var now = timeProvider.GetUtcNow();
        var leases = await BoundedTaskHealthOperation.RunAsync(
            "task_claim",
            limits.OperationTimeout,
            token => store.ClaimAsync(request, now, token),
            cancellationToken).ConfigureAwait(false);
        DurableTaskStoreResultValidator.Validate(leases, request, timeProvider.GetUtcNow());
        logger.Claimed(request.Worker, leases.Count);
        return leases;
    }

    public async ValueTask<DurableTaskHeartbeatResult> HeartbeatAsync(
        DurableTaskHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.Validate(request);
        var now = timeProvider.GetUtcNow();
        var result = await BoundedTaskHealthOperation.RunAsync(
            "task_heartbeat",
            limits.OperationTimeout,
            token => store.HeartbeatAsync(request, now, token),
            cancellationToken).ConfigureAwait(false);
        DurableTaskStoreResultValidator.Validate(
            result,
            timeProvider.GetUtcNow(),
            request.LeaseDuration);
        return result;
    }

    public async ValueTask<DurableTaskFinishResult> FinishAsync(
        DurableTaskFinishRequest request,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.Validate(request);
        var result = await BoundedTaskHealthOperation.RunAsync(
            "task_finish",
            limits.OperationTimeout,
            token => store.FinishAsync(request, timeProvider.GetUtcNow(), token),
            cancellationToken).ConfigureAwait(false);
        DurableTaskStoreResultValidator.Validate(result);
        logger.Finished(request.TaskId, result);
        return result;
    }

    public async ValueTask<DurableTaskCancellationResult> RequestCancellationAsync(
        DurableTaskId taskId,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.Validate(taskId);
        var result = await BoundedTaskHealthOperation.RunAsync(
            "task_cancel",
            limits.OperationTimeout,
            token => store.RequestCancellationAsync(taskId, timeProvider.GetUtcNow(), token),
            cancellationToken).ConfigureAwait(false);
        DurableTaskStoreResultValidator.Validate(result);
        logger.Cancellation(taskId, result);
        return result;
    }

    public ValueTask<int> ReclaimExpiredAsync(
        int batchSize,
        CancellationToken cancellationToken) =>
        TaskHealthReclaimOperation.RunAsync(
            "task_reclaim",
            "task reclaim",
            store.ReclaimExpiredAsync,
            timeProvider,
            limits,
            batchSize,
            cancellationToken);
}
