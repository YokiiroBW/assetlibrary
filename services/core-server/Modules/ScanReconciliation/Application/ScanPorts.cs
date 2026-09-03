using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Domain;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

public interface IReadOnlyFileDiscovery
{
    IAsyncEnumerable<DiscoveredEntry> DiscoverAsync(
        LibraryScanTarget target,
        CancellationToken cancellationToken);
}

public enum ScanRunTerminalState
{
    Completed = 0,
    DiscoveryFailed = 1,
    Cancelled = 2,
    TimedOut = 3,
}

public interface IScanRunJournal
{
    ValueTask StartAsync(
        Guid scanId,
        LibraryId libraryId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    ValueTask FinishAsync(
        Guid scanId,
        ScanRunTerminalState state,
        int observedEntries,
        int committedEntries,
        string? failureCode,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken);
}

public sealed class FileDiscoveryException(string failureCode, Exception? innerException = null)
    : IOException("Read-only file discovery did not produce a complete snapshot.", innerException)
{
    public string FailureCode { get; } = string.IsNullOrWhiteSpace(failureCode)
        ? throw new ArgumentException("A discovery failure code is required.", nameof(failureCode))
        : failureCode;
}
