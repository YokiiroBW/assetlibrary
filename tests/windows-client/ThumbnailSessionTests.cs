using System.Net;
using AssetLibrary.Windows.AssetHost;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class ThumbnailSessionTests
{
    [TestMethod]
    [DataRow(401, ThumbnailStatus.AccessDenied, true, false)]
    [DataRow(401, ThumbnailStatus.AccessDenied, true, true)]
    [DataRow(403, ThumbnailStatus.AccessDenied, true, false)]
    [DataRow(403, ThumbnailStatus.AccessDenied, true, true)]
    [DataRow(404, ThumbnailStatus.Unsupported, false, false)]
    [DataRow(404, ThumbnailStatus.Unsupported, false, true)]
    [DataRow(409, ThumbnailStatus.Unavailable, false, false)]
    [DataRow(409, ThumbnailStatus.Unavailable, false, true)]
    [DataRow(503, ThumbnailStatus.Unavailable, false, false)]
    [DataRow(503, ThumbnailStatus.Unavailable, false, true)]
    public async Task ImageErrorsKeepOptionalFallbackSeparateFromIdentityLoss(int code, ThumbnailStatus expected, bool revoked, bool preview)
    {
        var imageCalls = 0;
        using var transport = ThumbnailTestSupport.Transport((request, _) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/image", StringComparison.Ordinal)) { return ThumbnailTestSupport.PageAsync(request); }
            ++imageCalls; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code));
        });
        await using var store = new SnapshotStore(new ReadOnlyClient(transport), DateTimeOffset.UtcNow.AddHours(1));
        var request = await ThumbnailTestSupport.FileAsync(store);
        await using var thumbnails = new ThumbnailSession(transport, store, UnexpectedDecode, UnexpectedDecode);
        var response = await ThumbnailTestSupport.ReadImageAsync(thumbnails, request, preview, CancellationToken.None);
        Assert.AreEqual(expected, response.Status);
        Assert.AreEqual(preview ? request.Epoch : store.CurrentEpoch, response.Epoch);
        Assert.AreEqual(request.Node, response.Node);
        Assert.AreEqual(revoked, store.IsRevoked); Assert.AreEqual(1, imageCalls);
        var old = request with { Epoch = Guid.NewGuid() };
        Assert.AreEqual(ThumbnailStatus.Expired, (await ThumbnailTestSupport.ReadImageAsync(thumbnails, old, preview, CancellationToken.None)).Status);
        Assert.AreEqual(1, imageCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LateDecodeCannotPublishAfterEpochRevocation(bool preview)
    {
        using var transport = ThumbnailTestSupport.SuccessfulTransport();
        await using var store = new SnapshotStore(new ReadOnlyClient(transport), DateTimeOffset.UtcNow.AddHours(1));
        var request = await ThumbnailTestSupport.FileAsync(store);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ThumbnailPixels> Decode(byte[] _, CancellationToken cancellation)
        {
            entered.TrySetResult(); await release.Task;
            return new ThumbnailPixels(2, 2, ThumbnailTestSupport.ExpectedPixels);
        }
        await using var thumbnails = new ThumbnailSession(transport, store, Decode, Decode);
        var read = ThumbnailTestSupport.ReadImageAsync(thumbnails, request, preview, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        store.RevokeSession(); release.TrySetResult();
        var result = await read;
        Assert.AreEqual(ThumbnailStatus.Expired, result.Status); Assert.IsNull(result.Image);
        Assert.AreEqual(preview ? request.Epoch : store.CurrentEpoch, result.Epoch);
    }
    private static Task<ThumbnailPixels> UnexpectedDecode(byte[] bytes, CancellationToken token) =>
        throw new InvalidOperationException("Error must not decode.");
}
