using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// The single place that states how one retained report version is addressed. A page, an export and
/// a recheck all bind to the same value, so a browser cannot read evidence from one version while
/// claiming another.
/// </summary>
internal static class DedupJobVersion
{
    public static string Text(DedupReportKey key) => key.VersionText;

    public static bool Matches(DedupReportKey key, Guid taskId) => key.TaskId == taskId;
}

/// <summary>
/// Rebuilds the report identity a request is allowed to address. It lives apart from the modules'
/// engine types so the identity rule can be reviewed on its own.
/// </summary>
internal static class DedupJobIdentity
{
    /// <summary>
    /// The task identity is derived from the library and the caller's operation key, so a retried
    /// request addresses the same durable job while a different key can never adopt someone else's.
    /// The digest is used as a stable identifier only, never as a security decision.
    /// </summary>
    public static Guid TaskId(LibraryId libraryId, DedupOperation operation)
    {
        Span<byte> material = stackalloc byte[32];
        libraryId.Value.TryWriteBytes(material);
        operation.IdempotencyKey.TryWriteBytes(material[16..]);
        var digest = System.Security.Cryptography.SHA256.HashData(material);
        return new Guid(digest.AsSpan(0, 16));
    }
}

internal static class DedupJobWire
{
    public static string State(DurableTaskState state) => state switch
    {
        DurableTaskState.Queued => "queued",
        DurableTaskState.Leased => "leased",
        DurableTaskState.Succeeded => "succeeded",
        DurableTaskState.Failed => "failed",
        DurableTaskState.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static string Recount(DedupRecountStatus status) => status switch
    {
        DedupRecountStatus.Identical => "identical",
        DedupRecountStatus.SourceChanged => "source_changed",
        DedupRecountStatus.UnreadableNow => "unreadable_now",
        DedupRecountStatus.Disappeared => "disappeared",
        DedupRecountStatus.NewContent => "new_content",
        DedupRecountStatus.Timeout => "timeout",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

/// <summary>
/// The immutable, replay-safe job payload: a reclaimed task re-runs exactly the analysis that was
/// requested, under the same ceilings, without consulting mutable state. A recheck carries the report
/// version it verifies instead of an analysis id, because a recheck that adopted a newer version would
/// answer about evidence nobody asked about.
/// </summary>
internal static class DedupJobPayload
{
    /// <summary>The payload a start writes: which library, under which ceilings.</summary>
    public static JsonObjectPayload Create(LibraryId libraryId, DedupAnalysisId analysisId, DedupAnalysisLimits limits) =>
        new($$"""
            {"kind":"analysis","library_id":"{{libraryId.Value:D}}","analysis_id":"{{analysisId.Value:D}}","version":1,"maximum_files":{{limits.MaximumFiles}},"maximum_bytes":{{limits.MaximumBytes}},"maximum_file_bytes":{{limits.MaximumFileBytes}},"hash_concurrency":{{limits.HashConcurrency}}}
            """);

    /// <summary>
    /// The payload a recheck writes: which report version is being verified and the digest the caller
    /// believed it was verifying. Both are read from the durable record by the worker, so a request that
    /// arrives after the report was replaced cannot recheck the newer one under the older name.
    /// </summary>
    public static JsonObjectPayload CreateRecheck(
        LibraryId libraryId,
        Guid reportTaskId,
        long generation,
        string planDigest) =>
        new($$"""
            {"kind":"recheck","library_id":"{{libraryId.Value:D}}","report_task_id":"{{reportTaskId:D}}","generation":{{generation}},"plan_digest":"{{planDigest}}","version":1}
            """);
}
