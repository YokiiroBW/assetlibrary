using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.TaskHealth.Domain;

public enum ExpiredTaskLeaseDisposition
{
    Requeue = 0,
    Fail = 1,
    Cancel = 2,
}

public static class DurableTaskStatePolicy
{
    public static bool IsTerminal(DurableTaskState state) =>
        state is DurableTaskState.Succeeded or DurableTaskState.Failed or DurableTaskState.Cancelled;

    public static TaskCancellationStatus DecideCancellation(DurableTaskState state) => state switch
    {
        DurableTaskState.Queued => TaskCancellationStatus.Cancelled,
        DurableTaskState.Leased => TaskCancellationStatus.Requested,
        DurableTaskState.Cancelled => TaskCancellationStatus.AlreadyCancelled,
        DurableTaskState.Succeeded or DurableTaskState.Failed => TaskCancellationStatus.AlreadyTerminal,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static ExpiredTaskLeaseDisposition DecideExpiredLease(DurableTaskSnapshot task)
    {
        ValidateLeased(task);

        if (task.CancellationRequested)
        {
            return ExpiredTaskLeaseDisposition.Cancel;
        }

        return task.Attempts >= task.MaxAttempts
            ? ExpiredTaskLeaseDisposition.Fail
            : ExpiredTaskLeaseDisposition.Requeue;
    }

    public static DurableTaskState DecideFinish(
        DurableTaskSnapshot task,
        DurableTaskFinishKind finishKind)
    {
        ValidateLeased(task);
        if (!Enum.IsDefined(finishKind))
        {
            throw new ArgumentOutOfRangeException(nameof(finishKind));
        }

        if (finishKind == DurableTaskFinishKind.Succeeded)
        {
            return DurableTaskState.Succeeded;
        }

        if (task.CancellationRequested)
        {
            return DurableTaskState.Cancelled;
        }

        return finishKind switch
        {
            DurableTaskFinishKind.RetryableFailure when task.Attempts < task.MaxAttempts =>
                DurableTaskState.Queued,
            DurableTaskFinishKind.RetryableFailure or DurableTaskFinishKind.PermanentFailure =>
                DurableTaskState.Failed,
            DurableTaskFinishKind.Cancelled => throw new InvalidOperationException(
                "A worker can confirm cancellation only after cancellation was requested."),
            _ => throw new ArgumentOutOfRangeException(nameof(finishKind)),
        };
    }

    public static void ValidateFinish(DurableTaskFinishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failureRequired = request.Kind is
            DurableTaskFinishKind.RetryableFailure or DurableTaskFinishKind.PermanentFailure;
        if (failureRequired != request.FailureCode.HasValue)
        {
            throw new ArgumentException(
                "Failure outcomes require exactly one failure code.",
                nameof(request));
        }

        if (request.Kind == DurableTaskFinishKind.RetryableFailure)
        {
            if (request.RetryDelay is null
                || request.RetryDelay < TimeSpan.Zero
                || request.RetryDelay > TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "A retryable failure requires a retry delay between zero and one day.");
            }
        }
        else if (request.RetryDelay is not null)
        {
            throw new ArgumentException(
                "A retry delay is valid only for a retryable failure.",
                nameof(request));
        }
    }

    private static void ValidateLeased(DurableTaskSnapshot task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.State != DurableTaskState.Leased)
        {
            throw new InvalidOperationException("Only a leased task can transition through a worker result.");
        }

        if (task.Attempts <= 0 || task.MaxAttempts <= 0 || task.Attempts > task.MaxAttempts)
        {
            throw new ArgumentException("Task attempt counters are inconsistent.", nameof(task));
        }
    }
}
