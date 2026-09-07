using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialAvailabilityIntegrationTests
{
    [TestMethod]
    public async Task OfflineLibraryDoesNotDisableSiblingAndRetryProbesRecoveryImmediately()
    {
        await using var fixture = new ReadOnlyTrialFixture();
        var libraryId = await fixture.RegisterAsync("one");
        var sibling = await fixture.RegisterAsync("two");
        var path = Path.Combine(fixture.Sandbox.Root, "one");
        var offline = Path.Combine(fixture.Sandbox.Root, "one-offline");
        Directory.Move(path, offline);
        try
        {
            Assert.AreEqual(StorageAvailability.Offline, await fixture.Availability.RefreshAsync(libraryId, CancellationToken.None));
            Assert.AreEqual(StorageAvailability.Online, (await fixture.Libraries.FindAsync(sibling, CancellationToken.None))!.Availability);
            var unavailable = await Assert.ThrowsExactlyAsync<ReadOnlyTrialException>(async () =>
                await fixture.Scans.StartAsync(libraryId, fixture.Operation(), CancellationToken.None));
            Assert.AreEqual("storage_unavailable", unavailable.Code);
        }
        finally
        {
            Directory.Move(offline, path);
        }

        var scan = await fixture.Scans.StartAsync(libraryId, fixture.Operation(), CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Queued, scan.State);
        Assert.AreEqual(StorageAvailability.Online, (await fixture.Libraries.FindAsync(libraryId, CancellationToken.None))!.Availability);
        await fixture.Scans.RunNextAsync(CancellationToken.None);
        var snapshot = await fixture.Snapshots.FindAsync(libraryId, CancellationToken.None);
        Directory.Move(path, offline);
        try
        {
            await fixture.Availability.RefreshAsync(libraryId, CancellationToken.None);
            Assert.AreEqual(snapshot, await fixture.Snapshots.FindAsync(libraryId, CancellationToken.None));
        }
        finally
        {
            Directory.Move(offline, path);
        }
    }

}
