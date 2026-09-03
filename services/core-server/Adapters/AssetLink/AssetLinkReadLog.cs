using Microsoft.Extensions.Logging;

namespace AssetLibrary.CoreServer.Adapters.AssetLink;

internal static partial class AssetLinkReadLog
{
    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Information,
        Message = "AssetLink request {RequestId} operation {Operation} completed with status {StatusCode}.")]
    public static partial void Completed(
        ILogger logger,
        string requestId,
        string operation,
        int statusCode);

    [LoggerMessage(
        EventId = 6102,
        Level = LogLevel.Warning,
        Message = "AssetLink request {RequestId} operation {Operation} was rejected with code {ResultCode}.")]
    public static partial void Rejected(
        ILogger logger,
        string requestId,
        string operation,
        string resultCode);
}
