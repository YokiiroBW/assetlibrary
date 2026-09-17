using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// The dedup workbench's business entry point. It owns the flow — what may be analyzed, when a report
/// is replaced, what a page may claim and what an export states — while TaskHealth keeps owning the
/// durable task, its lease fence and its cancellation record, and the library module keeps owning which
/// roots exist. Every call is made against a library the composition root has already authorized.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "This is the module's single port aggregate for one workbench: its type count is the shape of the six authorized operations and their result records, not a dependency on another module. Each rule it exposes lives in its own component (starter, recheck runner, reader, exporter, publisher) and TaskHealth is reached only through its public contracts.")]
public sealed class DedupJobService : IDedupJobCoordinator
{
    private readonly DedupAnalyzer analyzer;
    private readonly IDurableTaskCoordinator tasks;
    private readonly IDurableTaskInspector inspector;
    private readonly DedupExecutionOptions options;
    private readonly DedupJobViewFactory views;
    private readonly DedupResultsReader reader;
    private readonly DedupExportBuilder exporter;
    private readonly DedupJobStarter starter;
    private readonly DedupRecheckScheduler rechecks;
    private readonly DedupReportPublisher publisher;
    private readonly DedupReportRegistry retained;

    /// <summary>
    /// The recheck executor, shared with the worker so both entry points drive one implementation. A
    /// second executor over the same registry would be a second place the comparison rules could drift.
    /// </summary>
    internal DedupRecheckRunner Rechecks { get; }

    public DedupJobService(
        DedupAnalyzer analyzer,
        DedupReportRegistry reports,
        IDurableTaskCoordinator tasks,
        IDurableTaskInspector inspector,
        IDedupSourceAvailability availability,
        DedupExecutionOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(availability);
        this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        retained = reports;
        ArgumentNullException.ThrowIfNull(timeProvider);
        views = new DedupJobViewFactory(reports, timeProvider);
        reader = new DedupResultsReader(reports, views);
        exporter = new DedupExportBuilder(reports, timeProvider);
        publisher = new DedupReportPublisher(reports);
        starter = new DedupJobStarter(reports, tasks, inspector, views, timeProvider);
        rechecks = new DedupRecheckScheduler(tasks, views, timeProvider);
        Rechecks = new DedupRecheckRunner(analyzer, reports, availability, options, timeProvider);
    }

    public ValueTask<DedupJobView> StartAsync(
        DedupResolvedSource source,
        DedupAnalysisLimits limits,
        bool retry,
        DedupOperation operation,
        CancellationToken cancellationToken) =>
        starter.StartAsync(source, limits, retry, operation, cancellationToken);

    public async ValueTask<DedupJobView> GetAsync(
        DedupResolvedSource source,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var details = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        return views.Of(source, taskId, details);
    }

    public async ValueTask<DedupResultsPage> ResultsAsync(
        DedupResolvedSource source,
        Guid taskId,
        DedupPageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        _ = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        return views.TryCurrent(source, taskId, out var key, out var report)
            ? reader.Read(request, taskId, key, report)
            : reader.NotRetained(request, taskId);
    }

    public async ValueTask<DedupJobView> CancelAsync(
        DedupResolvedSource source,
        DedupOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        operation.Validate();
        var taskId = DedupJobIdentity.TaskId(source.LibraryId, operation);
        var details = await DedupJobLocator.FindAsync(inspector, taskId, cancellationToken).ConfigureAwait(false)
            ?? throw new ReadOnlyTrialException("dedup_not_found");
        if (DedupJobViewFactory.IsActive(details.Snapshot.State))
        {
            // Cancellation is requested through the durable task, so a crashed worker still learns of
            // it. It stops the analysis only: this workbench never touches a file.
            var result = await tasks.RequestCancellationAsync(new DurableTaskId(taskId), cancellationToken)
                .ConfigureAwait(false);
            if (result.Status == TaskCancellationStatus.NotFound)
            {
                throw new ReadOnlyTrialException("dedup_not_found");
            }
        }

        var updated = await DedupJobLocator.FindAsync(inspector, taskId, cancellationToken).ConfigureAwait(false)
            ?? details;
        return views.Of(source, taskId, updated);
    }

