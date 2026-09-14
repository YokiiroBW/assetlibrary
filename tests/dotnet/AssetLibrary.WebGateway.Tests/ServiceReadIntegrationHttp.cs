using System.Text;
using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.WebGateway.Tests;

internal static class ServiceReadIntegrationHttp
{
    public static async Task<JsonObject> ManageAsync(TrialHostIntegrationFixture host, string action,
        Guid principal, JsonObject? fields = null, int expectedExit = 0)
    {
        var body = fields?.DeepClone().AsObject() ?? new JsonObject();
        body["principal_id"] = principal.ToString("D");
        body["operation_id"] = Guid.NewGuid().ToString("D");
        body["authorization_id"] = Guid.NewGuid().ToString("D");
        body["operator_id"] = "TS063-synthetic-operator";
        body["expires_at"] = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(body.ToJsonString()));
        var result = await ServiceReadOperator.ExecuteAsync(action, host.Services, input, CancellationToken.None);
        Assert.AreEqual(expectedExit, result.ExitCode, "Synthetic service management outcome: " + action);
        return JsonNode.Parse(result.Json)!.AsObject();
    }

    public static JsonObject Libraries(params Guid[] libraries) => new()
    {
        ["library_ids"] = new JsonArray(libraries.Select(value => (JsonNode?)JsonValue.Create(value.ToString("D"))).ToArray()),
    };

    public static async Task<TrialHostIntegrationResponse> SendAsync(TrialHostIntegrationFixture host, string? token,
        string operation, JsonObject body, string? mixedHeader = null, string? mixedValue = null,
        string path = "/assetlink/v1/control")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (token is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        if (mixedHeader is not null) request.Headers.TryAddWithoutValidation(mixedHeader, mixedValue);
        request.Content = new StringContent(new JsonObject
        {
            ["message_type"] = "control.request",
            ["request_id"] = Guid.NewGuid().ToString("D"),
            ["operation"] = operation,
            ["body"] = body.DeepClone(),
        }.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await host.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsLessThanOrEqualTo(1024 * 1024, Encoding.UTF8.GetByteCount(text), "Fixture consumer response budget.");
        return new TrialHostIntegrationResponse((int)response.StatusCode,
            text.Length == 0 ? new JsonObject() : JsonNode.Parse(text)!.AsObject(), response.Headers.Contains("Set-Cookie"));
    }

    public static async Task<JsonObject> ReadAsync(TrialHostIntegrationFixture host, string token,
        string operation, JsonObject body, int expectedStatus = 200)
    {
        var result = await SendAsync(host, token, operation, body);
        Assert.AreEqual(expectedStatus, result.Status, operation);
        Assert.IsFalse(result.SetsCookie, "Service identity must never issue a browser cookie.");
        return (expectedStatus == 200 ? result.Payload["body"] : result.Payload["error"])!.AsObject();
    }

    public static JsonObject Library(Guid library) => new() { ["library_id"] = library.ToString("D") };
    public static JsonObject Browse(Guid library, string? cursor = null) => new()
    {
        ["library_id"] = library.ToString("D"),
        ["parent_relative_path"] = "",
        ["kind"] = "files",
        ["page_size"] = 100,
        ["cursor"] = cursor,
    };
    public static JsonObject Search(Guid library) => new()
    {
        ["scope"] = "library",
        ["library_id"] = library.ToString("D"),
        ["query"] = "说明_中文",
        ["page_size"] = 100,
    };
}
