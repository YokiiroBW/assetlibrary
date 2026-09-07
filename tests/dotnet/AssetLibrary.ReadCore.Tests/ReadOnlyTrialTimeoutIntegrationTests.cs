using AssetLibrary.Modules.ScanReconciliation.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialTimeoutIntegrationTests
{
    [TestMethod]
    public async Task AnExpiredDiscoveryNeverBecomesAnEmptySuccessfulIndex()
    {
        await using var fixture = new ReadOnlyTrialFixture(new PausingDiscovery(), options: new InitialScanExecutionOptions
        { ScanTimeout = TimeSpan.FromMilliseconds(250), BatchSize = 1, HeartbeatInterval = TimeSpan.FromMilliseconds(50) });
        var library = await fixture.RegisterAsync();
        await fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None);
        await fixture.Scans.RunNextAsync(CancellationToken.None);
        var failed = await fixture.Scans.GetAsync(library, CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Failed, failed!.State);
        Assert.AreEqual("scan_timed_out", failed.FailureCode);
        Assert.IsNull(await fixture.Snapshots.FindAsync(library, CancellationToken.None));
    }
}
