using System.Buffers.Binary;
using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class ControlPipeTests
{
    [TestMethod]
    public async Task ProductionShapeReservesFirstInstanceAndSupportsStatusAndShutdown()
    {
        var path = SessionTestSupport.TemporaryPath();
        var endpoint = LocalPipe.ControlEndpoint + ".test." + Guid.NewGuid().ToString("N");
        try
        {
            await using var session = new UserSessionCoordinator(new UserConnectionStore(path));
            await using var server = new ControlPipeServer(session, endpoint);
            Assert.ThrowsExactly<IOException>(() => new ControlPipeServer(session, endpoint));
            var request = new ControlRequest(1, Guid.NewGuid(), ControlOperation.Status);
            var response = await ControlClient.SendAsync(request, CancellationToken.None, endpoint);
            Assert.AreEqual(request.RequestId, response.RequestId);
            Assert.AreEqual(ConnectionState.Unconfigured, response.Status.State);
            Assert.IsFalse(server.Completion.IsCompleted);
            var shutdown = await ControlClient.SendAsync(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Shutdown), CancellationToken.None, endpoint);
            Assert.IsTrue(shutdown.Ok);
            await server.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { if (Directory.Exists(path)) { Directory.Delete(path, true); } }
    }

    [TestMethod]
    public async Task OversizedAndAbandonedFramesCannotStopListener()
    {
        var path = SessionTestSupport.TemporaryPath();
        var endpoint = LocalPipe.ControlEndpoint + ".test." + Guid.NewGuid().ToString("N");
        try
        {
            await using var session = new UserSessionCoordinator(new UserConnectionStore(path));
            await using var server = new ControlPipeServer(session, endpoint);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await using (var bad = await LocalPipe.OpenClientAsync(endpoint, deadline.Token))
            {
                var size = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(size, 16385);
                await bad.WriteAsync(size, deadline.Token);
                await HostNativePipeClient.AssertDisconnectedAsync(bad, deadline.Token);
            }
            await using (var stalled = await LocalPipe.OpenClientAsync(endpoint, deadline.Token))
            {
                await stalled.WriteAsync(new byte[1], deadline.Token);
                await HostNativePipeClient.AssertDisconnectedAsync(stalled, deadline.Token);
            }
            var status = await ControlClient.SendAsync(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Status), deadline.Token, endpoint);
            Assert.IsTrue(status.Ok);
            Assert.AreEqual(ConnectionState.Unconfigured, status.Status.State);
        }
        finally { if (Directory.Exists(path)) { Directory.Delete(path, true); } }
    }
}
