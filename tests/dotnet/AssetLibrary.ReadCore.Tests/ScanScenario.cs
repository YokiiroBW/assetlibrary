using System.Runtime.CompilerServices;
using AssetLibrary.Modules.AssetIdentity.Contracts;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.ScanReconciliation.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.ReadCore.Tests;

internal sealed class ScanScenario
{
    private readonly TestDiscovery discovery;
    private readonly TestObservationSink sink;
    private readonly TestScanJournal journal;
    private readonly InitialReadOnlyScanService service;
    private readonly LibraryId requestLibraryId;

    private ScanScenario(
        LibraryScanTarget target,
        TestDiscovery discovery,
        bool failStaging,
        bool failInitialization = false,
        bool failFinalization = false,
        LibraryId? requestLibraryId = null)
    {
        this.discovery = discovery;
        this.requestLibraryId = requestLibraryId ?? target.LibraryId;
        sink = new TestObservationSink(failStaging, failInitialization);
        journal = new TestScanJournal(failFinalization);
        service = new InitialReadOnlyScanService(
            new TestTargetQuery(target),
            discovery,
            sink,
            journal,
            TimeProvider.System,
            new InitialScanLogger(NullLogger<InitialReadOnlyScanService>.Instance));
    }

    public int BeginCount => sink.BeginCount;

    public int EnumerationCount => discovery.EnumerationCount;

    public int StartCount => journal.StartCount;

    public int[] BatchSizes => sink.Session?.BatchSizes.ToArray() ?? [];

    public bool Completed => sink.Session?.Completed ?? false;

    public bool Aborted => sink.Session?.Aborted ?? false;

    public ScanRunTerminalState? FinishedState => journal.Finished?.State;

    public string? FailureCode => journal.Finished?.FailureCode;

    public static ScanScenario WithEntries(
        int count,
        string? discoveryFailure = null,
        bool failStaging = false,
        bool failInitialization = false,
        bool failFinalization = false) =>
        new(
            OnlineTarget(),
            new SequenceTestDiscovery(TestDiscoveredEntries.Create(count), discoveryFailure),
            failStaging,
            failInitialization,
            failFinalization);

    public static ScanScenario StreamingEntries(int count) =>
        new(OnlineTarget(), new GeneratedTestDiscovery(count), failStaging: false);

    public static ScanScenario Offline()
    {
        var target = OnlineTarget() with { Availability = StorageAvailability.Offline };
        return new ScanScenario(
            target,
            new SequenceTestDiscovery(TestDiscoveredEntries.Create(1), null),
            failStaging: false);
    }

    public static ScanScenario Blocking() =>
        new(OnlineTarget(), new BlockingTestDiscovery(), failStaging: false);

    public static ScanScenario UnrelatedCancellation() =>
        new(OnlineTarget(), new UnrelatedCancellationTestDiscovery(), failStaging: false);

    public static ScanScenario MismatchedTarget() =>
        new(
            OnlineTarget(),
            new SequenceTestDiscovery(TestDiscoveredEntries.Create(1), null),
            failStaging: false,
            requestLibraryId: LibraryId.New());

    public static ScanScenario DefaultRequestLibraryId() =>
        new(
            OnlineTarget(),
            new SequenceTestDiscovery(TestDiscoveredEntries.Create(1), null),
            failStaging: false,
            requestLibraryId: default(LibraryId));

    public Task<InitialScanResult> ExecuteAsync(
        TimeSpan? timeout = null,
        int batchSize = 256,
        CancellationToken cancellationToken = default) =>
        service.ExecuteAsync(
            new InitialScanRequest(requestLibraryId, timeout ?? TimeSpan.FromSeconds(5), batchSize),
            cancellationToken);

    private static LibraryScanTarget OnlineTarget() =>
        new(
            LibraryId.New(),
            StorageSourceId.New(),
            new CanonicalLibraryRoot("C:/sandbox", RootPathComparison.CaseInsensitive),
            StorageAvailability.Online);

}

internal static class TestDiscoveredEntries
{
    public static DiscoveredEntry[] Create(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new DiscoveredEntry(
                new RelativeAssetPath($"folder/file-{index:D4}.bin"),
                AssetEntryKind.File,
                index,
                DateTimeOffset.UnixEpoch.AddSeconds(index),
                DiscoveryAttributes.None))
            .ToArray();
}

