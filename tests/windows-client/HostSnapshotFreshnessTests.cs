using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class HostSnapshotFreshnessTests
{
    [TestMethod]
    public async Task SlowLoggingCannotRenewTheAgeOfAnAlreadyCompletedCorePage()
    {
        var clock = new HostTestSupport.ManualClock();
        var logged = 0;
        var calls = 0;
        var logEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseLog = new ManualResetEventSlim();
        using var handler = new ProtocolFixture((request, _) =>
        {
            Interlocked.Increment(ref calls);
            return ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries());
        });
        using var transport = HostTestSupport.CreateTransport(handler);
        await using var store = new SnapshotStore(new ReadOnlyClient(transport), clock.GetUtcNow().AddHours(1), clock,
            log: (_, _, _) =>
            {
                if (Interlocked.Increment(ref logged) != 1) { return; }
                logEntered.TrySetResult();
                if (!releaseLog.Wait(TimeSpan.FromSeconds(3))) { throw new TimeoutException("Test log release timed out."); }
            });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            Assert.AreEqual(SnapshotStatus.Loading, store.Query(HostTestSupport.Root).Status);
            await logEntered.Task.WaitAsync(deadline.Token);
            clock.Advance(TimeSpan.FromSeconds(6));
            releaseLog.Set();
            while (store.Counts.Running != 0) { await Task.Delay(1, deadline.Token); }
            var response = store.Query(HostTestSupport.Root);
            Assert.AreEqual(SnapshotStatus.Loading, response.Status, "Core completion time must precede the blocked log sink.");
            Assert.HasCount(0, response.Items);
            Assert.AreEqual(SnapshotStatus.Ready, (await HostTestSupport.SettledAsync(store)).Status);
            Assert.AreEqual(2, Volatile.Read(ref calls));
        }
        finally { releaseLog.Set(); }
    }
}
