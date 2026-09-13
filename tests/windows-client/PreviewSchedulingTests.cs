using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class PreviewSchedulingTests
{
    [TestMethod]
    public async Task MixedImageWorkHasOneDecoderAndFourTotalQueueSlots()
    {
        using var transport = ThumbnailTestSupport.SuccessfulTransport();
        await using var store = new SnapshotStore(new ReadOnlyClient(transport), DateTimeOffset.UtcNow.AddHours(1));
        var request = await ThumbnailTestSupport.FileAsync(store);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<ThumbnailPixels> Decode(byte[] bytes, CancellationToken token)
        {
            Interlocked.Increment(ref calls); entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { stopped.TrySetResult(); }
            throw new InvalidOperationException("Cancellation required.");
        }
        await using var images = new ThumbnailSession(transport, store, Decode, Decode);
        using var cancellation = new CancellationTokenSource();
        var first = images.ReadPreviewAsync(request, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var pending = new[] { first, images.ReadAsync(request, cancellation.Token),
            images.ReadPreviewAsync(request, cancellation.Token), images.ReadAsync(request, cancellation.Token) };
        var rejected = await images.ReadPreviewAsync(request, CancellationToken.None);
        Assert.AreEqual(ThumbnailStatus.Busy, rejected.Status); Assert.AreEqual(request.Epoch, rejected.Epoch);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(SnapshotStatus.Ready, store.Query(HostTestSupport.Root).Status);
        await cancellation.CancelAsync();
        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(3));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsTrue(pending.All(task => task.Result.Image is null)); Assert.AreEqual(1, calls);
    }
}
