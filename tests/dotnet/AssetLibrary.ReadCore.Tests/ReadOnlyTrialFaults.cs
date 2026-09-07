using AssetLibrary.Modules.AssetIdentity.Contracts;
using System.Runtime.CompilerServices;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Domain;
using AssetLibrary.Modules.ScanReconciliation.Infrastructure;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

internal sealed class PausingDiscovery : IReadOnlyFileDiscovery
{
    public TaskCompletionSource Staged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async IAsyncEnumerable<DiscoveredEntry> DiscoverAsync(LibraryScanTarget target,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in new ProcessReadOnlyFileDiscovery(ReadOnlyTrialFixture.WorkerOptions())
            .DiscoverAsync(target, cancellationToken))
        {
            yield return entry;
            Staged.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}

internal sealed class FailCompletedJournal(IScanRequestStore inner) : IScanRequestStore
{
    public bool Fired { get; private set; }
    public ValueTask<ScanRequestRecord?> FindOperationAsync(LibraryId libraryId, ManagementOperation operation, CancellationToken token) => inner.FindOperationAsync(libraryId, operation, token);
    public ValueTask<ScanRequestRecord> AcceptAsync(LibraryId libraryId, ManagementOperation operation, CancellationToken cancellationToken) => inner.AcceptAsync(libraryId, operation, cancellationToken);
    public ValueTask<ScanRequestRecord?> FindAsync(DurableTaskId taskId, CancellationToken token) => inner.FindAsync(taskId, token);
    public ValueTask<ScanRequestRecord?> LatestAsync(LibraryId libraryId, CancellationToken token) => inner.LatestAsync(libraryId, token);
    public ValueTask<IReadOnlyList<ScanRequestRecord>> RecoverBatchAsync(CancellationToken cancellationToken) => inner.RecoverBatchAsync(cancellationToken);
    public ValueTask MarkDispatchedAsync(DurableTaskId taskId, CancellationToken token) => inner.MarkDispatchedAsync(taskId, token);
    public ValueTask MarkTerminalAsync(DurableTaskId taskId, CancellationToken token) => inner.MarkTerminalAsync(taskId, token);
    public ValueTask<bool> CancelAsync(LibraryId libraryId, DurableTaskId taskId, ManagementOperation operation, CancellationToken cancellationToken) => inner.CancelAsync(libraryId, taskId, operation, cancellationToken);
    public ValueTask<IReadOnlyList<ScanRunRecord>> RunsAsync(DurableTaskId taskId, CancellationToken cancellationToken) => inner.RunsAsync(taskId, cancellationToken);
    public ValueTask StartRunAsync(Guid scanId, ScanRequestRecord request, int attempt, DateTimeOffset startedAt, CancellationToken token) => inner.StartRunAsync(scanId, request, attempt, startedAt, token);
    public ValueTask ProgressAsync(Guid scanId, int count, CancellationToken token) => inner.ProgressAsync(scanId, count, token);

    public ValueTask FinishRunAsync(Guid scanId, ScanRunTerminalState state, int observed, int committed,
        string? failureCode, DateTimeOffset finishedAt, CancellationToken cancellationToken)
    {
        if (state == ScanRunTerminalState.Completed && !Fired)
        {
            Fired = true;
            throw new IOException("Injected lost journal acknowledgement.");
        }

        return inner.FinishRunAsync(scanId, state, observed, committed, failureCode, finishedAt, cancellationToken);
    }
}

internal static class ReadOnlyTrialObservations
{
    public static AssetObservation File(string path) =>
        new(StableEntryId.New(), new RelativeAssetPath(path), AssetEntryKind.File, 17, DateTimeOffset.UtcNow);
}
