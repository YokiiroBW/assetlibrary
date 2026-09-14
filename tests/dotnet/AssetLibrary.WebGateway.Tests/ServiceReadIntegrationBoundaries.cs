using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace AssetLibrary.WebGateway.Tests;

internal static class ServiceReadIntegrationBoundaries
{
    public static async Task AssertAsync(TrialHostIntegrationFixture host, string token,
        TrialHostIntegrationSession administrator, Guid library, string cursor)
    {
        foreach (var (header, value) in new[]
        {
            ("Cookie", administrator.Cookie), ("Origin", host.Configuration.PublicOrigin),
            ("Sec-Fetch-Site", "same-origin"), ("Sec-Fetch-Mode", "cors"),
            ("Authorization", "Bearer " + token),
        })
        {
            var response = await ServiceReadIntegrationHttp.SendAsync(host, token, "libraries.list", new JsonObject(), header, value);
            Assert.IsTrue(response.Status is 400 or 401 or 403, "Mixed authentication must fail before dispatch: " + header);
        }
        var unknown = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await ServiceReadIntegrationHttp.ReadAsync(host, unknown, "libraries.list", new JsonObject(), 401);
        Assert.AreEqual(403, (await ServiceReadIntegrationHttp.SendAsync(host, null, "libraries.list", new JsonObject())).Status);
        foreach (var operation in new[] { "libraries.register", "libraries.update_category", "library_scans.start",
            "library_scans.cancel", "library_scans.get", "storage_sources.list", "preview.get", "organization.execute" })
        {
            var response = await ServiceReadIntegrationHttp.SendAsync(host, token, operation, ServiceReadIntegrationHttp.Library(library));
            Assert.IsTrue(response.Status is 400 or 403, "Service operation allowlist: " + operation);
        }
        var page = ServiceReadIntegrationHttp.Browse(library, cursor);
        page["sort_direction"] = "desc";
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "entries.browse", page, 400);
        page = ServiceReadIntegrationHttp.Browse(library);
        page["page_size"] = 101;
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "entries.browse", page, 400);
        var missing = await ServiceReadIntegrationHttp.ReadAsync(host, token, "libraries.get", ServiceReadIntegrationHttp.Library(Guid.NewGuid()), 404);
        var reader = await TrialHostIntegrationHttp.SignInAsync(host, "trial-reader");
        var hidden = await TrialHostIntegrationHttp.ControlAsync(host, reader, "libraries.get", ServiceReadIntegrationHttp.Library(library));
        Assert.AreEqual(404, hidden.Status);
        Assert.AreEqual(missing["code"]!.GetValue<string>(), hidden.Payload["error"]!["code"]!.GetValue<string>());
        Assert.AreEqual(403, (await TrialHostIntegrationHttp.SendAsync(host, administrator, HttpMethod.Post,
            "/assetlink/v1/control", "{}", origin: "https://attacker.invalid")).Status);
        Assert.AreEqual(403, (await TrialHostIntegrationHttp.SendAsync(host, administrator, HttpMethod.Post,
            "/assetlink/v1/control", "{}", csrf: new string('a', 43))).Status);
        Assert.AreEqual(200, (await TrialHostIntegrationHttp.ControlAsync(host, administrator, "libraries.list", new JsonObject())).Status);
    }
}
