using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.AssetIdentity.Dedup.Application;

/// <summary>
/// The in-process wake-up signal for dedup work. It carries no business fact: the durable task is the
/// single source of truth, and a signal only tells the worker that something was enqueued. A dropped
/// signal costs one polling interval, never a job.
/// </summary>
public interface IDedupJobSignal
{
    ValueTask SignalAsync(CancellationToken cancellationToken);

    ValueTask<bool> WaitAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Executes one claimed dedup task. It decides nothing about what a duplicate is: it re-reads the
/// immutable payload, runs the module's analyzer under a bounded timeout and heartbeat, and files the
/// report only while the lease it holds is still the current one.
/// </summary>
public sealed class DedupJobWorker(
    DedupJobService service,
    DedupReportRegistry reports,
    IDurableTaskCoordinator tasks,
    IDurableTaskCommitGuard guard,
    DedupExecutionOptions options,
    TimeProvider timeProvider)
{
    public async Task RunAsync(DurableTaskLease lease, DedupResolvedSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(source);
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var analysis = CancellationTokenSource.CreateLinkedTokenSource(work.Token);
        analysis.CancelAfter(options.AnalysisTimeout);
        var heartbeat = new DedupLeaseHeartbeat(lease, tasks, guard, options, work);
        var monitor = heartbeat.MonitorAsync(timeProvider);
        try
        {
            if (!await heartbeat.ConfirmAsync(work.Token).ConfigureAwait(false))
            {
                throw new LeaseLostException();
            }

            // The ceilings come from the task's own payload, so a reclaimed task re-runs the analysis
            // that was requested rather than whatever ceiling happens to be configured now.
            var limits = DedupJobPayloadReader.Read(lease.Payload).Limits;
            reports.RecordLimits(lease.TaskId.Value, limits);
            var plan = await service.AnalyzeAsync(source, limits, analysis.Token).ConfigureAwait(false);

            // The report is filed inside the fenced commit, so a lease that expired while the analysis
            // ran can never leave a reviewable result behind for a later attempt to adopt.
            _ = await heartbeat.CommitAsync(
                _ => PublishAsync(lease.TaskId.Value, source, limits, plan),
                cancellationToken).ConfigureAwait(false);
            await FinishAsync(lease, DurableTaskFinishKind.Succeeded, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var kind = Classify(error, heartbeat);
            await FinishAsync(lease, kind, Code(kind, error), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await work.CancelAsync().ConfigureAwait(false);
            await monitor.ConfigureAwait(false);
        }
    }

    private async ValueTask<int> PublishAsync(
        Guid taskId,
        DedupResolvedSource source,
        DedupAnalysisLimits limits,
        DedupCurationPlan plan)
    {
        try
        {
            service.Publish(taskId, limits, source, plan);
            return plan.Items.Count;
        }
        catch
        {
            // A commit that threw did not durably record anything for this attempt, so a report the
            // callback managed to file must not survive as if the attempt had succeeded.
            service.Discard(taskId);
            throw;
        }
    }

    private async ValueTask FinishAsync(
        DurableTaskLease lease,
        DurableTaskFinishKind kind,
        string? code,
        CancellationToken cancellationToken)
    {
        _ = await tasks.FinishAsync(
            new DurableTaskFinishRequest(
                lease.TaskId,
                lease.Identity,
                kind,
                code is null ? null : new FailureCode(code),
                kind == DurableTaskFinishKind.RetryableFailure ? TimeSpan.FromSeconds(15) : null),
            cancellationToken).ConfigureAwait(false);
    }

    private static DurableTaskFinishKind Classify(Exception error, DedupLeaseHeartbeat heartbeat) => error switch
    {
        LeaseLostException when heartbeat.CancellationRequested => DurableTaskFinishKind.Cancelled,
        LeaseLostException => DurableTaskFinishKind.RetryableFailure,
        OperationCanceledException when heartbeat.CancellationRequested => DurableTaskFinishKind.Cancelled,
        OperationCanceledException => DurableTaskFinishKind.RetryableFailure,
        ReadOnlyTrialException trial when trial.Code is "storage_unavailable" or "library_not_found"
            => DurableTaskFinishKind.RetryableFailure,
        _ => DurableTaskFinishKind.PermanentFailure,
    };

    private static string Code(DurableTaskFinishKind kind, Exception error) => kind switch
    {
        DurableTaskFinishKind.Cancelled => "dedup_cancelled",
        DurableTaskFinishKind.RetryableFailure when error is LeaseLostException => "dedup_lease_lost",
        DurableTaskFinishKind.RetryableFailure when error is OperationCanceledException => "dedup_timeout",
        DurableTaskFinishKind.RetryableFailure => "dedup_source_unavailable",
        _ => "dedup_execution_failed",
    };
}

/// <summary>
/// Signals that the claim this attempt was made under is no longer current. The attempt must stop and
/// must not publish: its work belongs to a lease the durable task no longer honours.
/// </summary>
internal sealed class LeaseLostException() : InvalidOperationException("The dedup task lease is no longer current.");

/// <summary>
/// Keeps one claim alive while a long analysis runs, and discovers cancellation through the same
/// heartbeat that renews the lease. A cancelled job therefore stops reading instead of finishing
/// quietly and presenting an abandoned result.
/// </summary>
internal sealed class DedupLeaseHeartbeat(
    DurableTaskLease lease,
    IDurableTaskCoordinator tasks,
    IDurableTaskCommitGuard guard,
    DedupExecutionOptions options,
    CancellationTokenSource work)
{
    public bool CancellationRequested { get; private set; }

    public async ValueTask<bool> ConfirmAsync(CancellationToken cancellationToken)
    {
        var result = await tasks.HeartbeatAsync(
            new DurableTaskHeartbeatRequest(lease.TaskId, lease.Identity, options.LeaseDuration),
            cancellationToken).ConfigureAwait(false);
        CancellationRequested = result.CancellationRequested;
        var lost = result.Status != TaskLeaseMutationStatus.Accepted || CancellationRequested;
        if (lost)
        {
            await work.CancelAsync().ConfigureAwait(false);
        }

        return !lost;
    }

    public async Task MonitorAsync(TimeProvider timeProvider)
    {
        try
        {
            while (!work.IsCancellationRequested)
            {
                await Task.Delay(options.HeartbeatInterval, timeProvider, work.Token).ConfigureAwait(false);
                _ = await ConfirmAsync(work.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            // Shutdown, cancellation and a lost lease all end the monitor together with its analysis.
        }
        catch (Exception)
        {
            // A heartbeat that cannot reach the store is treated as a lost fence: publishing under a
            // claim we can no longer prove would be worse than retrying the attempt.
            await work.CancelAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<int> CommitAsync(Func<CancellationToken, ValueTask<int>> commit, CancellationToken cancellationToken)
    {
        try
        {
            return await guard.CommitAsync(
                new DurableTaskHeartbeatRequest(lease.TaskId, lease.Identity, options.LeaseDuration),
                commit,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCommitLeaseException)
        {
            throw new LeaseLostException();
        }
    }
}

/// <summary>
/// Reads the immutable payload of one claimed dedup task. It carries the authorized library id and the
/// ceilings — never a physical path, so a reclaimed task reads the directory the library query resolves
/// rather than a value stored in a queue. It is public because the Host reads the same payload before
/// it can hand the task to the module: resolving which library a claimed task names is composition.
/// </summary>
public static class DedupJobPayloadReader
{
    public static Payload Read(JsonObjectPayload payload)
    {
        using var document = System.Text.Json.JsonDocument.Parse(payload.Value);
        var root = document.RootElement;
        if (!Guid.TryParse(root.GetProperty("library_id").GetString(), out var libraryId)
            || libraryId == Guid.Empty
            || !Guid.TryParse(root.GetProperty("analysis_id").GetString(), out var analysisId)
            || analysisId == Guid.Empty)
        {
            throw new ReadOnlyTrialException("dedup_payload_invalid");
        }

        return new Payload(
            new DedupAnalysisId(analysisId),
            new LibraryId(libraryId),
            new DedupAnalysisLimits(
                root.GetProperty("maximum_files").GetInt32(),
                root.GetProperty("maximum_bytes").GetInt64(),
                root.GetProperty("maximum_file_bytes").GetInt32(),
                root.GetProperty("hash_concurrency").GetInt32()));
    }

    public sealed record Payload(
        DedupAnalysisId AnalysisId,
        LibraryId LibraryId,
        DedupAnalysisLimits Limits);
}
