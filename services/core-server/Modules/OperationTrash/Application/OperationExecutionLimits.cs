using AssetLibrary.Modules.OperationTrash.Domain;

namespace AssetLibrary.Modules.OperationTrash.Application;

public sealed record OperationExecutionLimits
{
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MaximumPreflightLifetime = TimeSpan.FromMinutes(15);

    public OperationExecutionLimits(
        TimeSpan operationTimeout,
        TimeSpan preflightLifetime,
        int maximumItems = OperationPlanPolicy.MaximumItems)
    {
        if (operationTimeout <= TimeSpan.Zero || operationTimeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        }

        if (preflightLifetime <= TimeSpan.Zero
            || preflightLifetime > MaximumPreflightLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(preflightLifetime));
        }

        if (maximumItems is < 1 or > OperationPlanPolicy.MaximumItems)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        OperationTimeout = operationTimeout;
        PreflightLifetime = preflightLifetime;
        MaximumItems = maximumItems;
    }

    public TimeSpan OperationTimeout { get; }

    public TimeSpan PreflightLifetime { get; }

    public int MaximumItems { get; }
}
