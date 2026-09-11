using System.Buffers.Binary;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class HostPipeTests
{
    [TestMethod]
    public async Task RealPipeKeepsDelayedResponseUntilClientReads()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows named pipes are required."); }
        await using var fixture = await HostPipeFixture.CreateAsync(prime: true);
        var name = fixture.Server.PipeName;
        using var client = HostPipeTestSupport.NewClient(name);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(deadline.Token);
        await client.WriteAsync(SnapshotProtocol.EncodeRequest(HostTestSupport.Root), deadline.Token);
        await Task.Delay(100, deadline.Token);
        var response = await HostPipeTestSupport.ReadResponseAsync(client, deadline.Token);
        Assert.AreEqual(SnapshotStatus.Ready, response.Status);
        Assert.HasCount(1, response.Items);
    }

    [TestMethod]
    public async Task FourStalledClientsAreBoundedAndRecoverAfterDeadline()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows named pipes are required."); }
        await using var fixture = await HostPipeFixture.CreateAsync();
        var name = fixture.Server.PipeName;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stalled = Enumerable.Range(0, 4).Select(_ => HostPipeTestSupport.NewClient(name)).ToArray();
        try
        {
            foreach (var client in stalled) { await client.ConnectAsync(deadline.Token); }
            using var fifth = HostPipeTestSupport.NewClient(name);
            using var shortDeadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAsync<OperationCanceledException>(() => fifth.ConnectAsync(shortDeadline.Token));
            await Task.Delay(600, deadline.Token);
            using var recovered = HostPipeTestSupport.NewClient(name);
            await recovered.ConnectAsync(deadline.Token);
            await recovered.WriteAsync(SnapshotProtocol.EncodeRequest(HostTestSupport.Root), deadline.Token);
            var response = await HostPipeTestSupport.ReadResponseAsync(recovered, deadline.Token);
            Assert.IsTrue(response.Status is SnapshotStatus.Ready or SnapshotStatus.Loading);
        }
        finally { foreach (var client in stalled) { client.Dispose(); } }
    }

    [TestMethod]
    public async Task InvalidHeaderDoesNotAllocateDeclaredPayloadOrPoisonNextClient()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows named pipes are required."); }
        await using var fixture = await HostPipeFixture.CreateAsync();
        var name = fixture.Server.PipeName;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using (var bad = HostPipeTestSupport.NewClient(name))
        {
            await bad.ConnectAsync(deadline.Token);
            var header = SnapshotProtocol.EncodeRequest(HostTestSupport.Root)[..16];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), uint.MaxValue);
            await bad.WriteAsync(header, deadline.Token);
            var bytes = new byte[1];
            Assert.AreEqual(0, await bad.ReadAsync(bytes, deadline.Token));
        }
        using var good = HostPipeTestSupport.NewClient(name);
        await good.ConnectAsync(deadline.Token);
        await good.WriteAsync(SnapshotProtocol.EncodeRequest(HostTestSupport.Root), deadline.Token);
        Assert.AreEqual(SnapshotStatus.Loading, (await HostPipeTestSupport.ReadResponseAsync(good, deadline.Token)).Status);
    }

}

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class HostPipeReservationTests
{
    [TestMethod]
    public async Task SecondHostCannotReserveLiveEndpoint()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows named pipes are required."); }
        await using var fixture = await HostPipeFixture.CreateAsync();
        Assert.ThrowsExactly<IOException>(() => new SnapshotPipeServer(fixture.Store, fixture.Server.PipeName));
    }
}
