using System.Diagnostics;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class HostPipeReconnectTests
{
    [TestMethod]
    public async Task NativeReadCloseCanReuseEveryReservedInstanceWithoutErrors()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Native Windows handles are required."); }
        var failures = 0;
        await using var fixture = await HostPipeFixture.CreateAsync(prime: true,
            log: (_, _, _) => Interlocked.Increment(ref failures));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 64; ++index)
        {
            await using (var client = await HostNativePipeClient.OpenAsync(fixture.Server.PipeName, deadline.Token))
            {
                await client.WriteAsync(SnapshotProtocol.EncodeRequest(HostTestSupport.Root), deadline.Token);
                await client.FlushAsync(deadline.Token);
                var response = await HostPipeTestSupport.ReadResponseAsync(client, deadline.Token);
                Assert.AreEqual(SnapshotStatus.Ready, response.Status);
            }
            await Task.Delay(10, deadline.Token);
            Assert.AreEqual(0, Volatile.Read(ref failures), "Normal native client closure must detach before the next accept.");
        }
        Assert.IsFalse(fixture.Server.Completion.IsCompleted);
    }

    [TestMethod]
    public async Task MalformedNativeClientsBackOffWithoutFloodingAndLeaveNextExchangeUsable()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Native Windows handles are required."); }
        var failures = 0;
        await using var fixture = await HostPipeFixture.CreateAsync(prime: true,
            log: (_, _, _) => Interlocked.Increment(ref failures));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < 20; ++index)
        {
            await using var client = await HostNativePipeClient.OpenAsync(fixture.Server.PipeName, deadline.Token);
            var invalid = SnapshotProtocol.EncodeRequest(HostTestSupport.Root);
            invalid[0] = 0;
            await client.WriteAsync(invalid, deadline.Token);
            await client.FlushAsync(deadline.Token);
            await HostNativePipeClient.AssertDisconnectedAsync(client, deadline.Token);
        }
        var elapsed = Stopwatch.GetElapsedTime(started);
        // Four independent listeners can reject four requests before each 100 ms backoff.
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(350), elapsed);
        Assert.IsGreaterThan(0, Volatile.Read(ref failures));
        Assert.IsLessThanOrEqualTo(4 * ((int)Math.Ceiling(elapsed.TotalSeconds) + 1), Volatile.Read(ref failures));
        await using var recovered = await HostNativePipeClient.OpenAsync(fixture.Server.PipeName, deadline.Token);
        await recovered.WriteAsync(SnapshotProtocol.EncodeRequest(HostTestSupport.Root), deadline.Token);
        await recovered.FlushAsync(deadline.Token);
        var response = await HostPipeTestSupport.ReadResponseAsync(recovered, deadline.Token);
        Assert.IsTrue(response.Status is SnapshotStatus.Ready or SnapshotStatus.Loading);
        Assert.IsFalse(fixture.Server.Completion.IsCompleted);
    }
}
