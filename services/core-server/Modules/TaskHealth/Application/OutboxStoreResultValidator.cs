using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.TaskHealth.Application;

internal static class OutboxStoreResultValidator
{
    public static void Validate(OutboxEnqueueResult result, OutboxEnqueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.EventId.Value == Guid.Empty
            || result.EventId != request.EventId
            || !Enum.IsDefined(result.Status))
        {
            throw TaskHealthStoreResult.Invalid("outbox enqueue");
        }
    }

    public static void Validate(
        IReadOnlyList<OutboxEventLease> leases,
        OutboxClaimRequest request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(leases);
        if (leases.Count > request.BatchSize)
        {
            throw TaskHealthStoreResult.Invalid("outbox claim batch size");
        }

        var eventIds = new HashSet<Guid>();
        foreach (var lease in leases)
        {
            if (lease.EventId.Value == Guid.Empty
                || string.IsNullOrWhiteSpace(lease.SourceModule.Value)
                || string.IsNullOrWhiteSpace(lease.EventType.Value)
                || string.IsNullOrWhiteSpace(lease.Payload.Value)
                || lease.AggregateId == Guid.Empty
                || lease.SchemaVersion is <= 0 or > 1000
                || lease.OccurredAt == default
                || lease.PublishAttempt <= 0
                || lease.MaxPublishAttempts > TaskHealthExecutionLimits.AbsoluteMaximumAttempts
                || lease.PublishAttempt > lease.MaxPublishAttempts
                || lease.Identity.Owner != request.Publisher
                || lease.Identity.Token.Value == Guid.Empty
                || lease.Identity.Generation <= 0
                || lease.LeaseUntil <= now
                || lease.LeaseUntil > now + request.LeaseDuration
                || !eventIds.Add(lease.EventId.Value))
            {
                throw TaskHealthStoreResult.Invalid("outbox claim");
            }
        }
    }

    public static void ValidatePublished(OutboxMutationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Status)
            || (result.Status == TaskLeaseMutationStatus.Accepted) != result.State.HasValue
            || result.State is { } state
                && (!Enum.IsDefined(state) || state != OutboxEventState.Published))
        {
            throw TaskHealthStoreResult.Invalid("outbox publish");
        }
    }

    public static void ValidateReleased(OutboxMutationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Status)
            || (result.Status == TaskLeaseMutationStatus.Accepted) != result.State.HasValue
            || result.State is { } state
                && (!Enum.IsDefined(state)
                    || state is not OutboxEventState.Pending and not OutboxEventState.DeadLettered))
        {
            throw TaskHealthStoreResult.Invalid("outbox release");
        }
    }
}
