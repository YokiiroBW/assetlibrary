using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.TaskHealth.Application;

public sealed class DurableOutboxService(
    IOutboxStore store,
    TimeProvider timeProvider,
    TaskHealthExecutionLimits limits,
    TaskHealthLogger logger)
{
    public async ValueTask<OutboxEnqueueResult> EnqueueAsync(
        OutboxEnqueueRequest request,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.Validate(request);
        var now = timeProvider.GetUtcNow();
        var normalized = request with
        {
            OccurredAt = request.OccurredAt.ToUniversalTime(),
            NotBefore = (request.NotBefore ?? now).ToUniversalTime(),
        };
        var result = await BoundedTaskHealthOperation.RunAsync(
            "outbox_enqueue",
            limits.OperationTimeout,
            token => store.EnqueueAsync(normalized, now, token),
            cancellationToken).ConfigureAwait(false);
        OutboxStoreResultValidator.Validate(result, request);
        logger.Enqueued(result);
        return result;
    }

    public async ValueTask<IReadOnlyList<OutboxEventLease>> ClaimAsync(
        OutboxClaimRequest request,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.Validate(request, limits);
        var now = timeProvider.GetUtcNow();
        var leases = await BoundedTaskHealthOperation.RunAsync(
            "outbox_claim",
            limits.OperationTimeout,
            token => store.ClaimAsync(request, now, token),
            cancellationToken).ConfigureAwait(false);
        OutboxStoreResultValidator.Validate(leases, request, timeProvider.GetUtcNow());
        logger.ClaimedOutbox(request.Publisher, leases.Count);
        return leases;
    }

    public async ValueTask<OutboxMutationResult> MarkPublishedAsync(
        OutboxPublishRequest request,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.Validate(request);
        var result = await BoundedTaskHealthOperation.RunAsync(
            "outbox_publish",
            limits.OperationTimeout,
            token => store.MarkPublishedAsync(request, timeProvider.GetUtcNow(), token),
            cancellationToken).ConfigureAwait(false);
        OutboxStoreResultValidator.ValidatePublished(result);
        logger.Mutated(request.EventId, result);
        return result;
    }

    public async ValueTask<OutboxMutationResult> ReleaseAsync(
        OutboxReleaseRequest request,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.Validate(request);
        var result = await BoundedTaskHealthOperation.RunAsync(
            "outbox_release",
            limits.OperationTimeout,
            token => store.ReleaseAsync(request, timeProvider.GetUtcNow(), token),
            cancellationToken).ConfigureAwait(false);
        OutboxStoreResultValidator.ValidateReleased(result);
        logger.Mutated(request.EventId, result);
        return result;
    }

    public ValueTask<int> ReclaimExpiredAsync(
        int batchSize,
        CancellationToken cancellationToken) =>
        TaskHealthReclaimOperation.RunAsync(
            "outbox_reclaim",
            "outbox reclaim",
            store.ReclaimExpiredAsync,
            timeProvider,
            limits,
            batchSize,
            cancellationToken);
}