internal sealed class TestTargetQuery(LibraryScanTarget target) : ILibraryScanTargetQuery
{
    public ValueTask<LibraryScanTarget?> FindAsync(
        LibraryId libraryId,
        CancellationToken cancellationToken) => ValueTask.FromResult<LibraryScanTarget?>(target);
}

internal abstract class TestDiscovery : IReadOnlyFileDiscovery
{
    public int EnumerationCount { get; protected set; }

    public abstract IAsyncEnumerable<DiscoveredEntry> DiscoverAsync(
        LibraryScanTarget target,
        CancellationToken cancellationToken);
}

internal sealed class SequenceTestDiscovery(
    IReadOnlyList<DiscoveredEntry> entries,
    string? failureCode) : TestDiscovery
{
    public override async IAsyncEnumerable<DiscoveredEntry> DiscoverAsync(
        LibraryScanTarget target,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnumerationCount++;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return entry;
        }

        if (failureCode is not null)
        {
            throw new FileDiscoveryException(failureCode);
        }
    }
}

internal sealed class BlockingTestDiscovery : TestDiscovery
{
    public override async IAsyncEnumerable<DiscoveredEntry> DiscoverAsync(
        LibraryScanTarget target,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnumerationCount++;
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }
}

internal sealed class UnrelatedCancellationTestDiscovery : TestDiscovery
{
    public override async IAsyncEnumerable<DiscoveredEntry> DiscoverAsync(
        LibraryScanTarget target,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnumerationCount++;
        await Task.FromException(new OperationCanceledException());
        yield break;
    }
}

internal sealed class GeneratedTestDiscovery(int count) : TestDiscovery
{
    public override async IAsyncEnumerable<DiscoveredEntry> DiscoverAsync(
        LibraryScanTarget target,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnumerationCount++;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index % 256 == 0)
            {
                await Task.Yield();
            }

            yield return new DiscoveredEntry(
                new RelativeAssetPath($"stream/item-{index:D6}.bin"),
                AssetEntryKind.File,
                index,
                DateTimeOffset.UnixEpoch,
                DiscoveryAttributes.None);
        }
    }
}

internal sealed class TestObservationSink(bool failStaging, bool failInitialization) : IAssetObservationSink
{
    public int BeginCount { get; private set; }

    public TestObservationSession? Session { get; private set; }

    public ValueTask<IAssetObservationSession> BeginInitialScanAsync(
        Guid scanId,
        LibraryId libraryId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        BeginCount++;
        if (failInitialization)
        {
            throw new InvalidOperationException("injected initialization failure");
        }

        Session = new TestObservationSession(failStaging);
        return ValueTask.FromResult<IAssetObservationSession>(Session);
    }
}

internal sealed class TestObservationSession(bool failStaging) : IAssetObservationSession
{
    private int stagedEntries;

    public List<int> BatchSizes { get; } = [];

    public bool Completed { get; private set; }

    public bool Aborted { get; private set; }

    public ValueTask StageAsync(
        IReadOnlyList<AssetObservation> observations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (failStaging)
        {
            throw new InvalidOperationException("injected staging failure");
        }

        BatchSizes.Add(observations.Count);
        stagedEntries += observations.Count;
        return ValueTask.CompletedTask;
    }

    public ValueTask<int> CompleteAsync(DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Completed = true;
        return ValueTask.FromResult(stagedEntries);
    }

    public ValueTask AbortAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Aborted = true;
        stagedEntries = 0;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class TestScanJournal(bool failFinalization) : IScanRunJournal
{
    public int StartCount { get; private set; }

    public TestFinishRecord? Finished { get; private set; }

    public ValueTask StartAsync(
        Guid scanId,
        LibraryId libraryId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        StartCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask FinishAsync(
        Guid scanId,
        ScanRunTerminalState state,
        int observedEntries,
        int committedEntries,
        string? failureCode,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken)
    {
        if (failFinalization && state == ScanRunTerminalState.Completed)
        {
            throw new InvalidOperationException("injected journal finalization failure");
        }

        Finished = new TestFinishRecord(state, observedEntries, committedEntries, failureCode);
        return ValueTask.CompletedTask;
    }
}

internal sealed record TestFinishRecord(
    ScanRunTerminalState State,
    int ObservedEntries,
    int CommittedEntries,
    string? FailureCode);
