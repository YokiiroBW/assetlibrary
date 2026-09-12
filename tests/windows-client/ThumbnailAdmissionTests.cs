using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class ThumbnailAdmissionTests
{
    [TestMethod]
    public async Task ASecondImageCannotConsumeTheNavigationSlot()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var images = 0;
        using var transport = ThumbnailTestSupport.Transport(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/image", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref images); entered.TrySetResult();
                await release.Task.WaitAsync(token); return ThumbnailTestSupport.ImageResponse();
            }
            return await ProtocolFixture.ResultAsync(request, HostTestSupport.Libraries());
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var first = transport.ThumbnailAsync(ThumbnailTestSupport.LibraryId, ThumbnailTestSupport.EntryId, deadline.Token);
        await entered.Task.WaitAsync(deadline.Token);
        var second = transport.ThumbnailAsync(ThumbnailTestSupport.LibraryId, ThumbnailTestSupport.EntryId, deadline.Token);
        try
        {
            _ = await new ReadOnlyClient(transport).LibrariesAsync(null, null, deadline.Token);
            Assert.AreEqual(1, Volatile.Read(ref images));
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, second);
        Assert.AreEqual(2, images);
    }
}
