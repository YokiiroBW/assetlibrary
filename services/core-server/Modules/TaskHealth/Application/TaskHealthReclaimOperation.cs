namespace AssetLibrary.Modules.TaskHealth.Application;

internal static class TaskHealthReclaimOperation
{
    public static async ValueTask<int> RunAsync(
        string operation,
        string resultContext,
        Func<DateTimeOffset, int, CancellationToken, ValueTask<int>> reclaim,
        TimeProvider timeProvider,
        TaskHealthExecutionLimits limits,
        int batchSize,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.ValidateReclaimBatch(batchSize, limits);
        var reclaimed = await BoundedTaskHealthOperation.RunAsync(
            operation,
            limits.OperationTimeout,
            token => reclaim(timeProvider.GetUtcNow(), batchSize, token),
            cancellationToken).ConfigureAwait(false);
        TaskHealthStoreResult.ValidateReclaimedCount(reclaimed, batchSize, resultContext);
        return reclaimed;
    }
}
