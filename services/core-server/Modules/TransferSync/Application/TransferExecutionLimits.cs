namespace AssetLibrary.Modules.TransferSync.Application;

public sealed record TransferExecutionLimits
{
    public static readonly TimeSpan MaximumOperationTimeout = TimeSpan.FromMinutes(10);

    public TransferExecutionLimits(TimeSpan operationTimeout)
    {
        if (operationTimeout <= TimeSpan.Zero || operationTimeout > MaximumOperationTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                $"The transfer timeout must be positive and at most {MaximumOperationTimeout}.");
        }

        OperationTimeout = operationTimeout;
    }

    public TimeSpan OperationTimeout { get; }
}
