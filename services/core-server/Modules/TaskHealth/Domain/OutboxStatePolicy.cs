using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.TaskHealth.Domain;

public enum ExpiredOutboxLeaseDisposition
{
    Retry = 0,
    DeadLetter = 1,
}

public static class OutboxStatePolicy
{
    public static ExpiredOutboxLeaseDisposition DecideExpiredLease(
        int publishAttempts,
        int maxPublishAttempts)
    {
        if (publishAttempts <= 0
            || maxPublishAttempts <= 0
            || publishAttempts > maxPublishAttempts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(publishAttempts),
                "Outbox attempt counters are inconsistent.");
        }

        return publishAttempts >= maxPublishAttempts
            ? ExpiredOutboxLeaseDisposition.DeadLetter
            : ExpiredOutboxLeaseDisposition.Retry;
    }

    public static void ValidateRelease(OutboxReleaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RetryDelay < TimeSpan.Zero || request.RetryDelay > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "An outbox retry delay must be between zero and one day.");
        }
    }
}
