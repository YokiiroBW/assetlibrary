using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal static partial class AuthorizedReadLog
{
    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "Read-only operation {Operation} completed in {ElapsedMilliseconds} ms.")]
    public static partial void Completed(
        ILogger logger,
        string operation,
        double elapsedMilliseconds);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Warning,
        Message = "Read-only operation {Operation} exceeded its {TimeoutMilliseconds} ms deadline.")]
    public static partial void TimedOut(
        ILogger logger,
        string operation,
        double timeoutMilliseconds);
}
