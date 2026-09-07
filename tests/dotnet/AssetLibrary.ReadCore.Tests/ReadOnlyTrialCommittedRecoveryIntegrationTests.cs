using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialCommittedRecoveryIntegrationTests
{
    [TestMethod]
    public async Task RecoveredCommittedSnapshotRepairsAnExpiredFinalAttempt()
    {
        await using var fixture = new ReadOnlyTrialFixture();
        var library = await fixture.RegisterAsync();
        var queued = await fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None);
        var (lease, request) = await fixture.ClaimAsync();
        var scanId = Guid.NewGuid();
        await fixture.Store.StartRunAsync(scanId, request, lease.Attempt, DateTimeOffset.UtcNow, CancellationToken.None);
        await using (var session = await fixture.Sink.BeginInitialScanAsync(scanId, library, DateTimeOffset.UtcNow, CancellationToken.None))
        {
            await session.StageAsync([ReadOnlyTrialObservations.File("summer-photo.jpg")], CancellationToken.None);
            Assert.AreEqual(1, await session.CompleteAsync(DateTimeOffset.UtcNow, CancellationToken.None));
        }

        var snapshot = await fixture.Snapshots.FindAsync(library, CancellationToken.None);
        await fixture.SqlAsync($"UPDATE task_health.durable_task SET attempts=max_attempts,lease_until=clock_timestamp()-interval '1 second' WHERE task_id='{queued.TaskId:D}'");
        await fixture.Tasks.ReclaimExpiredAsync(32, CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Failed, (await fixture.Execution.FindAsync(lease.TaskId, CancellationToken.None))!.Snapshot.State);
        await fixture.Scans.RecoverAsync(CancellationToken.None);
        var recovered = await fixture.Scans.GetAsync(library, CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Succeeded, recovered!.State);
        Assert.AreEqual(scanId, recovered.ScanId);
        Assert.AreEqual(snapshot, await fixture.Snapshots.FindAsync(library, CancellationToken.None));
    }

}
