using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// One library the Host has already authorized as a dedup source. The browser only ever submits a
/// library id; the physical root here comes from the composition root's public library query, so a
/// page cannot ask the analyzer to read an arbitrary directory.
/// </summary>
public sealed record DedupResolvedSource(
    DedupSourceId SourceId,
    LibraryId LibraryId,
    CanonicalLibraryRoot Root,
    string DisplayName,
    DedupSourceRole Role = DedupSourceRole.RegisteredLibrary);

/// <summary>
/// What a caller may ask of one report page. Exactly one of <paramref name="GroupKey"/> and
/// <paramref name="Kind"/> selects the section; a cursor, when present, must belong to the same
/// report version, otherwise the page is refused instead of mixed with older evidence.
/// </summary>
public sealed record DedupPageRequest(
    string? Cursor,
    string? GroupKey,
    DedupFindingKind? Kind,
    int PageSize);

/// <summary>
/// Business operations of the dedup workbench. The library scope is supplied by the composition root
/// on every call, so authorization is re-checked per page, per recheck and per export rather than only
/// when a job was started.
/// </summary>
public interface IDedupJobCoordinator
{
    ValueTask<DedupJobView> StartAsync(
        DedupResolvedSource source,
        DedupAnalysisLimits limits,
        bool retry,
        DedupOperation operation,
        CancellationToken cancellationToken);

    ValueTask<DedupJobView> GetAsync(DedupResolvedSource source, Guid taskId, CancellationToken cancellationToken);

    ValueTask<DedupResultsPage> ResultsAsync(
        DedupResolvedSource source,
        Guid taskId,
        DedupPageRequest request,
        CancellationToken cancellationToken);

    ValueTask<DedupJobView> CancelAsync(
        DedupResolvedSource source,
        DedupOperation operation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Files a recheck of one retained report version as a durable background task. It answers with the
    /// receipt rather than the outcome: the recheck enumerates and hashes, so it is never run inside a
    /// request, and <see cref="RecheckStateAsync"/> is how a caller learns the result afterwards.
    /// </summary>
    ValueTask<DedupRecheckAccepted> RevalidateAsync(
        DedupResolvedSource source,
        Guid taskId,
        string? expectedDigest,
        CancellationToken cancellationToken);

    /// <summary>The state of one accepted recheck, addressed by the receipt's own durable task.</summary>
    ValueTask<DedupRecheckState> RecheckStateAsync(
        DedupResolvedSource source,
        Guid reportTaskId,
        Guid recheckTaskId,
        CancellationToken cancellationToken);

    ValueTask<DedupExportDocument> ExportAsync(
        DedupResolvedSource source,
        Guid taskId,
        string? expectedVersion,
        string? expectedDigest,
        CancellationToken cancellationToken);
}
