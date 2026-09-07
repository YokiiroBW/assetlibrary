using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationHttp
{
    public static async Task<TrialHostIntegrationSession> SignInAsync(
        TrialHostIntegrationFixture host, string account = "trial-admin", string password = TrialHostIntegrationAuthentication.Password)
    {
        using var request = Request(host, HttpMethod.Post, "/assetlink/v1/auth/login");
        request.Content = new StringContent(JsonSerializer.Serialize(new { account_name = account, password }), Encoding.UTF8, "application/json");
        using var response = await host.Client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Real local sign-in");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new TrialHostIntegrationSession(response.Headers.GetValues("Set-Cookie").Single().Split(';')[0],
            json.RootElement.GetProperty("csrf_token").GetString()!, json.RootElement.GetProperty("principal_id").GetGuid());
    }

    public static Task<TrialHostIntegrationResponse> ControlAsync(TrialHostIntegrationFixture host,
        TrialHostIntegrationSession session, string operation, JsonObject body, Guid? idempotencyKey = null)
    {
        var envelope = new JsonObject
        {
            ["message_type"] = "control.request",
            ["request_id"] = Guid.NewGuid().ToString("D"),
            ["operation"] = operation,
            ["body"] = body,
        };
        if (idempotencyKey.HasValue) envelope["idempotency_key"] = idempotencyKey.Value.ToString("D");
        return SendAsync(host, session, HttpMethod.Post, "/assetlink/v1/control", envelope.ToJsonString());
    }

    public static async Task<TrialHostIntegrationResponse> SendAsync(TrialHostIntegrationFixture host,
        TrialHostIntegrationSession session, HttpMethod method, string path, string? json = null,
        string? origin = null, string? csrf = null)
    {
        using var request = Request(host, method, path);
        request.Headers.Add("Cookie", session.Cookie);
        request.Headers.Add("X-AssetLibrary-CSRF", csrf ?? session.Csrf);
        if (origin is not null)
        {
            request.Headers.Remove("Origin");
            request.Headers.Add("Origin", origin);
        }

        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await host.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new TrialHostIntegrationResponse((int)response.StatusCode,
            text.Length == 0 ? new JsonObject() : JsonNode.Parse(text)!.AsObject(), response.Headers.Contains("Set-Cookie"));
    }

    private static HttpRequestMessage Request(TrialHostIntegrationFixture host, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", host.Configuration.PublicOrigin);
        return request;
    }
}

internal sealed record TrialHostIntegrationSession(string Cookie, string Csrf, Guid PrincipalId)
{
    public override string ToString() => "[redacted]";
}

internal sealed record TrialHostIntegrationResponse(int Status, JsonObject Payload, bool SetsCookie);
