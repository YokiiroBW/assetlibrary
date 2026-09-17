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
/// What one attempt is about to verify, read from the retained report before any byte is read. A refusal
/// is <see cref="Refused"/>: it has already been filed as the outcome, so the attempt has nothing left to
/// scan and must not treat the absence of work as a failure.
/// </summary>
internal sealed record DedupRecheckPreparation(
    DedupJobPayloadReader.RecheckTarget Target,
    DedupReportKey Key,
    string PlanDigest,
    DedupRecountRequest Request)
{
    public static DedupRecheckPreparation Refused { get; } = new(
        new DedupJobPayloadReader.RecheckTarget(Guid.Empty, 0, string.Empty),
        new DedupReportKey(Guid.Empty, 0),
        string.Empty,
        null!);

    public bool CanScan => Request is not null;
}

/// <summary>
/// Runs one accepted recheck under the lease it was claimed with. It is deliberately split in two. A
/// recheck enumerates and hashes a real directory, so the scan is prepared and then performed with no
/// fence held: a filesystem walk inside a fenced commit would hold the durable task's row for the length
/// of a library read, and a slow share would turn one recheck into a lock every other attempt waits on.
/// Only the verdict is filed inside the fence, by a compare-and-file step that also checks the version is
/// still the one a reader would see, so a report that moved on while the scan ran is never answered with
/// stale evidence and never overwritten by it.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "One recheck is inherently the join of the retained report, the module's analyzer, the lease fence's payload vocabulary and every outcome a pass can end in. Its refusals already live in DedupRecheckRefusals; splitting the remaining three steps would put the fence check in a different type from the scan it guards, which is exactly the property this type exists to keep in one place.")]
internal sealed class DedupRecheckRunner(
    DedupAnalyzer analyzer,
    DedupReportRegistry reports,
    IDedupSourceAvailability availability,
    DedupExecutionOptions options,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Decides whether this attempt can scan at all, without touching the filesystem. Every refusal is
    /// filed here as the final outcome, because no scan will follow to discover it.
    /// </summary>
    public async ValueTask<DedupRecheckPreparation> PrepareAsync(
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
            reports.RecordRecheck(recheckTaskId, DedupRecheckRefusals.Missing(target));
            return DedupRecheckPreparation.Refused;
        }

        if (!string.Equals(report.PlanDigest, target.PlanDigest, StringComparison.Ordinal))
        {
            // The retained version is no longer the one the caller asked about. Answering about it would
            // present newer evidence as the result of an older question.
            reports.RecordRecheck(recheckTaskId, DedupRecheckRefusals.Superseded(target, key));
            return DedupRecheckPreparation.Refused;
        }

        var sources = DedupReportRehydrator.SourcesOf(report);
        if (sources.Count == 0)
        {
            reports.RecordRecheck(recheckTaskId, DedupRecheckRefusals.Rejected(target, key));
            return DedupRecheckPreparation.Refused;
        }

        // A source whose share went away would make every byte of this run unreadable. Spending a whole
        // pass to discover that would hold the attempt for the length of a walk over a share that is not
        // there, so it is refused now and the durable task ends with a code a caller can act on.
        foreach (var candidate in sources)
        {
            var state = await availability.CheckAsync(candidate.Root, cancellationToken).ConfigureAwait(false);
            if (state != StorageAvailability.Online)
            {
                reports.RecordRecheck(recheckTaskId, DedupRecheckRefusals.Unreachable(target, key));
                return DedupRecheckPreparation.Refused;
            }
        }

        // The request is taken from the retained report now, so the scan has no reason to look at the
        // registry again and cannot be handed a newer version halfway through.
        return new DedupRecheckPreparation(
            target,
            key,
            report.PlanDigest,
            DedupReportRehydrator.Recount(report, options.RecheckTimeout));
    }

    /// <summary>Performs the scan. This is the part that must never run while a fence is held.</summary>
    public Task<DedupRecountResult> ScanAsync(
        DedupRecheckPreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        return analyzer.RecountAsync(preparation.Request, cancellationToken);
    }

    /// <summary>
    /// Files the verdict of a completed scan. It hands the verdict to one compare-and-file step that both
    /// checks the version is still the one a reader would see and writes the outcome, so the outcome that
    /// becomes durable always belongs to the version that was verified and can never displace a newer
    /// analysis that published while the scan ran.
    /// </summary>
    public void File(
        Guid recheckTaskId,
        DedupResolvedSource source,
        DedupRecheckPreparation preparation,
        DedupRecountResult result)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(preparation);
        var target = preparation.Target;
        var key = preparation.Key;
        var evidence = Evidence(result);
        // The plan the recheck produced becomes the next version of the same job, and the old one stays
        // readable: a reader can still see what was verified and what replaced it. Both the "next version"
        // and the "no new version" paths compare and write in the same registry step.
        var outcome = result.CurrentPlan is { } current
            // The next version is composed first and handed to one compare-and-file step, so a library that
            // moved on while the scan ran is refused instead of having its newer report overwritten.
            ? reports.FileIfCurrent(
                source.LibraryId.Value,
                key,
                target.PlanDigest,
                Compose(target.ReportTaskId, key, source, current),
                evidence)
            : reports.StoreEvidenceIfCurrent(source.LibraryId.Value, key, target.PlanDigest, evidence);
        if (!outcome.Filed)
        {
            // The version was dropped, or the library moved on to a newer analysis, while the scan ran. The
            // verdict is no longer an answer about anything a reader can look up, so it is reported as
            // superseded rather than filed.
            reports.RecordRecheck(recheckTaskId, DedupRecheckRefusals.Superseded(target, key));
            return;
        }

        reports.RecordRecheck(recheckTaskId, Completed(target, key, outcome.Key, result));
    }

    /// <summary>
    /// The next version of the rechecked job. The ceilings are a property of the job rather than of this
    /// pass, so they are read back from the version that was verified and restated unchanged. The caller
    /// has already proved that version is readable, so the fallback is the module's own default.
    /// </summary>
    private DedupReport Compose(
        Guid taskId,
        DedupReportKey key,
        DedupResolvedSource source,
        DedupCurationPlan plan)
    {
        var limits = reports.TryGet(key, out var verified) ? verified.Limits : DedupAnalysisLimits.Default;
        return DedupReportComposer.Compose(taskId, limits, source, plan);
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
