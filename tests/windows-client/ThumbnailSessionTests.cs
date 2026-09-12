using System.Net;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class ThumbnailSessionTests
{
    [TestMethod]
    [DataRow(401, ThumbnailStatus.AccessDenied, true)]
    [DataRow(403, ThumbnailStatus.AccessDenied, true)]
    [DataRow(404, ThumbnailStatus.Unsupported, false)]
    [DataRow(409, ThumbnailStatus.Unavailable, false)]
    [DataRow(503, ThumbnailStatus.Unavailable, false)]
    public async Task ImageErrorsKeepOptionalFallbackSeparateFromIdentityLoss(int code, ThumbnailStatus expected, bool revoked)
    {
        var imageCalls = 0;
        using var transport = ThumbnailTestSupport.Transport((request, _) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/image", StringComparison.Ordinal)) { return ThumbnailTestSupport.PageAsync(request); }
            ++imageCalls; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code));
        });
        await using var store = new SnapshotStore(new ReadOnlyClient(transport), DateTimeOffset.UtcNow.AddHours(1));
        var request = await ThumbnailTestSupport.FileAsync(store);
        await using var thumbnails = new ThumbnailSession(transport, store, (_, _) => throw new InvalidOperationException("Error must not decode."));
        Assert.AreEqual(expected, (await thumbnails.ReadAsync(request, CancellationToken.None)).Status);
        Assert.AreEqual(revoked, store.IsRevoked); Assert.AreEqual(1, imageCalls);
        var old = request with { Epoch = Guid.NewGuid() };
        Assert.AreEqual(ThumbnailStatus.Expired, (await thumbnails.ReadAsync(old, CancellationToken.None)).Status);
        Assert.AreEqual(1, imageCalls);
    }

    [TestMethod]
    public async Task LateDecodeCannotPublishAfterEpochRevocation()
    {
        using var transport = ThumbnailTestSupport.SuccessfulTransport();
        await using var store = new SnapshotStore(new ReadOnlyClient(transport), DateTimeOffset.UtcNow.AddHours(1));
        var request = await ThumbnailTestSupport.FileAsync(store);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var thumbnails = new ThumbnailSession(transport, store, async (_, _) =>
        {
            entered.TrySetResult(); await release.Task;
            return new ThumbnailPixels(2, 2, ThumbnailTestSupport.ExpectedPixels);
        });
        var read = thumbnails.ReadAsync(request, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        store.RevokeSession(); release.TrySetResult();
        var result = await read;
        Assert.AreEqual(ThumbnailStatus.Expired, result.Status); Assert.IsNull(result.Image);
    }
}
