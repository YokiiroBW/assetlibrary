using System.Net;
using System.Text;
using System.Text.Json;
using AssetLibrary.CoreServer.Hosting;

namespace AssetLibrary.Packaging.Tests;

internal static class TrialAuthenticationRequests
{
    public static async Task<(string Cookie, string Csrf)> LoginAsync(this TrialAuthenticationTestHost host)
    {
        using var response = await host.SendAsync(HttpMethod.Post, "/assetlink/v1/auth/login",
            body: JsonSerializer.Serialize(new { account_name = "trial-admin", password = TrialAuthenticationStore.Password }));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (cookie, body.RootElement.GetProperty("csrf_token").GetString()!);
    }

    public static Task<HttpResponseMessage> SendAsync(this TrialAuthenticationTestHost host,
        HttpMethod method, string path, string? cookie = null, string? csrf = null,
        string? body = null, string? origin = "default", string? site = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin == "default"
                ? host.Origin.GetLeftPart(UriPartial.Authority) : origin);
        }

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        if (csrf is not null)
        {
            request.Headers.TryAddWithoutValidation(TrialRequestTrust.CsrfHeader, csrf);
        }

        if (site is not null)
        {
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return SendOwnedAsync(host.Client, request);
    }

    private static async Task<HttpResponseMessage> SendOwnedAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request)
        {
            return await client.SendAsync(request);
        }
    }
}
