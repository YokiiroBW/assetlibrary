using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialRegistrationIntegrationTests
{
    [TestMethod]
    public async Task RegistrationScanAndReplayPreservePhysicalFilesAndImmutableIdentity()
    {
        await using var fixture = new ReadOnlyTrialFixture();
        var path = Directory.CreateDirectory(Path.Combine(fixture.Sandbox.Root, "photos")).FullName;
        await File.WriteAllTextAsync(Path.Combine(path, "summer-photo.jpg"), "same-physical-content");
        var before = fixture.Sandbox.CaptureStrongSnapshot();
        var operation = fixture.Operation();
        var request = new LibraryRegistrationRequest("sandbox", "Photos", path);
        var libraryId = await fixture.Registration.RegisterAsync(request, operation, CancellationToken.None);
        Assert.AreEqual(libraryId, await fixture.Registration.RegisterAsync(request, operation, CancellationToken.None));
        var conflict = await Assert.ThrowsExactlyAsync<ReadOnlyTrialException>(async () =>
            await fixture.Registration.RegisterAsync(request with { DisplayName = "changed" }, operation, CancellationToken.None));
        Assert.AreEqual("idempotency_conflict", conflict.Code);
        var overlap = await Assert.ThrowsExactlyAsync<ReadOnlyTrialException>(async () =>
            await fixture.Registration.RegisterAsync(request with { RootPath = fixture.Sandbox.Root }, fixture.Operation(), CancellationToken.None));
        Assert.AreEqual("root_overlap", overlap.Code);

        var start = fixture.Operation();
        var queued = await fixture.Scans.StartAsync(libraryId, start, CancellationToken.None);
        Assert.AreEqual(queued.TaskId, (await fixture.Scans.StartAsync(libraryId, fixture.Operation(), CancellationToken.None)).TaskId);
        Assert.IsTrue(await fixture.Scans.RunNextAsync(CancellationToken.None));
        var completed = await fixture.Scans.GetAsync(libraryId, CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Succeeded, completed!.State);
        Assert.AreEqual(1, completed.CommittedEntries);
        Assert.AreEqual(1, completed.ObservedEntries);
        Assert.AreEqual(completed, await fixture.Scans.StartAsync(libraryId, start, CancellationToken.None));
        var snapshot = await fixture.Snapshots.FindAsync(libraryId, CancellationToken.None);
        Assert.AreEqual(completed.ScanId, snapshot!.ScanId);
        var indexed = await Assert.ThrowsExactlyAsync<ReadOnlyTrialException>(async () =>
            await fixture.Scans.StartAsync(libraryId, fixture.Operation(), CancellationToken.None));
        Assert.AreEqual("already_indexed", indexed.Code);
        Assert.AreEqual(before, fixture.Sandbox.CaptureStrongSnapshot());
    }

}
