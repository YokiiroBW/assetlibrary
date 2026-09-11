using System.Net;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class HostSnapshotTests
{
    [TestMethod]
    public async Task ExpiredSnapshotNeverFallsBackAndFailureRetriesAfterOneSecond()
    {
        var clock = new HostTestSupport.ManualClock();
        var calls = 0;
        using var handler = new ProtocolFixture((request, _) => Interlocked.Increment(ref calls) == 1
            ? ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries())
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var transport = HostTestSupport.CreateTransport(handler);
        await using var store = HostTestSupport.CreateStore(transport, clock);
        Assert.AreEqual(SnapshotStatus.Ready, (await HostTestSupport.SettledAsync(store)).Status);
        clock.Advance(TimeSpan.FromSeconds(5));
        var refreshing = store.Query(HostTestSupport.Root);
        Assert.AreEqual(SnapshotStatus.Loading, refreshing.Status);
        Assert.HasCount(0, refreshing.Items);
        Assert.AreEqual(SnapshotStatus.Unavailable, (await HostTestSupport.SettledAsync(store)).Status);
        _ = store.Query(HostTestSupport.Root);
        Assert.AreEqual(2, calls);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(SnapshotStatus.Unavailable, (await HostTestSupport.SettledAsync(store)).Status);
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(404)]
    public async Task PermissionLossChangesEpochAndClearsAllIdentityData(int status)
    {
        var clock = new HostTestSupport.ManualClock();
        var calls = 0;
        using var handler = new ProtocolFixture((request, _) => ++calls == 1
            ? ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries())
            : Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var transport = HostTestSupport.CreateTransport(handler);
        await using var store = HostTestSupport.CreateStore(transport, clock);
        var root = await HostTestSupport.SettledAsync(store);
        clock.Advance(TimeSpan.FromSeconds(5));
        var rejected = await HostTestSupport.SettledAsync(store);
        Assert.AreEqual(SnapshotStatus.AccessDenied, rejected.Status);
        Assert.AreNotEqual(root.Epoch, rejected.Epoch);
        Assert.AreEqual(SnapshotStatus.Expired, store.Query(HostTestSupport.Open(root, root.Items[0])).Status);
        Assert.AreEqual((0, 0, 0), store.Counts);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.AreEqual(SnapshotStatus.AccessDenied, store.Query(HostTestSupport.Root).Status);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task CursorExpiryInvalidatesNodesAndAllowsRootReopen()
    {
        var calls = 0;
        using var handler = new ProtocolFixture((request, _) => ++calls == 2
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone))
            : ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries()));
        using var transport = HostTestSupport.CreateTransport(handler);
        await using var store = HostTestSupport.CreateStore(transport);
        var root = await HostTestSupport.SettledAsync(store);
        var old = HostTestSupport.Open(root, root.Items[0]);
        Assert.AreEqual(SnapshotStatus.Expired, (await HostTestSupport.SettledAsync(store, old)).Status);
        var reopened = await HostTestSupport.SettledAsync(store);
        Assert.AreEqual(SnapshotStatus.Ready, reopened.Status);
        Assert.AreNotEqual(root.Epoch, reopened.Epoch);
        Assert.AreEqual(SnapshotStatus.Expired, store.Query(old).Status);
    }

    [TestMethod]
    public async Task SessionExpiryAndLateResponseCannotRepopulateCache()
    {
        var clock = new HostTestSupport.ManualClock();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ProtocolFixture(async (request, _) =>
        { started.SetResult(); await released.Task; return await ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries()); });
        using var transport = HostTestSupport.CreateTransport(handler);
        await using var store = new SnapshotStore(new ReadOnlyClient(transport), clock.GetUtcNow().AddSeconds(1), clock);
        var pending = store.Query(HostTestSupport.Root);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromSeconds(2));
        var expired = store.Query(HostTestSupport.Root);
        Assert.AreEqual(SnapshotStatus.AccessDenied, expired.Status);
        Assert.AreNotEqual(pending.Epoch, expired.Epoch);
        released.SetResult();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (store.Counts.Running != 0) { await Task.Delay(5, deadline.Token); }
        Assert.AreEqual((0, 0, 0), store.Counts);
        Assert.AreEqual(SnapshotStatus.AccessDenied, store.Query(HostTestSupport.Root).Status);
    }

    [TestMethod]
    public async Task TwoReadsCoalesceAndThirdReturnsBusyWithoutStartingNetwork()
    {
        var started = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ProtocolFixture(async (request, token) =>
        {
            if (Interlocked.Increment(ref started) == 1) { return await ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries(3)); }
            await release.Task.WaitAsync(token);
            return await ProtocolFixture.ResultAsync(request, ProtocolFixture.Page());
        });
        using var transport = HostTestSupport.CreateTransport(handler);
        await using var store = HostTestSupport.CreateStore(transport);
        var root = await HostTestSupport.SettledAsync(store);
        var first = HostTestSupport.Open(root, root.Items[0]);
        Assert.AreEqual(SnapshotStatus.Loading, store.Query(first).Status);
        Assert.AreEqual(SnapshotStatus.Loading, store.Query(first).Status);
        Assert.AreEqual(SnapshotStatus.Loading, store.Query(HostTestSupport.Open(root, root.Items[1])).Status);
        Assert.AreEqual(SnapshotStatus.Busy, store.Query(HostTestSupport.Open(root, root.Items[2])).Status);
        Assert.AreEqual(2, store.Counts.Running);
        release.SetResult();
        _ = await HostTestSupport.SettledAsync(store, first);
        Assert.AreEqual(3, started);
    }
}
