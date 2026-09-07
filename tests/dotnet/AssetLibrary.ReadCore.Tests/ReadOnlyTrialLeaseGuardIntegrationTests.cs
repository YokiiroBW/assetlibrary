using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.ReadCore.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReadOnlyTrialLeaseGuardIntegrationTests
{
    [TestMethod]
    public async Task CommitGuardRejectsLostAndCancelledLeasesBeforeCallingTheAssetCommit()
    {
        await using var fixture = new ReadOnlyTrialFixture();
        var library = await fixture.RegisterAsync();
        await fixture.Scans.StartAsync(library, fixture.Operation(), CancellationToken.None);
        var (lease, _) = await fixture.ClaimAsync();
        var called = false;
        ValueTask<int> Commit(CancellationToken _) { called = true; return ValueTask.FromResult(1); }
        var identity = lease.Identity with { Generation = lease.Identity.Generation + 1 };
        await Assert.ThrowsExactlyAsync<TaskCommitLeaseException>(async () =>
            await fixture.Execution.CommitAsync(new DurableTaskHeartbeatRequest(lease.TaskId, identity, TimeSpan.FromMinutes(1)), Commit, CancellationToken.None));
        Assert.IsFalse(called);
        await fixture.Tasks.RequestCancellationAsync(lease.TaskId, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<TaskCommitLeaseException>(async () =>
            await fixture.Execution.CommitAsync(new DurableTaskHeartbeatRequest(lease.TaskId, lease.Identity, TimeSpan.FromMinutes(1)), Commit, CancellationToken.None));
        Assert.IsFalse(called);
        await fixture.Tasks.FinishAsync(new DurableTaskFinishRequest(lease.TaskId, lease.Identity, DurableTaskFinishKind.Cancelled), CancellationToken.None);
        await fixture.Scans.RecoverAsync(CancellationToken.None);
    }

}