    /// <summary>
    /// Files a recheck as a durable task and answers at once. The recheck itself runs on the background
    /// path, so this never enumerates or hashes inside a request.
    /// </summary>
    public async ValueTask<DedupRecheckAccepted> RevalidateAsync(
        DedupResolvedSource source,
        Guid taskId,
        string? expectedDigest,
        CancellationToken cancellationToken)
    {
        _ = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        return await rechecks.AcceptAsync(source, taskId, expectedDigest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The state of one recheck as its own durable task records it. A caller that asked for a recheck
    /// polls this instead of holding a request open, and the answer states whether the run has finished.
    /// </summary>
    public ValueTask<DedupRecheckState> RecheckStateAsync(
        DedupResolvedSource source,
        Guid reportTaskId,
        Guid recheckTaskId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var run = retained.RecheckOf(recheckTaskId);
        if (run is null)
        {
            return ValueTask.FromResult(new DedupRecheckState(
                RecheckRunState.Pending, null, reportTaskId, recheckTaskId, source.DisplayName));
        }

        return ValueTask.FromResult(new DedupRecheckState(
            run.Completed ? RecheckRunState.Completed : RecheckRunState.Refused,
            run,
            reportTaskId,
            recheckTaskId,
            source.DisplayName));
    }

    public async ValueTask<DedupExportDocument> ExportAsync(
        DedupResolvedSource source,
        Guid taskId,
        string? expectedVersion,
        string? expectedDigest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        _ = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (!views.TryCurrent(source, taskId, out var key, out var report))
        {
            throw new ReadOnlyTrialException("dedup_report_not_retained");
        }

        // An export must name the version it exports. A caller naming a different version is asking for
        // evidence this server no longer holds, so it is refused instead of quietly re-bound.
        if (expectedVersion is { Length: > 0 } && !string.Equals(expectedVersion, key.VersionText, StringComparison.Ordinal))
        {
            throw new ReadOnlyTrialException("dedup_version_conflict");
        }

        if (expectedDigest is { Length: > 0 } && !string.Equals(expectedDigest, report.PlanDigest, StringComparison.Ordinal))
        {
            throw new ReadOnlyTrialException("dedup_version_conflict");
        }

        return exporter.Build(taskId, key, report);
    }

    /// <summary>
    /// Runs the module's own analyzer for one authorized library. The display name is the resolved
    /// library identifier, never a caller-supplied string: a report must not carry a name the
    /// composition root did not verify.
    /// </summary>
    public async ValueTask<DedupCurationPlan> AnalyzeAsync(
        DedupResolvedSource source,
        DedupAnalysisLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var request = new DedupAnalysisRequest(
            DedupAnalysisId.New(),
            [new DedupSourceRequest(
                source.SourceId,
                source.DisplayName,
                source.Role,
                source.LibraryId,
                source.Root)],
            limits.Validate(),
            options.AnalysisTimeout);
        return await analyzer.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Files a finished plan under the lease the worker still holds. Not a success decision.</summary>
    public void Publish(Guid taskId, DedupAnalysisLimits limits, DedupResolvedSource source, DedupCurationPlan plan) =>
        publisher.Publish(taskId, limits, source, plan);

    /// <summary>Drops the retained report of a job whose attempt was abandoned or superseded.</summary>
    public void Discard(Guid taskId) => publisher.Discard(taskId);

    private async ValueTask<DurableTaskDetails> RequireAsync(Guid taskId, CancellationToken cancellationToken) =>
        await DedupJobLocator.FindAsync(inspector, Require(taskId), cancellationToken).ConfigureAwait(false)
        ?? throw new ReadOnlyTrialException("dedup_not_found");

    private static Guid Require(Guid taskId) =>
        taskId == Guid.Empty ? throw new ReadOnlyTrialException("invalid_request") : taskId;
}
