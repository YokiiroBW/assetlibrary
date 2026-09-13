using System.Net;
using AssetLibrary.Windows.Client;

namespace AssetLibrary.Windows.Tests;

[TestClass]
public sealed class PreviewHttpTests
{
    [TestMethod]
    public async Task PreviewUsesExistingAuthorizedRouteAndFullEncodedBudget()
    {
        var body = new byte[12582912]; body[0] = 137;
        using var transport = ThumbnailTestSupport.Transport((request, _) =>
        {
            Assert.AreEqual($"/assetlink/v1/libraries/{ThumbnailTestSupport.LibraryId}/entries/{ThumbnailTestSupport.EntryId}/image?variant=preview", request.RequestUri!.PathAndQuery);
            Assert.AreEqual(ProtocolFixture.Profile.Origin, request.Headers.GetValues("Origin").Single());
            return Task.FromResult(Response(body, body.Length));
        });
        CollectionAssert.AreEqual(body, await transport.PreviewAsync(ThumbnailTestSupport.LibraryId, ThumbnailTestSupport.EntryId, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(12582913, "image/png", false)]
    [DataRow(78, "image/jpeg", false)]
    [DataRow(78, "image/png", true)]
    public async Task InvalidHeadersRejectBeforeImageBytes(int length, string type, bool compressed)
    {
        using var transport = ThumbnailTestSupport.Transport((_, _) =>
        {
            var response = Response([], length); response.Content.Headers.ContentType = new(type);
            if (compressed) { response.Content.Headers.ContentEncoding.Add("gzip"); }
            return Task.FromResult(response);
        });
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => transport.PreviewAsync(ThumbnailTestSupport.LibraryId, ThumbnailTestSupport.EntryId, CancellationToken.None));
    }

    [TestMethod]
    public async Task TruncatedBodyAndCancellationReleaseTheSharedImagePermit()
    {
        var calls = 0;
        using var transport = ThumbnailTestSupport.Transport(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { return Response([], 78); }
            await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException();
        });
        await Assert.ThrowsExactlyAsync<EndOfStreamException>(() => transport.PreviewAsync(ThumbnailTestSupport.LibraryId, ThumbnailTestSupport.EntryId, CancellationToken.None));
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAsync<OperationCanceledException>(() => transport.PreviewAsync(ThumbnailTestSupport.LibraryId, ThumbnailTestSupport.EntryId, stop.Token));
        Assert.AreEqual(2, calls);
    }

    private static HttpResponseMessage Response(byte[] bytes, int length)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new("image/png"); response.Content.Headers.ContentLength = length;
        return response;
    }
}
