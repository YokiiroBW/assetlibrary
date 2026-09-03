using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Contracts;

public sealed record InitialScanRequest(
    LibraryId LibraryId,
    TimeSpan Timeout,
    int BatchSize = 256);

public enum InitialScanStatus
{
    Completed = 0,
    LibraryNotFound = 1,
    StorageOffline = 2,
    DiscoveryFailed = 3,
    TimedOut = 4,
}

public sealed record InitialScanResult(
    Guid? ScanId,
    InitialScanStatus Status,
    int ObservedEntries,
    int CommittedEntries,
    string? FailureCode);
