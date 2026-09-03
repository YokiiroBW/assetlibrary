using Microsoft.Extensions.Logging;

namespace AssetLibrary.CoreServer.Hosting;

internal static partial class CoreServerHostLog
{
    [LoggerMessage(
        EventId = 8001,
        Level = LogLevel.Information,
        Message = "Core server host started contract={Contract} port={Port} production_file_writes={ProductionFileWrites}")]
    public static partial void Started(
        ILogger logger,
        string contract,
        int port,
        bool productionFileWrites);

    [LoggerMessage(
        EventId = 8002,
        Level = LogLevel.Information,
        Message = "Core server host stopping contract={Contract}")]
    public static partial void Stopping(ILogger logger, string contract);
}
