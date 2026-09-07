using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialJournalRecoveryIntegrationTests
{
    [TestMethod]
    public async Task CompletedSnapshotSurvivesLostJournalAcknowledgement()
    {
        FailCompletedJournal? injected = null;
        await using var fixture = new ReadOnlyTrialFixture(decorateStore: store => injected = new FailCompletedJournal(store));
        var library = await fixture.RegisterAsync();
        await fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None);
        await fixture.Scans.RunNextAsync(CancellationToken.None);
        Assert.IsTrue(injected!.Fired);
        var completed = await fixture.Scans.GetAsync(library, CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Succeeded, completed!.State);
        Assert.AreEqual(1, completed.CommittedEntries);
        Assert.AreEqual(completed.ScanId, (await fixture.Snapshots.FindAsync(library, CancellationToken.None))!.ScanId);
    }

}
