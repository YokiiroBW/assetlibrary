using AssetLibrary.Modules.ScanReconciliation.Application;
using AssetLibrary.Modules.ScanReconciliation.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
public sealed class InitialReadOnlyScanServiceTests
{
    private static readonly int[] ExpectedBatchSizes = [2, 2, 1];

    [TestMethod]
    public async Task OnlineScanStagesFixedBatchesAndCommitsOneCompleteSnapshot()
    {
        var scenario = ScanScenario.WithEntries(5);

        var result = await scenario.ExecuteAsync(batchSize: 2);

        Assert.AreEqual(InitialScanStatus.Completed, result.Status);
        Assert.AreEqual(5, result.ObservedEntries);
        Assert.AreEqual(5, result.CommittedEntries);
        Assert.AreEqual(1, scenario.BeginCount);
        CollectionAssert.AreEqual(ExpectedBatchSizes, scenario.BatchSizes);
        Assert.IsTrue(scenario.Completed);
        Assert.IsFalse(scenario.Aborted);
        Assert.AreEqual(ScanRunTerminalState.Completed, scenario.FinishedState);
    }

    [TestMethod]
    public async Task EmptyOnlineLibraryCommitsAnExplicitZeroEntrySnapshot()
    {
        var scenario = ScanScenario.WithEntries(0);

        var result = await scenario.ExecuteAsync();

        Assert.AreEqual(InitialScanStatus.Completed, result.Status);
        Assert.AreEqual(0, result.ObservedEntries);
        Assert.AreEqual(0, result.CommittedEntries);
        Assert.IsTrue(scenario.Completed);
    }

    [TestMethod]
    public async Task LargeDiscoveryIsGeneratedLazilyAndNeverExceedsTheBatchBound()
    {
        var scenario = ScanScenario.StreamingEntries(10_000);

        var result = await scenario.ExecuteAsync(batchSize: 256);

        Assert.AreEqual(10_000, result.CommittedEntries);
        Assert.HasCount(40, scenario.BatchSizes);
        Assert.IsLessThanOrEqualTo(256, scenario.BatchSizes.Max());
    }

    [TestMethod]
    public async Task OfflineStorageReturnsBeforeDiscoveryJournalOrIndexWrites()
    {
        var scenario = ScanScenario.Offline();

        var result = await scenario.ExecuteAsync();

        Assert.AreEqual(InitialScanStatus.StorageOffline, result.Status);
        Assert.AreEqual(0, scenario.EnumerationCount);
        Assert.AreEqual(0, scenario.BeginCount);
        Assert.AreEqual(0, scenario.StartCount);
        Assert.IsNull(scenario.FinishedState);
    }

    [TestMethod]
    public async Task MismatchedScanTargetFailsBeforeDiscoveryJournalOrIndexWrites()
    {
        var scenario = ScanScenario.MismatchedTarget();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scenario.ExecuteAsync());

        Assert.AreEqual(0, scenario.EnumerationCount);
        Assert.AreEqual(0, scenario.BeginCount);
        Assert.AreEqual(0, scenario.StartCount);
    }

    [TestMethod]
    public async Task DefaultLibraryIdFailsBeforeAnyPortIsCalled()
    {
        var scenario = ScanScenario.DefaultRequestLibraryId();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => scenario.ExecuteAsync());

        Assert.AreEqual(0, scenario.BeginCount);
        Assert.AreEqual(0, scenario.StartCount);
    }

    [TestMethod]
    public async Task IncompleteDiscoveryAbortsStagedRowsAndNeverCommitsThem()
    {
        var scenario = ScanScenario.WithEntries(2, discoveryFailure: "entry_metadata_failed");

        var result = await scenario.ExecuteAsync(batchSize: 1);

        Assert.AreEqual(InitialScanStatus.DiscoveryFailed, result.Status);
        Assert.AreEqual("entry_metadata_failed", result.FailureCode);
        Assert.AreEqual(0, result.CommittedEntries);
        Assert.IsTrue(scenario.Aborted);
        Assert.IsFalse(scenario.Completed);
        Assert.AreEqual(ScanRunTerminalState.DiscoveryFailed, scenario.FinishedState);
    }

    [TestMethod]
    public async Task TimeoutAbortsTheSnapshotWithAnExplicitTerminalState()
    {
        var scenario = ScanScenario.Blocking();

        var result = await scenario.ExecuteAsync(timeout: TimeSpan.FromMilliseconds(50));

        Assert.AreEqual(InitialScanStatus.TimedOut, result.Status);
        Assert.IsTrue(scenario.Aborted);
        Assert.AreEqual(ScanRunTerminalState.TimedOut, scenario.FinishedState);
    }

    [TestMethod]
    public async Task CallerCancellationAbortsThenPreservesCancellationSemantics()
    {
        var scenario = ScanScenario.Blocking();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            scenario.ExecuteAsync(cancellationToken: cancellation.Token));

        Assert.IsTrue(scenario.Aborted);
        Assert.AreEqual(ScanRunTerminalState.Cancelled, scenario.FinishedState);
    }

    [TestMethod]
    public async Task UnrelatedCancellationIsAnInternalFailureNotAClaimThatTheCallerCancelled()
    {
        var scenario = ScanScenario.UnrelatedCancellation();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => scenario.ExecuteAsync());

        Assert.IsTrue(scenario.Aborted);
        Assert.AreEqual(ScanRunTerminalState.DiscoveryFailed, scenario.FinishedState);
        Assert.AreEqual("scan_internal_failure", scenario.FailureCode);
    }

    [TestMethod]
    public async Task UnexpectedSinkFailureAlsoAbortsAndPropagatesTheOriginalError()
    {
        var scenario = ScanScenario.WithEntries(1, failStaging: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scenario.ExecuteAsync(batchSize: 1));

        Assert.IsTrue(scenario.Aborted);
        Assert.AreEqual("scan_internal_failure", scenario.FailureCode);
    }

    [TestMethod]
    public async Task SinkInitializationFailureTerminatesTheJournalWithoutACommit()
    {
        var scenario = ScanScenario.WithEntries(1, failInitialization: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scenario.ExecuteAsync());

        Assert.AreEqual(ScanRunTerminalState.DiscoveryFailed, scenario.FinishedState);
        Assert.AreEqual("scan_initialization_failed", scenario.FailureCode);
    }

    [TestMethod]
    public async Task JournalFailureAfterCommitNeverAttemptsToAbortCommittedObservations()
    {
        var scenario = ScanScenario.WithEntries(1, failFinalization: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scenario.ExecuteAsync());

        Assert.IsTrue(scenario.Completed);
        Assert.IsFalse(scenario.Aborted);
    }

    [TestMethod]
    public async Task InvalidBatchSizeFailsBeforeAnyPortIsCalled()
    {
        var scenario = ScanScenario.WithEntries(0);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => scenario.ExecuteAsync(batchSize: 1025));

        Assert.AreEqual(0, scenario.BeginCount);
        Assert.AreEqual(0, scenario.StartCount);
    }
}
