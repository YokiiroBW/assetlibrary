namespace AssetLibrary.Modules.TaskHealth.Application;

public sealed class TaskHealthExecutionLimits
{
    public const int AbsoluteMaximumClaimBatchSize = 256;
    public const int AbsoluteMaximumReclaimBatchSize = 1024;
    public const int AbsoluteMaximumAttempts = 100;
    public static readonly TimeSpan AbsoluteMaximumLeaseDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan AbsoluteMaximumRetryDelay = TimeSpan.FromDays(1);

    public TaskHealthExecutionLimits(
        TimeSpan operationTimeout,
        int maximumClaimBatchSize = AbsoluteMaximumClaimBatchSize,
        int maximumReclaimBatchSize = AbsoluteMaximumReclaimBatchSize)
    {
        if (operationTimeout <= TimeSpan.Zero || operationTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                "The operation timeout must be positive and no longer than five minutes.");
        }

        if (maximumClaimBatchSize is <= 0 or > AbsoluteMaximumClaimBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumClaimBatchSize));
        }

        if (maximumReclaimBatchSize is <= 0 or > AbsoluteMaximumReclaimBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumReclaimBatchSize));
        }

        OperationTimeout = operationTimeout;
        MaximumClaimBatchSize = maximumClaimBatchSize;
        MaximumReclaimBatchSize = maximumReclaimBatchSize;
    }

    public TimeSpan OperationTimeout { get; }

    public int MaximumClaimBatchSize { get; }

    public int MaximumReclaimBatchSize { get; }

    public static TaskHealthExecutionLimits Default { get; } = new(TimeSpan.FromSeconds(30));
}

public sealed class TaskHealthOperationTimeoutException(string operation, Exception innerException)
    : TimeoutException($"TaskHealth operation '{operation}' exceeded its configured timeout.", innerException)
{
    public string Operation { get; } = operation;
}

internal static class BoundedTaskHealthOperation
{
    public static async ValueTask<T> RunAsync<T>(
        string operation,
        TimeSpan timeout,
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout);
        try
        {
            var result = await action(bounded.Token).ConfigureAwait(false);
            bounded.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException exception) when (
            bounded.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TaskHealthOperationTimeoutException(operation, exception);
        }
    }
}
