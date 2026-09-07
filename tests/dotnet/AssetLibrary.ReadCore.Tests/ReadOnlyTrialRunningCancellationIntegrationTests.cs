using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialRunningCancellationIntegrationTests
{
    [TestMethod]
    public async Task RunningCancellationAbortsOnlyStagedObservations()
    {
        var discovery = new PausingDiscovery();
        await using var fixture = new ReadOnlyTrialFixture(discovery);
        var library = await fixture.RegisterAsync();
        var queued = await fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None);
        var running = fixture.Scans.RunNextAsync(CancellationToken.None);
        await discovery.Staged.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, (await fixture.Scans.GetAsync(library, CancellationToken.None))!.ObservedEntries);
        await fixture.Scans.CancelAsync(library, queued.TaskId, fixture.Operation(), CancellationToken.None);
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        var cancelled = await fixture.Scans.GetAsync(library, CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Cancelled, cancelled!.State);
        Assert.AreEqual(0, cancelled.CommittedEntries);
        Assert.IsNull(await fixture.Snapshots.FindAsync(library, CancellationToken.None));
        Assert.AreEqual(0L, await fixture.SqlAsync($"SELECT count(*) FROM asset_identity.scan_observation_stage WHERE library_id='{library.Value:D}'"));
    }

}
