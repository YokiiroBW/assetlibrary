using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialInterruptedRecoveryIntegrationTests
{
    [TestMethod]
    public async Task ExpiredIncompleteScanRestartsWithoutPublishingOldPartialRows()
    {
        await using var fixture = new ReadOnlyTrialFixture();
        var library = await fixture.RegisterAsync();
        await fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None);
        var (lease, request) = await fixture.ClaimAsync();
        var abandoned = Guid.NewGuid();
        await fixture.Store.StartRunAsync(abandoned, request, lease.Attempt, DateTimeOffset.UtcNow, CancellationToken.None);
        await using (var session = await fixture.Sink.BeginInitialScanAsync(abandoned, library, DateTimeOffset.UtcNow, CancellationToken.None))
        {
            await session.StageAsync([ReadOnlyTrialObservations.File("partial-only.bin")], CancellationToken.None);
        }

        await fixture.SqlAsync($"UPDATE task_health.durable_task SET lease_until=clock_timestamp()-interval '1 second' WHERE task_id='{lease.TaskId.Value:D}'");
        await fixture.Scans.RecoverAsync(CancellationToken.None);
        await fixture.Scans.RunNextAsync(CancellationToken.None);
        var completed = await fixture.Scans.GetAsync(library, CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Succeeded, completed!.State);
        Assert.AreEqual(1, completed.CommittedEntries);
        Assert.AreNotEqual(abandoned, completed.ScanId);
        Assert.AreEqual(0L, await fixture.SqlAsync($"SELECT count(*) FROM asset_identity.filesystem_entry WHERE library_id='{library.Value:D}' AND normalized_relative_path='partial-only.bin'"));
    }

}
