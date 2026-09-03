using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.TaskHealth.Application;

internal static class TaskHealthStoreResult
{
    public static void Validate(HealthStatusWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Status))
        {
            throw Invalid("health status write");
        }
    }

    public static void ValidateReclaimedCount(int count, int requestedBatchSize, string operation)
    {
        if (count < 0 || count > requestedBatchSize)
        {
            throw Invalid(operation);
        }
    }

    public static InvalidOperationException Invalid(string operation) =>
        new($"The TaskHealth store returned an invalid result for {operation}.");
}
