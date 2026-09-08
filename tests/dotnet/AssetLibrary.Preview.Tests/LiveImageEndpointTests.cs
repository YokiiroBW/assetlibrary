using System.Buffers.Binary;
using System.Net;
using AssetLibrary.ImagePreview.Protocol;

namespace AssetLibrary.Preview.Tests;

[TestClass]
public sealed class LiveImageEndpointTests
{
    [TestMethod]
    public async Task RealHttpsImageTrustAndInvisibleEntryRulesPreserveTheJsonContract()
    {
        using var client = await LivePreviewConnection.OpenAsync();
        using (var anonymous = await client.SendAsync(client.ImagePath(Guid.NewGuid())))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.AreEqual("unauthenticated", await LivePreviewConnection.ErrorCodeAsync(anonymous));
        }
        using (var jsonSession = await client.SendAsync("/assetlink/v1/auth/session"))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, jsonSession.StatusCode);
            Assert.AreEqual("authentication_required", await LivePreviewConnection.ErrorCodeAsync(jsonSession));
        }
        await client.SignInAsync();
        var images = await client.ImageEntriesAsync();
        var entry = images["landscape.png"];
        foreach (var query in new[] { "", "variant=other", "variant=thumbnail&variant=preview", "variant=thumbnail&path=other" })
        {
            using var rejected = await client.SendAsync(client.ImagePath(entry, query));
            Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using (var trust = await client.SendAsync(client.ImagePath(entry), origin: "https://untrusted.invalid"))
            Assert.AreEqual(HttpStatusCode.Forbidden, trust.StatusCode);
        using (var csrf = await client.SendAsync(client.ImagePath(entry), HttpMethod.Post))
            Assert.AreEqual(HttpStatusCode.Forbidden, csrf.StatusCode);
        using (var missing = await client.SendAsync(client.ImagePath(Guid.NewGuid())))
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        using var reader = await LivePreviewConnection.OpenAsync();
        await reader.SignInAsync(invisible: true);
        using var invisible = await reader.SendAsync(reader.ImagePath(entry));
        Assert.AreEqual(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.AreEqual("not_found", await LivePreviewConnection.ErrorCodeAsync(invisible));
    }

    [TestMethod]
    public async Task RealCoreReturnsTheDeclaredEngineStateWithoutOriginalFallback()
    {
        using var client = await LivePreviewConnection.OpenAsync();
        await client.SignInAsync();
        var images = await client.ImageEntriesAsync();
        var available = Environment.GetEnvironmentVariable("ASSETLIBRARY_PREVIEW_EXPECT_AVAILABLE") == "1";
        using var response = await client.SendAsync(client.ImagePath(images["landscape.png"]));
        if (!available)
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.AreEqual("preview_unavailable", await LivePreviewConnection.ErrorCodeAsync(response));
            return;
        }
        await AssertPngAsync(response, 512, ImageWorkerProtocol.ThumbnailBytes);
        foreach (var file in new[] { "landscape.jpg", "landscape.webp", "rotate-six.jpg", "transparent.png" })
        {
            using var image = await client.SendAsync(client.ImagePath(images[file], "variant=preview"));
            await AssertPngAsync(image, 1600, ImageWorkerProtocol.PreviewBytes, file);
        }
        foreach (var item in new[] { ("active.svg", 415), ("not-an-image.png", 415), ("truncated.jpg", 422), ("oversized-header.png", 422) })
        {
            using var error = await client.SendAsync(client.ImagePath(images[item.Item1]));
            Assert.AreEqual(item.Item2, (int)error.StatusCode);
        }
    }

    private static async Task AssertPngAsync(HttpResponseMessage response, int edge, int maximumBytes, string sample = "landscape.png")
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            Assert.Fail($"Synthetic input {sample}: HTTP {(int)response.StatusCode}, code {await LivePreviewConnection.ErrorCodeAsync(response)}.");
        }
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("private, no-store", response.Headers.CacheControl?.ToString());
        Assert.IsFalse(response.Headers.Contains("ETag"));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.IsTrue(bytes.Length > 33 && bytes.Length <= maximumBytes);
        Assert.AreEqual((long)bytes.Length, response.Content.Headers.ContentLength);
        Assert.IsTrue(bytes.AsSpan().StartsWith(ImageWorkerProtocol.PngSignature));
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        Assert.IsTrue(width > 0 && width <= edge && height > 0 && height <= edge);
        Assert.AreEqual(8, bytes[24]);
    }
}
