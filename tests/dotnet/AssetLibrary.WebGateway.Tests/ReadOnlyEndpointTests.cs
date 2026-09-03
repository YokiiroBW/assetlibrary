using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Adapters.AssetLink;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class OversizeEndpointTests
{
    [TestMethod]
    public async Task RejectsDeclaredAndStreamedOversizeBodies()
    {
        await using var host = await EndpointTestHost.StartAsync();
        using var declared = new ByteArrayContent(
            new byte[ReadOnlyAssetLinkEndpoints.MaximumRequestBytes + 1]);
        declared.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await host.Client.PostAsync("/assetlink/v1/control", declared);

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.AreEqual("request_too_large", await ErrorCodeAsync(response));

        using var streamed = new StreamContent(
            new MemoryStream(new byte[ReadOnlyAssetLinkEndpoints.MaximumRequestBytes + 1]));
        streamed.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        streamed.Headers.ContentLength = null;
        response = await host.Client.PostAsync("/assetlink/v1/control", streamed);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.AreEqual("request_too_large", await ErrorCodeAsync(response));
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        return json!["error"]!["code"]!.GetValue<string>();
    }
}

[TestClass]
public sealed class Utf8EndpointTests
{
    [TestMethod]
    public async Task RejectsInvalidUtf8AndKeepsJsonTransportShape()
    {
        await using var host = await EndpointTestHost.StartAsync();
        using var content = new ByteArrayContent([0xff]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await host.Client.PostAsync("/assetlink/v1/control", content);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("invalid_request", json!["error"]!["code"]!.GetValue<string>());
    }
}

[TestClass]
public sealed class MediaTypeEndpointTests
{
    [TestMethod]
    public async Task RejectsNonJsonRequestBodies()
    {
        await using var host = await EndpointTestHost.StartAsync();
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "text/plain");

        var response = await host.Client.PostAsync("/assetlink/v1/control", content);

        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.AreEqual("unsupported_media_type", await ErrorCodeAsync(response));
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        return json!["error"]!["code"]!.GetValue<string>();
    }
}
