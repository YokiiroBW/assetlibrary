using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Contracts;

public sealed record ScanTaskView(Guid TaskId, Guid? ScanId, LibraryId LibraryId, DurableTaskState State,
    bool CancellationRequested, int ObservedEntries, int CommittedEntries, DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt, string? FailureCode, bool CanCancel, bool CanRetry);

public interface IInitialScanCoordinator
{
    ValueTask<ScanTaskView> StartAsync(LibraryId libraryId, ManagementOperation operation, CancellationToken cancellationToken);
    ValueTask<ScanTaskView?> GetAsync(LibraryId libraryId, CancellationToken cancellationToken);
    ValueTask<ScanTaskView> CancelAsync(LibraryId libraryId, Guid taskId, ManagementOperation operation, CancellationToken cancellationToken);
    Task<bool> RunNextAsync(CancellationToken cancellationToken);
    ValueTask<int> RecoverAsync(CancellationToken cancellationToken);
}

public sealed record InitialScanExecutionOptions
{
    public TimeSpan ScanTimeout { get; init; } = TimeSpan.FromHours(24);
    public int BatchSize { get; init; } = 256;
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(10);
    public static TaskTypeName TaskType => new("scan.initial_read_only");
}
