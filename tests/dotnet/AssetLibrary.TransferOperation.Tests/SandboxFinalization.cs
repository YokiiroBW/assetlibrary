namespace AssetLibrary.TransferOperation.Tests;

internal static class SandboxFinalization
{
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(5);

    public static CancellationTokenSource Create(
        DateTimeOffset requestDeadline,
        DateTimeOffset leaseExpiry)
    {
        var now = DateTimeOffset.UtcNow;
        var end = new[]
        {
            now + MaximumDuration,
            requestDeadline.ToUniversalTime(),
            leaseExpiry.ToUniversalTime(),
        }.Min();
        var duration = end - now;
        return new(duration > TimeSpan.Zero ? duration : TimeSpan.FromTicks(1));
    }
}
