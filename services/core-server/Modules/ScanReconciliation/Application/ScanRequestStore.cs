using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

public sealed record ScanRequestRecord(DurableTaskId TaskId, LibraryId LibraryId, DateTimeOffset CreatedAt,
    bool Dispatched, bool Terminal, bool CancellationRequested);
public sealed record ScanRunRecord(Guid ScanId, int Attempt, int ObservedEntries, int CommittedEntries,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string? FailureCode);

public interface IScanRequestStore
{
    ValueTask<ScanRequestRecord?> FindOperationAsync(LibraryId libraryId, ManagementOperation operation, CancellationToken token);
    ValueTask<ScanRequestRecord> AcceptAsync(LibraryId libraryId, ManagementOperation operation, CancellationToken cancellationToken);
    ValueTask<ScanRequestRecord?> FindAsync(DurableTaskId taskId, CancellationToken token);
    ValueTask<ScanRequestRecord?> LatestAsync(LibraryId libraryId, CancellationToken token);
    ValueTask<IReadOnlyList<ScanRequestRecord>> RecoverBatchAsync(CancellationToken cancellationToken);
    ValueTask MarkDispatchedAsync(DurableTaskId taskId, CancellationToken token);
    ValueTask MarkTerminalAsync(DurableTaskId taskId, CancellationToken token);
    ValueTask<bool> CancelAsync(LibraryId libraryId, DurableTaskId taskId, ManagementOperation operation, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<ScanRunRecord>> RunsAsync(DurableTaskId taskId, CancellationToken cancellationToken);
    ValueTask StartRunAsync(Guid scanId, ScanRequestRecord request, int attempt, DateTimeOffset startedAt, CancellationToken token);
    ValueTask ProgressAsync(Guid scanId, int count, CancellationToken token);
    ValueTask FinishRunAsync(Guid scanId, ScanRunTerminalState state, int observed, int committed,
        string? failureCode, DateTimeOffset finishedAt, CancellationToken cancellationToken);
}
