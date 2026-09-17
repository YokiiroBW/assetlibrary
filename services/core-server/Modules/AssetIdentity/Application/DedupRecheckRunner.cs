using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Accepts a request to re-verify one retained report version as a durable background task, and runs
/// that recheck when the lease for it is claimed. A recheck enumerates and hashes, so it is never done
/// inside an HTTP request: a large library would time out while holding a connection, and a result that
/// arrived after the caller gave up would have no owner. TaskHealth owns the task, its lease and its
/// cancellation record exactly as it does for an analysis.
/// </summary>
internal sealed class DedupRecheckScheduler(
    IDurableTaskCoordinator tasks,
    DedupJobViewFactory views,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Files the recheck as a durable task and answers at once. The report version the caller named is
    /// recorded in the payload, so the worker verifies that version and refuses to verify a newer one
    /// that happens to exist by the time the task is claimed.
    /// </summary>
    public async ValueTask<DedupRecheckAccepted> AcceptAsync(
        DedupResolvedSource source,
        Guid reportTaskId,
        string? expectedDigest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!views.TryCurrent(source, reportTaskId, out var key, out var report))
        {
            throw new ReadOnlyTrialException("dedup_report_not_retained");
        }

        if (expectedDigest is { Length: > 0 } && !string.Equals(expectedDigest, report.PlanDigest, StringComparison.Ordinal))
        {
            // The caller believes it is rechecking one plan; rechecking a different one would answer a
            // question nobody asked, so the mismatch is refused instead of reconciled silently.
            throw new ReadOnlyTrialException("idempotency_conflict");
        }

        if (DedupReportRehydrator.SourcesOf(report).Count == 0)
        {
            throw new ReadOnlyTrialException("dedup_scope_rejected");
        }

        var recheckTaskId = Guid.NewGuid();
        var enqueued = await tasks.EnqueueAsync(
            new DurableTaskEnqueueRequest(
                new DurableTaskId(recheckTaskId),
                // One recheck per report version: asking twice for the same version addresses the same
                // durable task instead of spending a second full pass over the same sources.
                new TaskIdempotencyKey($"dedup-recheck:{key.TaskId:D}:{key.Generation}"),
                DedupExecutionOptions.TaskType,
                DedupJobPayload.CreateRecheck(source.LibraryId, key.TaskId, key.Generation, report.PlanDigest),
                TaskPriority.P3,
                2,
                timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        if (enqueued.Status == DurableTaskEnqueueStatus.Existing && enqueued.TaskId.Value != recheckTaskId)
        {
            // The version was already being rechecked under its own durable task; that attempt owns it.
            recheckTaskId = enqueued.TaskId.Value;
        }

        return new DedupRecheckAccepted(recheckTaskId, key.VersionText, report.PlanDigest);
    }
}

/// <summary>The receipt of an accepted recheck: which version is being verified and under which task.</summary>
public sealed record DedupRecheckAccepted(Guid RecheckTaskId, string AnalysisVersion, string PlanDigest);

/// <summary>
/// Runs one accepted recheck under the lease it was claimed with. It re-reads the sources of the
/// report the payload names, runs the module's published recount comparison, and files the outcome
/// beside the job. A version that no longer exists is not silently re-bound: the run states that the
/// report it was asked about is gone.
/// </summary>
internal sealed class DedupRecheckRunner(
    DedupAnalyzer analyzer,
    DedupReportRegistry reports,
    DedupReportPublisher publisher,
    DedupExecutionOptions options,
    TimeProvider timeProvider)
{
    public async ValueTask RunAsync(
        Guid recheckTaskId,
        DedupResolvedSource source,
        DedupJobPayloadReader.RecheckTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var key = new DedupReportKey(target.ReportTaskId, target.Generation);
        if (!reports.TryGet(key, out var report) || report.LibraryId != source.LibraryId)
        {
            reports.RecordRecheck(recheckTaskId, Missing(target));
            return;
        }

        if (!string.Equals(report.PlanDigest, target.PlanDigest, StringComparison.Ordinal))
        {
            // The retained version is no longer the one the caller asked about. Answering about it would
            // present newer evidence as the result of an older question.
            reports.RecordRecheck(recheckTaskId, Superseded(target, key));
            return;
        }

        var sources = DedupReportRehydrator.SourcesOf(report);
        if (sources.Count == 0)
        {
            reports.RecordRecheck(recheckTaskId, Rejected(target, key));
            return;
        }

        var result = await analyzer.RecountAsync(
            DedupReportRehydrator.Recount(report, options.RecheckTimeout),
            cancellationToken).ConfigureAwait(false);

        // The plan the recheck produced becomes the next version of the same job, and the old one stays
        // readable: a reader can still see what was verified and what replaced it.
        var published = result.CurrentPlan is { } current
            ? publisher.Publish(target.ReportTaskId, report.Limits, source, current)
            : key;
        reports.StoreEvidence(published, Evidence(result));
        reports.RecordRecheck(recheckTaskId, Completed(target, key, published, result));
    }

    private static DedupRecheckRun Completed(
        DedupJobPayloadReader.RecheckTarget target,
        DedupReportKey key,
        DedupReportKey published,
        DedupRecountResult result) => new(
        Completed: true,
        result.Status,
        PlanStillCurrent: result.PlanStillCurrent,
        Reasons: [.. result.Reasons.Select(DedupText.Describe)],
        ChangedPaths: [.. result.ChangedPaths.Select(path => path.Value)],
        DisappearedPaths: [.. result.DisappearedPaths.Select(path => path.Value)],
        NewPaths: [.. result.NewPaths.Select(path => path.Value)],
        PlanDigest: result.PlanDigest,
        PreviousPlanDigest: key.VersionText,
        VerifiedGeneration: target.Generation,
        AnalysisVersion: published.VersionText,
        FailureCode: null);

    private static DedupRecheckRun Missing(DedupJobPayloadReader.RecheckTarget target) => new(
        Completed: false,
        Status: DedupRecountStatus.SourceChanged,
        PlanStillCurrent: false,
        Reasons: ["要复核的报告版本已不再保留。"],
        ChangedPaths: [],
        DisappearedPaths: [],
        NewPaths: [],
        PlanDigest: target.PlanDigest,
        PreviousPlanDigest: null,
        VerifiedGeneration: target.Generation,
        AnalysisVersion: string.Empty,
        FailureCode: "dedup_report_not_retained");

    private static DedupRecheckRun Superseded(DedupJobPayloadReader.RecheckTarget target, DedupReportKey key) => new(
        Completed: false,
        Status: DedupRecountStatus.SourceChanged,
        PlanStillCurrent: false,
        Reasons: ["要复核的报告版本已被更新的版本取代，本次复核未执行。"],
        ChangedPaths: [],
        DisappearedPaths: [],
        NewPaths: [],
        PlanDigest: target.PlanDigest,
        PreviousPlanDigest: null,
        VerifiedGeneration: target.Generation,
        AnalysisVersion: key.VersionText,
        FailureCode: "dedup_version_conflict");

    private static DedupRecheckRun Rejected(DedupJobPayloadReader.RecheckTarget target, DedupReportKey key) => new(
        Completed: false,
        Status: DedupRecountStatus.SourceChanged,
        PlanStillCurrent: false,
        Reasons: ["报告没有可复核的来源范围。"],
        ChangedPaths: [],
        DisappearedPaths: [],
        NewPaths: [],
        PlanDigest: target.PlanDigest,
        PreviousPlanDigest: null,
        VerifiedGeneration: target.Generation,
        AnalysisVersion: key.VersionText,
        FailureCode: "dedup_scope_rejected");

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
