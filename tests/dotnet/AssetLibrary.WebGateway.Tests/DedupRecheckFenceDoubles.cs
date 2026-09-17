using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Application;
using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
using AssetLibrary.Modules.AssetIdentity.Dedup.Jobs;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.WebGateway.Tests;

/// <summary>
/// The lease a test hands the worker, built the way the host builds one for a claimed recheck. It is a
/// factory rather than inline construction so the lease vocabulary stays out of the fixture that measures
/// the fence, which is about when the commit is open and not about how a lease is shaped.
/// </summary>
internal static class DedupRecheckLeaseFactory
{
    public static DurableTaskLease ForRecheck(DedupJobPayloadReader.RecheckTarget target, LibraryId libraryId) =>
        new(
            new DurableTaskId(Guid.NewGuid()),
            DedupExecutionOptions.TaskType,
            DedupJobPayload.CreateRecheck(libraryId, target.ReportTaskId, target.Generation, target.PlanDigest),
            TaskPriority.P3,
            Attempt: 1,
            MaxAttempts: 2,
            new TaskLeaseIdentity(new LeaseOwner("fixture"), new LeaseToken(Guid.NewGuid()), 1),
            DateTimeOffset.UtcNow.AddMinutes(5),
            CancellationRequested: false);
}

/// <summary>
/// The probes one recheck is observed through: a walk that records whether a file was read while the
/// durable commit was open, and a guard that records when that commit was open at all. Both are inert
/// except for what they record, so a test can state the fence property without a database.
/// </summary>
internal sealed class FenceProbeGuard : IDurableTaskCommitGuard
{
    private readonly Lock gate = new();

    /// <summary>How many times a verdict was filed under a fence.</summary>
    public int Commits { get; private set; }

    /// <summary>The most fences that were ever open at once on one attempt.</summary>
    public int MaximumOpen { get; private set; }

    /// <summary>How many fences are open right now.</summary>
    public int Open { get; private set; }

    public async ValueTask<int> CommitAsync(
        DurableTaskHeartbeatRequest request,
        Func<CancellationToken, ValueTask<int>> commit,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Commits++;
            Open++;
            MaximumOpen = Math.Max(MaximumOpen, Open);
        }

        try
        {
            return await commit(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                Open--;
            }
        }
    }
}

/// <summary>
/// A source scope that accepts exactly the roots a test names, and an availability that always reports
/// them online. It replaces the composition root, which is the one thing a synthetic library has no
/// installation for.
/// </summary>
internal sealed class FixedScopeQuery(CanonicalLibraryRoot root) : IDedupSourceScopeQuery
{
    public ValueTask<IReadOnlyList<CanonicalLibraryRoot>> RegisteredRootsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<CanonicalLibraryRoot>>([root]);

    public ValueTask<IReadOnlyList<CanonicalLibraryRoot>> ManagedOutputRootsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<CanonicalLibraryRoot>>([]);
}

/// <summary>Reports every accepted root as online, so a synthetic run is never refused as unavailable.</summary>
internal sealed class AlwaysOnlineAvailability : IDedupSourceAvailability
{
    public ValueTask<StorageAvailability> CheckAsync(CanonicalLibraryRoot root, CancellationToken cancellationToken) =>
        ValueTask.FromResult(StorageAvailability.Online);
}

/// <summary>
/// A durable-task coordinator that answers lease lifecycle calls successfully and enqueues nothing. The
/// recheck under test is handed its lease directly, so nothing here may start or claim work of its own.
/// </summary>
internal sealed class InertTaskCoordinator : IDurableTaskCoordinator
{
    public ValueTask<DurableTaskEnqueueResult> EnqueueAsync(
        DurableTaskEnqueueRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new DurableTaskEnqueueResult(request.TaskId, DurableTaskEnqueueStatus.Created));

    public ValueTask<IReadOnlyList<DurableTaskLease>> ClaimAsync(
        DurableTaskClaimRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DurableTaskLease>>([]);

    public ValueTask<DurableTaskHeartbeatResult> HeartbeatAsync(
        DurableTaskHeartbeatRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new DurableTaskHeartbeatResult(
            TaskLeaseMutationStatus.Accepted,
            CancellationRequested: false,
            DateTimeOffset.UtcNow.AddMinutes(5)));

    public ValueTask<DurableTaskFinishResult> FinishAsync(
        DurableTaskFinishRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new DurableTaskFinishResult(TaskLeaseMutationStatus.Accepted, null));

    public ValueTask<DurableTaskCancellationResult> RequestCancellationAsync(
        DurableTaskId taskId,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new DurableTaskCancellationResult(TaskCancellationStatus.Requested));

    public ValueTask<int> ReclaimExpiredAsync(int batchSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult(0);
}

/// <summary>
/// A durable-task inspector that reads no task. A recheck claimed by hand needs no lookup, so nothing
/// here may answer as if a task existed.
/// </summary>
internal sealed class InertTaskInspector : IDurableTaskInspector
{
    public ValueTask<DurableTaskDetails?> FindAsync(DurableTaskId taskId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<DurableTaskDetails?>(null);

    /// <summary>
    /// Reports that nothing was committed. A task this fixture never stored has no commit to reconcile, so
    /// claiming one would be an invention.
    /// </summary>
    public ValueTask<bool> ReconcileCommittedAsync(DurableTaskId taskId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}
