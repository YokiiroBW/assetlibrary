using System.Threading.RateLimiting;

namespace AssetLibrary.CoreServer.Hosting;

internal sealed class TrialLoginLimiter : IDisposable
{
    private readonly FixedWindowRateLimiter window = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = 20,
        Window = TimeSpan.FromMinutes(1),
        AutoReplenishment = true,
        QueueLimit = 0,
    });
    private readonly ConcurrencyLimiter concurrency = new(new ConcurrencyLimiterOptions
    {
        PermitLimit = 2,
        QueueLimit = 0,
    });

    public TrialLoginAdmission Acquire()
    {
        var windowLease = window.AttemptAcquire();
        if (!windowLease.IsAcquired)
        {
            windowLease.Dispose();
            return new TrialLoginAdmission(null, null, 60);
        }

        var concurrentLease = concurrency.AttemptAcquire();
        if (!concurrentLease.IsAcquired)
        {
            windowLease.Dispose();
            concurrentLease.Dispose();
            return new TrialLoginAdmission(null, null, 1);
        }

        return new TrialLoginAdmission(windowLease, concurrentLease, 0);
    }

    public void Dispose()
    {
        concurrency.Dispose();
        window.Dispose();
    }
}

internal sealed class TrialLoginAdmission(
    RateLimitLease? window,
    RateLimitLease? concurrency,
    int retryAfterSeconds) : IDisposable
{
    public bool IsAllowed => window is not null && concurrency is not null;

    public int RetryAfterSeconds { get; } = retryAfterSeconds;

    public void Dispose()
    {
        concurrency?.Dispose();
        window?.Dispose();
    }
}
