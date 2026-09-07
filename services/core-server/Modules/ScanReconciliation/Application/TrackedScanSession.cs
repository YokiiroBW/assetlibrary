using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;

namespace AssetLibrary.Modules.ScanReconciliation.Application;

internal sealed class TrackedScanJournal(IScanRequestStore store, ScanRequestRecord request, int attempt) : IScanRunJournal
{
    public Guid? ScanId { get; private set; }

    public async ValueTask StartAsync(Guid scanId, LibraryId libraryId, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        await store.StartRunAsync(scanId, request, attempt, startedAt, cancellationToken).ConfigureAwait(false);
        ScanId = scanId;
    }

    public ValueTask FinishAsync(Guid scanId, ScanRunTerminalState state, int observedEntries, int committedEntries,
        string? failureCode, DateTimeOffset finishedAt, CancellationToken cancellationToken) =>
        store.FinishRunAsync(scanId, state, observedEntries, committedEntries, failureCode, finishedAt, cancellationToken);
}

internal sealed class TrackedScanSink(IAssetObservationSink sink, IScanRequestStore store, ScanLeaseExecution execution) : IAssetObservationSink
{
    public async ValueTask<IAssetObservationSession> BeginInitialScanAsync(Guid scanId, LibraryId libraryId,
        DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var session = await sink.BeginInitialScanAsync(scanId, libraryId, startedAt, cancellationToken).ConfigureAwait(false);
        return new TrackedScanSession(session, store, execution, scanId);
    }
}

internal sealed class TrackedScanSession(IAssetObservationSession session, IScanRequestStore store,
    ScanLeaseExecution execution, Guid scanId) : IAssetObservationSession
{
    private int staged;

    public async ValueTask StageAsync(IReadOnlyList<AssetObservation> observations, CancellationToken cancellationToken)
    {
        await execution.ConfirmAsync(cancellationToken).ConfigureAwait(false);
        await session.StageAsync(observations, cancellationToken).ConfigureAwait(false);
        staged = checked(staged + observations.Count);
        await store.ProgressAsync(scanId, staged, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<int> CompleteAsync(DateTimeOffset observedAt, CancellationToken cancellationToken) =>
        execution.CommitAsync(token => session.CompleteAsync(observedAt, token), cancellationToken);

    public ValueTask AbortAsync(CancellationToken cancellationToken) => session.AbortAsync(cancellationToken);
    public ValueTask DisposeAsync() => session.DisposeAsync();
}
