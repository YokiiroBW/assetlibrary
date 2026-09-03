using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.TaskHealth.Application;

public sealed class HealthStatusService(
    IHealthStatusStore store,
    TimeProvider timeProvider,
    TaskHealthExecutionLimits limits,
    TaskHealthLogger logger)
{
    public async ValueTask<HealthStatusWriteResult> WriteAsync(
        HealthStatusUpdate update,
        CancellationToken cancellationToken)
    {
        TaskHealthRequestValidator.Validate(update);
        var normalized = update with { ObservedAt = update.ObservedAt.ToUniversalTime() };
        var result = await BoundedTaskHealthOperation.RunAsync(
            "health_write",
            limits.OperationTimeout,
            token => store.WriteAsync(normalized, timeProvider.GetUtcNow(), token),
            cancellationToken).ConfigureAwait(false);
        TaskHealthStoreResult.Validate(result);
        logger.Health(update.Component, result);
        return result;
    }
}
