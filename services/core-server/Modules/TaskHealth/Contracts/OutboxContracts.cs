namespace AssetLibrary.Modules.TaskHealth.Contracts;

public enum OutboxEventState
{
    Pending = 0,
    Leased = 1,
    Published = 2,
    DeadLettered = 3,
}

public sealed record OutboxEnqueueRequest(
    OutboxEventId EventId,
    ModuleName SourceModule,
    EventTypeName EventType,
    Guid? AggregateId,
    int SchemaVersion,
    JsonObjectPayload Payload,
    DateTimeOffset OccurredAt,
    int MaxPublishAttempts = 10,
    DateTimeOffset? NotBefore = null);

public enum OutboxEnqueueStatus
{
    Created = 0,
    Existing = 1,
}

public sealed record OutboxEnqueueResult(
    OutboxEventId EventId,
    OutboxEnqueueStatus Status);

public readonly record struct OutboxLeaseIdentity(
    LeaseOwner Owner,
    LeaseToken Token,
    long Generation);

public sealed record OutboxClaimRequest(
    LeaseOwner Publisher,
    TimeSpan LeaseDuration,
    int BatchSize = 32);

public sealed record OutboxEventLease(
    OutboxEventId EventId,
    ModuleName SourceModule,
    EventTypeName EventType,
    Guid? AggregateId,
    int SchemaVersion,
    JsonObjectPayload Payload,
    DateTimeOffset OccurredAt,
    int PublishAttempt,
    int MaxPublishAttempts,
    OutboxLeaseIdentity Identity,
    DateTimeOffset LeaseUntil);

public sealed record OutboxPublishRequest(
    OutboxEventId EventId,
    OutboxLeaseIdentity Identity);

public sealed record OutboxReleaseRequest(
    OutboxEventId EventId,
    OutboxLeaseIdentity Identity,
    FailureCode FailureCode,
    TimeSpan RetryDelay);

public sealed record OutboxMutationResult(
    TaskLeaseMutationStatus Status,
    OutboxEventState? State);
