using System.Net;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class ThumbnailHttpTests
{
    [TestMethod]
    [DataRow(401, true)]
    [DataRow(403, true)]
    [DataRow(404, false)]
    [DataRow(302, false)]
    public async Task ErrorsActOnHeadersWithoutReadingOrFollowingBody(int status, bool invalidates)
    {
        var calls = 0;
        using var transport = ThumbnailTestSupport.Transport((_, _) =>
        {
            ++calls;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new UnreadableBody() });
        });
        var failure = await Assert.ThrowsExactlyAsync<ThumbnailHttpException>(() => transport.ThumbnailAsync(ThumbnailTestSupport.LibraryId, ThumbnailTestSupport.EntryId, CancellationToken.None));
        Assert.AreEqual(status, failure.Status); Assert.AreEqual(invalidates, failure.InvalidatesSession); Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow("text/html", 78)]
    [DataRow("image/png", 2097153)]
    public async Task InvalidContentTypeOrDeclaredLimitIsRejected(string type, int length)
    {
        using var transport = ThumbnailTestSupport.Transport((_, _) =>
        {
            var response = ThumbnailTestSupport.ImageResponse();
            response.Content.Headers.ContentType = new(type); response.Content.Headers.ContentLength = length;
            return Task.FromResult(response);
        });
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => transport.ThumbnailAsync(ThumbnailTestSupport.LibraryId, ThumbnailTestSupport.EntryId, CancellationToken.None));
    }

    [TestMethod]
    public async Task RateLimitRetriesAreBoundedToTwoAndUseExactOriginRoute()
    {
        var calls = 0;
        using var transport = ThumbnailTestSupport.Transport((request, _) =>
        {
            Assert.AreEqual("/assetlink/v1/libraries/" + ThumbnailTestSupport.LibraryId + "/entries/" + ThumbnailTestSupport.EntryId + "/image?variant=thumbnail", request.RequestUri!.PathAndQuery);
            Assert.AreEqual(ProtocolFixture.Profile.Origin, request.Headers.GetValues("Origin").Single());
            ++calls;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        var error = await Assert.ThrowsExactlyAsync<ThumbnailHttpException>(() => transport.ThumbnailAsync(ThumbnailTestSupport.LibraryId, ThumbnailTestSupport.EntryId, CancellationToken.None));
        Assert.AreEqual(429, error.Status); Assert.AreEqual(3, calls);
    }

    private sealed class UnreadableBody : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new InvalidOperationException("Error body must not be consumed."));
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
