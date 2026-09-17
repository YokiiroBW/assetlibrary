using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// Accepts a request to analyze one already-authorized library as a durable background task. It owns
/// the acceptance rules: one job per library and operation key, one active attempt at a time, and a
/// payload that carries ceilings rather than paths.
/// </summary>
internal sealed class DedupJobStarter(
    DedupReportRegistry reports,
    IDurableTaskCoordinator tasks,
    IDurableTaskInspector inspector,
    DedupJobViewFactory views,
    TimeProvider timeProvider)
{
    public async ValueTask<DedupJobView> StartAsync(
        DedupResolvedSource source,
        DedupAnalysisLimits limits,
        bool retry,
        DedupOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        operation.Validate();
        var validated = limits.Validate();
        var taskId = DedupJobIdentity.TaskId(source.LibraryId, operation);
        var known = await DedupJobLocator.FindAsync(inspector, taskId, cancellationToken).ConfigureAwait(false);
        if (known is not null && !retry)
        {
            return views.Of(source, taskId, known);
        }

        if (known is { } existing && DedupJobViewFactory.IsActive(existing.Snapshot.State))
        {
            // Re-running a job that is still leased would read the same directories twice at once.
            throw new ReadOnlyTrialException("dedup_already_running");
        }

        // The idempotency key is bound to this operation key and library only: a retried start
        // replays the same durable task, while a fresh analysis cannot adopt someone else's.
        var enqueued = await tasks.EnqueueAsync(
            new DurableTaskEnqueueRequest(
                new DurableTaskId(taskId),
                new TaskIdempotencyKey($"dedup:{source.LibraryId.Value:D}:{operation.IdempotencyKey:D}"),
                DedupExecutionOptions.TaskType,
                DedupJobPayload.Create(source.LibraryId, DedupAnalysisId.New(), validated),
                TaskPriority.P3,
                2,
                timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        if (enqueued.Status == DurableTaskEnqueueStatus.Existing && enqueued.TaskId.Value != taskId)
        {
            throw new ReadOnlyTrialException("idempotency_conflict");
        }

        // A new attempt starts from a clean review surface: an earlier report of this job is no longer
        // the current evidence for the version that is about to be produced. The ceilings are recorded
        // because they are request parameters, not findings the analysis can report back.
        reports.DiscardTask(taskId);
        reports.RecordLimits(taskId, validated);
        var details = await DedupJobLocator.FindAsync(inspector, taskId, cancellationToken).ConfigureAwait(false)
            ?? throw new ReadOnlyTrialException("dedup_task_missing");
        return views.Of(source, taskId, details);
    }
}

/// <summary>
/// Reads one durable task's status under a short deadline. A status read is a page request, so a slow
/// database is reported as unavailable rather than as a job that does not exist.
/// </summary>
internal static class DedupJobLocator
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(4);

    public static async ValueTask<DurableTaskDetails?> FindAsync(
        IDurableTaskInspector inspector,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(StatusTimeout);
        try
        {
            // The inspector port is read-only by contract, so a status page can never mutate a job.
            return await inspector.FindAsync(new DurableTaskId(taskId), deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReadOnlyTrialException("service_unavailable");
        }
    }
}
