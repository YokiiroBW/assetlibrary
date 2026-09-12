using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class ThumbnailPipeTests
{
    [TestMethod]
    public async Task DisconnectCancelsAndReclaimsTheActualInFlightOperation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = ThumbnailPipeServer.Endpoint + ".test." + Guid.NewGuid().ToString("N");
        await using var server = new ThumbnailPipeServer(async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cancelled.TrySetResult(); }
            throw new InvalidOperationException("Cancelled request must not publish.");
        }, endpoint);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using (var pipe = await LocalPipe.OpenClientAsync(endpoint, deadline.Token))
        {
            await pipe.WriteAsync(ThumbnailProtocol.EncodeRequest(new ThumbnailRequest(1, Guid.NewGuid(), Guid.NewGuid())), deadline.Token);
            await entered.Task.WaitAsync(deadline.Token);
        }
        await cancelled.Task.WaitAsync(deadline.Token);
    }

    [TestMethod]
    public async Task RealPipeReturnsOnePixelFrameAndRecoversAfterPartialRequest()
    {
        var endpoint = ThumbnailPipeServer.Endpoint + ".test." + Guid.NewGuid().ToString("N");
        await using var server = new ThumbnailPipeServer((request, _) => Task.FromResult(new ThumbnailResponse(
            ThumbnailStatus.Ready, request.Epoch, request.Node, new ThumbnailPixels(2, 2, ThumbnailTestSupport.ExpectedPixels))), endpoint);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using (var partial = await LocalPipe.OpenClientAsync(endpoint, deadline.Token))
        {
            await partial.WriteAsync(new byte[1], deadline.Token);
            await HostNativePipeClient.AssertDisconnectedAsync(partial, deadline.Token);
        }
        var request = new ThumbnailRequest(7, Guid.NewGuid(), Guid.NewGuid());
        await using var pipe = await LocalPipe.OpenClientAsync(endpoint, deadline.Token);
        await pipe.WriteAsync(ThumbnailProtocol.EncodeRequest(request), deadline.Token);
        var frame = new byte[16 + 56 + 16]; await pipe.ReadExactlyAsync(frame, deadline.Token);
        var response = ThumbnailProtocol.DecodeResponse(frame, request);
        CollectionAssert.AreEqual(ThumbnailTestSupport.ExpectedPixels, response.Image!.Bytes);
    }
}
