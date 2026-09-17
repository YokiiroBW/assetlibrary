using System.Text.Json;
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
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "A worker for one task type runs two kinds of work under the same lease, fence and cancellation discovery. The count is the union of the two payload shapes, the lease lifecycle they share and the finish vocabulary TaskHealth accepts; splitting it would duplicate the fence rather than reduce coupling.")]
public sealed class DedupJobWorker(
    DedupJobService service,
    DedupReportRegistry reports,
    IDurableTaskCoordinator tasks,
    IDurableTaskCommitGuard guard,
    DedupExecutionOptions options,
    TimeProvider timeProvider)
{
    private readonly DedupRecheckRunner rechecks = service.Rechecks;

    /// <summary>
    /// Runs one claimed dedup task. The module's own payload decides which work this attempt is: an
    /// analysis or a recheck of one report version. Both kinds come from the durable record, so a
    /// reclaimed task re-runs what was requested rather than whatever the configuration says now.
    /// </summary>
    public async Task RunAsync(DurableTaskLease lease, DedupResolvedSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var payload = DedupJobPayloadReader.Read(lease.Payload);
        if (payload.Kind == DedupJobPayloadReader.PayloadKind.Recheck)
        {
            await RunRecheckAsync(
                lease,
                source,
                payload.Recheck ?? throw new ReadOnlyTrialException("dedup_payload_invalid"),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await RunAnalysisAsync(lease, source, payload.Limits, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAnalysisAsync(
        DurableTaskLease lease,
        DedupResolvedSource source,
        DedupAnalysisLimits limits,
        CancellationToken cancellationToken)
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

    /// <summary>
    /// Runs one claimed recheck under its own lease. A recheck enumerates and hashes like an analysis,
    /// so it needs the same heartbeat, the same fence and the same cancellation discovery; what differs
    /// is the work, which is the module's recheck runner rather than the analyzer.
    /// </summary>
    private async Task RunRecheckAsync(
        DurableTaskLease lease,
        DedupResolvedSource source,
        DedupJobPayloadReader.RecheckTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var scan = CancellationTokenSource.CreateLinkedTokenSource(work.Token);
        scan.CancelAfter(options.RecheckTimeout);
        var heartbeat = new DedupLeaseHeartbeat(lease, tasks, guard, options, work);
        var monitor = heartbeat.MonitorAsync(timeProvider);
        try
        {
            if (!await heartbeat.ConfirmAsync(work.Token).ConfigureAwait(false))
            {
                throw new LeaseLostException();
            }

            // Whether this attempt has anything to scan is decided before any byte is read, and a refusal
            // is already filed: there is nothing to fence afterwards, so the attempt is simply done.
            var preparation = await rechecks
                .PrepareAsync(lease.TaskId.Value, source, target, scan.Token)
                .ConfigureAwait(false);
            if (!preparation.CanScan)
            {
                await FinishAsync(lease, DurableTaskFinishKind.Succeeded, null, cancellationToken).ConfigureAwait(false);
                return;
            }

            // The scan runs with no fence held. A recheck walks and hashes a whole directory, and doing
            // that inside the fenced commit would hold the durable task's row for as long as the share
            // takes to answer, so a slow NAS would block every other attempt on the same task.
            var result = await rechecks.ScanAsync(preparation, scan.Token).ConfigureAwait(false);

            // Only the verdict is filed inside the fence, and it re-reads the exact version the payload
            // named, so a lease that expired mid-scan cannot leave an answer behind for a later attempt to
            // adopt and a version that moved on is never answered with stale evidence.
            _ = await heartbeat.CommitAsync(
                _ =>
                {
                    rechecks.File(lease.TaskId.Value, source, preparation, result);
                    return ValueTask.FromResult(0);
                },
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
            || libraryId == Guid.Empty)
        {
            throw new ReadOnlyTrialException("dedup_payload_invalid");
        }

        // An older payload without a kind is an analysis: the field was added when the workbench
        // learned to run a recheck, and a task enqueued before that still means what it meant then.
        var kind = root.TryGetProperty("kind", out var kindNode) ? kindNode.GetString() : "analysis";
        return kind switch
        {
            "analysis" => Analysis(root, libraryId),
            "recheck" => Recheck(root, libraryId),
            _ => throw new ReadOnlyTrialException("dedup_payload_invalid"),
        };
    }

    private static Payload Analysis(JsonElement root, Guid libraryId)
    {
        if (!Guid.TryParse(root.GetProperty("analysis_id").GetString(), out var analysisId)
            || analysisId == Guid.Empty)
        {
            throw new ReadOnlyTrialException("dedup_payload_invalid");
        }

        return new Payload(
            PayloadKind.Analysis,
            new DedupAnalysisId(analysisId),
            new LibraryId(libraryId),
            new DedupAnalysisLimits(
                root.GetProperty("maximum_files").GetInt32(),
                root.GetProperty("maximum_bytes").GetInt64(),
                root.GetProperty("maximum_file_bytes").GetInt32(),
                root.GetProperty("hash_concurrency").GetInt32()),
            null);
    }

    private static Payload Recheck(JsonElement root, Guid libraryId)
    {
        if (!Guid.TryParse(root.GetProperty("report_task_id").GetString(), out var reportTaskId)
            || reportTaskId == Guid.Empty
            || !root.TryGetProperty("generation", out var generation)
            || generation.GetInt64() <= 0
            || root.GetProperty("plan_digest") is not { ValueKind: System.Text.Json.JsonValueKind.String } digest
            || string.IsNullOrEmpty(digest.GetString()))
        {
            throw new ReadOnlyTrialException("dedup_payload_invalid");
        }

        return new Payload(
            PayloadKind.Recheck,
            null,
            new LibraryId(libraryId),
            DedupAnalysisLimits.Default,
            new RecheckTarget(reportTaskId, generation.GetInt64(), digest.GetString()!));
    }

    public enum PayloadKind
    {
        Analysis = 0,
        Recheck = 1,
    }

    public sealed record RecheckTarget(Guid ReportTaskId, long Generation, string PlanDigest);

    public sealed record Payload(
        PayloadKind Kind,
        DedupAnalysisId? AnalysisId,
        LibraryId LibraryId,
        DedupAnalysisLimits Limits,
        RecheckTarget? Recheck);
}
