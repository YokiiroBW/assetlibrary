using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class ThumbnailSchedulingTests
{
    [TestMethod]
    public async Task SynchronousImageWorkCannotHoldTheCallingSessionCriticalSection()
    {
        using var transport = ThumbnailTestSupport.Transport((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/image", StringComparison.Ordinal)
            ? Task.FromResult(ThumbnailTestSupport.ImageResponse()) : ThumbnailTestSupport.PageAsync(request));
        await using var store = new SnapshotStore(new ReadOnlyClient(transport), DateTimeOffset.UtcNow.AddHours(1));
        var request = await ThumbnailTestSupport.FileAsync(store);
        var decoderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var thumbnails = new ThumbnailSession(transport, store, (_, _) =>
        {
            decoderEntered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(3), CancellationToken.None)) { throw new TimeoutException("Test decoder release timed out."); }
            return Task.FromResult(new ThumbnailPixels(2, 2, ThumbnailTestSupport.ExpectedPixels));
        });
        var callerGate = new object();
        Task<ThumbnailResponse>? result = null;
        var caller = Task.Run(() => { lock (callerGate) { result = thumbnails.ReadAsync(request, CancellationToken.None); } });
        try
        {
            await decoderEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var entered = Monitor.TryEnter(callerGate, TimeSpan.FromMilliseconds(150));
            try
            {
                Assert.IsTrue(entered, "Status/page calls must not wait behind synchronous image work.");
                Assert.AreEqual(SnapshotStatus.Ready, store.Query(HostTestSupport.Root).Status);
            }
            finally { if (entered) { Monitor.Exit(callerGate); } }
        }
        finally { release.Set(); await caller; }
        Assert.AreEqual(ThumbnailStatus.Ready, (await result!).Status);
    }
}
