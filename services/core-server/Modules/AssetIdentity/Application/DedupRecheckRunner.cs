using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Re-reads the sources of a retained report and reports whether its every recorded observation still
/// holds. It runs the module's published recount comparison rather than a second, weaker one, and it
/// publishes a new version beside the old report instead of editing it.
/// </summary>
internal sealed class DedupRecheckRunner(
    DedupAnalyzer analyzer,
    DedupReportRegistry reports,
    DedupReportPublisher publisher,
    DedupJobViewFactory views,
    DedupExecutionOptions options,
    TimeProvider timeProvider)
{
    public async ValueTask<DedupRecheckView> RevalidateAsync(
        DedupResolvedSource source,
        Guid taskId,
        string? expectedDigest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!views.TryCurrent(source, taskId, out var key, out var report))
        {
            throw new ReadOnlyTrialException("dedup_report_not_retained");
        }

        if (expectedDigest is { Length: > 0 } && !string.Equals(expectedDigest, report.PlanDigest, StringComparison.Ordinal))
        {
            // The caller believes it is rechecking one plan; rechecking a different one would answer a
            // question nobody asked, so the mismatch is refused instead of reconciled silently.
            throw new ReadOnlyTrialException("idempotency_conflict");
        }

        var sources = DedupReportRehydrator.SourcesOf(report);
        if (sources.Count == 0)
        {
            throw new ReadOnlyTrialException("dedup_scope_rejected");
        }

        var result = await analyzer.RecountAsync(
            new DedupRecountRequest(DedupReportRehydrator.ToPlan(report), sources, options.RecheckTimeout),
            cancellationToken).ConfigureAwait(false);
        var published = result.CurrentPlan is { } current
            ? publisher.Publish(taskId, report.Limits, source, current)
            : key;
        reports.StoreEvidence(published, Evidence(result));
        return new DedupRecheckView(
            result.Status,
            result.PlanStillCurrent,
            [.. result.Reasons.Select(DedupText.Describe)],
            [.. result.ChangedPaths.Select(path => path.Value)],
            [.. result.DisappearedPaths.Select(path => path.Value)],
            [.. result.NewPaths.Select(path => path.Value)],
            result.PlanDigest,
            report.PlanDigest,
            published.VersionText,
            taskId,
            result.CurrentPlan is not null);
    }

    /// <summary>
    /// The evidence a later export ships. Counts are recorded beside the paths so a plan can state how
    /// much of its own evidence changed without re-reading the lists.
    /// </summary>
    private DedupRecheckEvidence Evidence(DedupRecountResult result) => new(
        Performed: true,
        Status: DedupJobWire.Recount(result.Status),
        [.. result.Reasons.Select(DedupText.Describe)],
        result.ChangedPaths.Count,
        result.DisappearedPaths.Count,
        result.NewPaths.Count,
        timeProvider.GetUtcNow(),
        result.PlanDigest);
}
