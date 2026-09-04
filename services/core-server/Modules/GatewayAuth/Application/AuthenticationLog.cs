using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal static partial class AuthenticationLog
{
    [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "Local sign-in succeeded.")]
    public static partial void LocalSignInSucceeded(ILogger logger);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "Local sign-in was rejected.")]
    public static partial void LocalSignInRejected(ILogger logger);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Warning, Message = "Authentication operation timed out.")]
    public static partial void OperationTimedOut(ILogger logger);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Debug, Message = "Browser session authentication was rejected.")]
    public static partial void SessionRejected(ILogger logger);
}
