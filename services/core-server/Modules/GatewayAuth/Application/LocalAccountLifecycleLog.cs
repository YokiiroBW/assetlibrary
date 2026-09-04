using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal static partial class LocalAccountLifecycleLog
{
    [LoggerMessage(
        EventId = 2110,
        Level = LogLevel.Information,
        Message = "Local account lifecycle operation {Action} completed with {Outcome} in {ElapsedMilliseconds} ms.")]
    public static partial void Completed(
        ILogger logger,
        string action,
        int outcome,
        long elapsedMilliseconds);

    [LoggerMessage(
        EventId = 2111,
        Level = LogLevel.Warning,
        Message = "Local account lifecycle dependency failed for {Action} after {ElapsedMilliseconds} ms.")]
    public static partial void DependencyFailed(
        ILogger logger,
        string action,
        long elapsedMilliseconds);
}
