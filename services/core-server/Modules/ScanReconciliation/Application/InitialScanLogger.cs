using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

public sealed class InitialScanLogger(ILogger<InitialReadOnlyScanService> logger)
{
    private static readonly Action<ILogger, Guid, Guid, Exception?> ScanStarted = LoggerMessage.Define<Guid, Guid>(
        LogLevel.Information,
        new EventId(4100, nameof(ScanStarted)),
        "Initial read-only scan {ScanId} started for library {LibraryId}");

    private static readonly Action<ILogger, Guid, int, Exception?> ScanCompleted = LoggerMessage.Define<Guid, int>(
        LogLevel.Information,
        new EventId(4101, nameof(ScanCompleted)),
        "Initial read-only scan {ScanId} completed with {EntryCount} entries");

    private static readonly Action<ILogger, Guid, string, Exception?> ScanFailed = LoggerMessage.Define<Guid, string>(
        LogLevel.Warning,
        new EventId(4102, nameof(ScanFailed)),
        "Initial read-only scan {ScanId} failed with code {FailureCode}");

    public void Started(Guid scanId, Guid libraryId) => ScanStarted(logger, scanId, libraryId, null);

    public void Completed(Guid scanId, int entryCount) => ScanCompleted(logger, scanId, entryCount, null);

    public void Failed(Guid scanId, string failureCode, Exception? exception) =>
        ScanFailed(logger, scanId, failureCode, exception);
}
