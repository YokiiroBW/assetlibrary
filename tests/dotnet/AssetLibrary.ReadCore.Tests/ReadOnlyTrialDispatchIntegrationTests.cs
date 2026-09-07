using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialDispatchIntegrationTests
{
    [TestMethod]
    public async Task CrashBetweenAcceptAndEnqueueRecoversOneDurableTask()
    {
        await using var fixture = new ReadOnlyTrialFixture();
        var library = await fixture.RegisterAsync();
        var operation = fixture.Operation();
        var accepted = await fixture.Store.AcceptAsync(library, operation, CancellationToken.None);
        Assert.IsNull(await fixture.Execution.FindAsync(accepted.TaskId, CancellationToken.None));
        await fixture.Scans.RecoverAsync(CancellationToken.None);
        Assert.AreEqual(DurableTaskState.Queued, (await fixture.Execution.FindAsync(accepted.TaskId, CancellationToken.None))!.Snapshot.State);
        await fixture.Scans.RunNextAsync(CancellationToken.None);
        var replay = await fixture.Scans.StartAsync(library, operation, CancellationToken.None);
        Assert.AreEqual(accepted.TaskId.Value, replay.TaskId);
        Assert.AreEqual(DurableTaskState.Succeeded, replay.State);
    }

    [TestMethod]
    public async Task ConcurrentStartsShareOneActiveTaskAcrossDistinctRequestKeys()
    {
        await using var fixture = new ReadOnlyTrialFixture();
        var library = await fixture.RegisterAsync();
        var starts = Enumerable.Range(0, 4).Select(_ =>
            fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None).AsTask()).ToArray();
        var results = await Task.WhenAll(starts);
        Assert.HasCount(1, results.Select(result => result.TaskId).Distinct().ToArray());
        await fixture.Scans.RunNextAsync(CancellationToken.None);
        Assert.IsFalse(await fixture.Scans.RunNextAsync(CancellationToken.None));
    }
}
