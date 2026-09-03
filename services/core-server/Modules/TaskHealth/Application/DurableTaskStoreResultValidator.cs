using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.TaskHealth.Application;

internal static class DurableTaskStoreResultValidator
{
    public static void Validate(
        DurableTaskEnqueueResult result,
        DurableTaskEnqueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.TaskId.Value == Guid.Empty
            || !Enum.IsDefined(result.Status)
            || (result.Status == DurableTaskEnqueueStatus.Created && result.TaskId != request.TaskId))
        {
            throw InvalidStoreResult("task enqueue");
        }
    }

    public static void Validate(
        IReadOnlyList<DurableTaskLease> leases,
        DurableTaskClaimRequest request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(leases);
        if (leases.Count > request.BatchSize)
        {
            throw InvalidStoreResult("task claim batch size");
        }

        var taskIds = new HashSet<Guid>();
        foreach (var lease in leases)
        {
            if (lease.TaskId.Value == Guid.Empty
                || string.IsNullOrWhiteSpace(lease.TaskType.Value)
                || string.IsNullOrWhiteSpace(lease.Payload.Value)
                || !Enum.IsDefined(lease.Priority)
                || lease.Attempt <= 0
                || lease.MaxAttempts > TaskHealthExecutionLimits.AbsoluteMaximumAttempts
                || lease.Attempt > lease.MaxAttempts
                || lease.Identity.Owner != request.Worker
                || lease.Identity.Token.Value == Guid.Empty
                || lease.Identity.Generation <= 0
                || lease.LeaseUntil <= now
                || lease.LeaseUntil > now + request.LeaseDuration
                || !taskIds.Add(lease.TaskId.Value))
            {
                throw InvalidStoreResult("task claim");
            }
        }
    }

    public static void Validate(
        DurableTaskHeartbeatResult result,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Status)
            || (result.Status == TaskLeaseMutationStatus.Accepted) != result.LeaseUntil.HasValue
            || (result.Status == TaskLeaseMutationStatus.NotCurrent && result.CancellationRequested))
        {
            throw InvalidStoreResult("task heartbeat");
        }

        if (result.LeaseUntil is { } leaseUntil
            && (leaseUntil <= now || leaseUntil > now + leaseDuration))
        {
            throw InvalidStoreResult("task heartbeat lease");
        }
    }

    public static void Validate(DurableTaskFinishResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Status)
            || (result.Status == TaskLeaseMutationStatus.Accepted) != result.State.HasValue
            || result.State is { } state && (!Enum.IsDefined(state) || state == DurableTaskState.Leased))
        {
            throw InvalidStoreResult("task finish");
        }
    }

    public static void Validate(DurableTaskCancellationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Status))
        {
            throw InvalidStoreResult("task cancellation");
        }
    }

    private static InvalidOperationException InvalidStoreResult(string operation) =>
        TaskHealthStoreResult.Invalid(operation);
}
