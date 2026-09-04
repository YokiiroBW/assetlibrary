using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal static partial class AdministratorBootstrapRecoveryLog
{
    [LoggerMessage(
        EventId = 2120,
        Level = LogLevel.Information,
        Message = "Administrator bootstrap or recovery operation {Action} completed with {Outcome} in {ElapsedMilliseconds} ms.")]
    public static partial void Completed(
        ILogger logger,
        string action,
        int outcome,
        long elapsedMilliseconds);

    [LoggerMessage(
        EventId = 2121,
        Level = LogLevel.Warning,
        Message = "Administrator bootstrap or recovery dependency failed for {Action} after {ElapsedMilliseconds} ms.")]
    public static partial void DependencyFailed(
        ILogger logger,
        string action,
        long elapsedMilliseconds);
}
