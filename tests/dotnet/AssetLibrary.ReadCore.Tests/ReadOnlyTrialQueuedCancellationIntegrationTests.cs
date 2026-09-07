using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialQueuedCancellationIntegrationTests
{
    [TestMethod]
    public async Task QueuedCancellationIsDurableIdempotentAndAllowsANewAttempt()
    {
        await using var fixture = new ReadOnlyTrialFixture();
        var library = await fixture.RegisterAsync();
        var queued = await fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None);
        var cancellation = fixture.Operation();
        var cancelled = await fixture.Scans.CancelAsync(library, queued.TaskId, cancellation, CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Cancelled, cancelled.State);
        Assert.AreEqual(cancelled, await fixture.Scans.CancelAsync(library, queued.TaskId, cancellation, CancellationToken.None));
        Assert.IsNull(await fixture.Snapshots.FindAsync(library, CancellationToken.None));
        var retry = await fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None);
        Assert.AreNotEqual(queued.TaskId, retry.TaskId);
        await fixture.Scans.RunNextAsync(CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Succeeded, (await fixture.Scans.GetAsync(library, CancellationToken.None))!.State);
        var conflict = await Assert.ThrowsExactlyAsync<ReadOnlyTrialException>(async () =>
            await fixture.Scans.CancelAsync(library, retry.TaskId, cancellation, CancellationToken.None));
        Assert.AreEqual("idempotency_conflict", conflict.Code);
    }

}
