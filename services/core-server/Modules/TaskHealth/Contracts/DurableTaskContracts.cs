namespace AssetLibrary.Modules.TaskHealth.Contracts;

public enum TaskPriority
{
    P0 = 0,
    P1 = 1,
    P2 = 2,
    P3 = 3,
    P4 = 4,
    P5 = 5,
}

public enum DurableTaskState
{
    Queued = 0,
    Leased = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

public sealed record DurableTaskEnqueueRequest(
    DurableTaskId TaskId,
    TaskIdempotencyKey IdempotencyKey,
    TaskTypeName TaskType,
    JsonObjectPayload Payload,
    TaskPriority Priority,
    int MaxAttempts,
    DateTimeOffset? NotBefore = null);

public enum DurableTaskEnqueueStatus
{
    Created = 0,
    Existing = 1,
}

public sealed record DurableTaskEnqueueResult(
    DurableTaskId TaskId,
    DurableTaskEnqueueStatus Status);

public readonly record struct TaskLeaseIdentity(
    LeaseOwner Owner,
    LeaseToken Token,
    long Generation);

public sealed record DurableTaskClaimRequest(
    LeaseOwner Worker,
    TimeSpan LeaseDuration,
    int BatchSize = 1);

public sealed record DurableTaskLease(
    DurableTaskId TaskId,
    TaskTypeName TaskType,
    JsonObjectPayload Payload,
    TaskPriority Priority,
    int Attempt,
    int MaxAttempts,
    TaskLeaseIdentity Identity,
    DateTimeOffset LeaseUntil,
    bool CancellationRequested);

public sealed record DurableTaskHeartbeatRequest(
    DurableTaskId TaskId,
    TaskLeaseIdentity Identity,
    TimeSpan LeaseDuration);

public enum TaskLeaseMutationStatus
{
    Accepted = 0,
    NotCurrent = 1,
}

public sealed record DurableTaskHeartbeatResult(
    TaskLeaseMutationStatus Status,
    bool CancellationRequested,
    DateTimeOffset? LeaseUntil);

public enum DurableTaskFinishKind
{
    Succeeded = 0,
    RetryableFailure = 1,
    PermanentFailure = 2,
    Cancelled = 3,
}

public sealed record DurableTaskFinishRequest(
    DurableTaskId TaskId,
    TaskLeaseIdentity Identity,
    DurableTaskFinishKind Kind,
    FailureCode? FailureCode = null,
    TimeSpan? RetryDelay = null);

public sealed record DurableTaskFinishResult(
    TaskLeaseMutationStatus Status,
    DurableTaskState? State);

public enum TaskCancellationStatus
{
    Cancelled = 0,
    Requested = 1,
    AlreadyCancelled = 2,
    AlreadyTerminal = 3,
    NotFound = 4,
}

public sealed record DurableTaskCancellationResult(TaskCancellationStatus Status);

public sealed record DurableTaskSnapshot(
    DurableTaskId TaskId,
    DurableTaskState State,
    int Attempts,
    int MaxAttempts,
    bool CancellationRequested);
