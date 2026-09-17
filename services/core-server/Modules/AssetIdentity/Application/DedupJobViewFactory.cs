using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Turns a durable task's generic facts into the dedup workbench's own view. It is the only place that
/// decides whether a job may be cancelled, whether a report exists, and what a page may claim about a
/// job that has not finished.
/// </summary>
internal sealed class DedupJobViewFactory(DedupReportRegistry reports, TimeProvider timeProvider)
{
    /// <summary>Finds the newest retained report this job owns, if the library still holds one.</summary>
    public bool TryCurrent(DedupResolvedSource source, Guid taskId, out DedupReportKey key, out DedupReport report)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (reports.TryLatest(source.LibraryId.Value, out key, out report) && key.TaskId == taskId)
        {
            return true;
        }

        key = default;
        report = null!;
        return false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "CA1506:Avoid excessive class coupling",
        Justification = "A job view is a projection of the durable task's own snapshot field by field; the coupling is the shape of the record it must fill, not a dependency on other modules.")]
    public DedupJobView Of(DedupResolvedSource source, Guid taskId, DurableTaskDetails details)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(details);
        var available = TryCurrent(source, taskId, out var key, out _);
        var terminal = !IsActive(details.Snapshot.State);
        return new DedupJobView(
            taskId,
            source.LibraryId,
            source.DisplayName,
            DedupJobWire.State(details.Snapshot.State),
            details.Snapshot.CancellationRequested,
            !terminal,
            details.Snapshot.State == DurableTaskState.Failed,
            available,
            available ? key.VersionText : string.Empty,
            details.CreatedAt,
            details.UpdatedAt,
            details.FailureCode,
            DedupJobContractText.RetentionBoundary);
    }

    /// <summary>
    /// Facts about a job whose report is gone. A missing report is reported as missing: it is never
    /// rendered as an analysis that found nothing.
    /// </summary>
    public DedupReportSummary MissingSummary(Guid taskId, DedupReportKey key) =>
        new(
            DedupContractText.PolicyVersion,
            timeProvider.GetUtcNow(),
            string.Empty,
            new DedupPlanStatistics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            new DedupPlanSummary(DedupAnalysisStatus.SourceRejected, [], [], [], false, "dedup_report_not_retained", []),
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            DedupJobContractText.RetentionBoundary,
            key.TaskId == taskId ? key.VersionText : string.Empty);

    public static DedupReportSummary Summary(DedupReportKey key, DedupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new DedupReportSummary(
            report.PolicyVersion,
            report.AnalyzedAt,
            report.PlanDigest,
            report.Statistics,
            report.Summary,
            report.Groups.Count,
            report.DuplicateItemCount,
            report.Unverified.Count,
            report.Unreadable.Count,
            report.FindingCount(DedupFindingKind.Unique),
            report.DuplicateItemCount + report.Unverified.Count + report.Unreadable.Count,
            report.Truncated,
            report.RetentionBoundary,
            key.VersionText);
    }

    public static bool IsActive(DurableTaskState state) => state is DurableTaskState.Queued or DurableTaskState.Leased;
}
