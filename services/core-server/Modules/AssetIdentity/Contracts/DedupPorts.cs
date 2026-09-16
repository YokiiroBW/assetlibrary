using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;

/// <summary>A single content read this analysis wants the adapter to perform.</summary>
public sealed record DedupContentReadRequest(
    string SourceIdentity,
    CanonicalLibraryRoot Root,
    RelativeAssetPath RelativePath,
    int MaximumBytes);

/// <summary>
/// Streams the bytes of explicitly allowed files. An adapter must never write, never follow a
/// reparse point and never fall back to a name-based comparison when a read fails.
/// </summary>
public interface IDedupContentReader
{
    Task<IReadOnlyList<ContentReadResult>> ReadAsync(
        IReadOnlyList<DedupContentReadRequest> requests,
        int concurrency,
        CancellationToken cancellationToken);
}

/// <summary>
/// Enumerates physical entries below one allowed root. The shared read-only discovery adapter is
/// reused instead of introducing a second traversal implementation, so an entry is reported with
/// the discovery policy's own include/exclude verdict rather than a second set of rules.
/// </summary>
public interface IDedupFileDiscovery
{
    IAsyncEnumerable<DiscoveredFile> DiscoverAsync(
        CanonicalLibraryRoot root,
        CancellationToken cancellationToken);
}

public sealed record DiscoveredFile(
    RelativeAssetPath RelativePath,
    bool IsReparsePoint,
    bool IsExcluded,
    long Length,
    DateTimeOffset LastWriteTimeUtc);

public interface IDedupSourceAvailability
{
    ValueTask<StorageAvailability> CheckAsync(
        CanonicalLibraryRoot root,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves which submitted sources this installation may read. Already registered library roots
/// and the managed output area are read through their own owners, so they can never be re-imported
/// as a fresh inbound source.
/// </summary>
public interface IDedupSourceScopeQuery
{
    ValueTask<IReadOnlyList<CanonicalLibraryRoot>> RegisteredRootsAsync(
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<CanonicalLibraryRoot>> ManagedOutputRootsAsync(
        CancellationToken cancellationToken);
}

public interface IDedupClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemDedupClock : IDedupClock
{
    public static SystemDedupClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed record DedupRejectedSourceRequest(
    DedupSourceRequest Source,
    DedupSourceRejection Rejection);

public sealed record DedupAnalysisScope(
    IReadOnlyList<DedupSourceRequest> Accepted,
    IReadOnlyList<DedupRejectedSourceRequest> Rejected)
{
    public bool AnyAccepted => Accepted.Count > 0;
}

/// <summary>
/// A read-only walk that could not complete. It is reported as an incomplete scan instead of being
/// turned into "this source has no files".
/// </summary>
public sealed class DedupDiscoveryException(string failureCode, Exception? innerException = null)
    : IOException("Read-only discovery did not produce a complete snapshot.", innerException)
{
    public string FailureCode { get; } = string.IsNullOrWhiteSpace(failureCode)
        ? throw new ArgumentException("A discovery failure code is required.", nameof(failureCode))
        : failureCode;
}