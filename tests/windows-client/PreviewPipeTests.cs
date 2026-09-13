using System.Runtime.Versioning;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class PreviewPipeTests
{
    [TestMethod]
    public async Task BothEndpointsShareFourSlotsAndDisconnectReclaimsAllWork()
    {
        var capacity = new ImageClientCapacity();
        var entered = 0; var cancelled = 0;
        async Task<ThumbnailResponse> Read(ThumbnailRequest request, CancellationToken token)
        {
            Interlocked.Increment(ref entered);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { Interlocked.Increment(ref cancelled); }
            throw new InvalidOperationException("Cancelled operation cannot publish.");
        }
        var suffix = ".test." + Guid.NewGuid().ToString("N");
        await using var thumbnails = new ThumbnailPipeServer(Read, capacity, ThumbnailPipeServer.Endpoint + suffix);
        await using var previews = new PreviewPipeServer(Read, capacity, PreviewPipeServer.Endpoint + suffix);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clients = new List<FileStream>();
        try
        {
            for (var index = 0; index < 4; ++index)
            {
                var preview = index % 2 == 0;
                var pipe = await LocalPipe.OpenClientAsync((preview ? PreviewPipeServer.Endpoint : ThumbnailPipeServer.Endpoint) + suffix, deadline.Token);
                clients.Add(pipe);
                var request = new ThumbnailRequest((uint)index + 1, Guid.NewGuid(), Guid.NewGuid());
                await pipe.WriteAsync(preview ? PreviewProtocol.EncodeRequest(request) : ThumbnailProtocol.EncodeRequest(request), deadline.Token);
            }
            while (Volatile.Read(ref entered) != 4) { await Task.Delay(10, deadline.Token); }
            Assert.AreEqual(4, capacity.Active);
            await using var excess = await LocalPipe.OpenClientAsync(PreviewPipeServer.Endpoint + suffix, deadline.Token);
            await HostNativePipeClient.AssertDisconnectedAsync(excess, deadline.Token);
            Assert.AreEqual(4, entered);
        }
        finally { foreach (var client in clients) { await client.DisposeAsync(); } }
        while (capacity.Active != 0) { await Task.Delay(10, deadline.Token); }
        Assert.AreEqual(4, cancelled);
        await using var partial = await LocalPipe.OpenClientAsync(PreviewPipeServer.Endpoint + suffix, deadline.Token);
        await partial.WriteAsync(new byte[1], deadline.Token);
        await HostNativePipeClient.AssertDisconnectedAsync(partial, deadline.Token);
    }

    [TestMethod]
    public async Task ActualPreviewPipePublishesOnlyAnExactRequestIdentity()
    {
        var endpoint = PreviewPipeServer.Endpoint + ".test." + Guid.NewGuid().ToString("N");
        await using var server = new PreviewPipeServer((request, _) => Task.FromResult(new ThumbnailResponse(
            ThumbnailStatus.Unsupported, request.Epoch, request.Node)), endpoint);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var request = new ThumbnailRequest(92, Guid.NewGuid(), Guid.NewGuid());
        var result = await ThumbnailLiveSupport.ReadPipeAsync(endpoint, request, deadline.Token, Client.DerivedImageProfile.Preview1600);
        Assert.AreEqual(ThumbnailStatus.Unsupported, result.Status); Assert.IsNull(result.Image);
        Assert.AreEqual(request.Epoch, result.Epoch); Assert.AreEqual(request.Node, result.Node);
    }
}
