using System.Text.Json.Nodes;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class ServiceReadIntegrationTests
{
    [TestMethod]
    public async Task RealHttpsServiceReadsReauthorizeEveryPageAndPersistCredentialLifecycle()
    {
        var settings = TrialHostIntegrationSettings.Load();
        var assets = new NativeClientSampleAssets(settings.RuntimeRoot);
        await using var host = await TrialHostIntegrationFixture.CreateAsync(settings, serviceReadEnabled: true);
        var library = await NativeClientFixtureSeed.PrepareAsync(host, assets);
        var administrator = await TrialHostIntegrationHttp.SignInAsync(host);
        var principal = Guid.NewGuid();
        await ServiceReadIntegrationHttp.ManageAsync(host, "service-create", principal, new JsonObject { ["display_name"] = "TS063 合成服务" });
        await ServiceReadIntegrationHttp.ManageAsync(host, "service-grant", principal, ServiceReadIntegrationHttp.Libraries(library));
        var issued = await ServiceReadIntegrationHttp.ManageAsync(host, "service-issue", principal);
        var token = issued["token"]!.GetValue<string>();
        var credential = issued["credential_id"]!.GetValue<string>();
        var expires = DateTimeOffset.Parse(issued["expires_at"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsTrue(expires > DateTimeOffset.UtcNow.AddDays(29) && expires <= DateTimeOffset.UtcNow.AddDays(30));
        var libraries = await ServiceReadIntegrationHttp.ReadAsync(host, token, "libraries.list", new JsonObject());
        Assert.HasCount(1, libraries["items"]!.AsArray());
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "libraries.get", ServiceReadIntegrationHttp.Library(library));
        var first = await ServiceReadIntegrationHttp.ReadAsync(host, token, "entries.browse", ServiceReadIntegrationHttp.Browse(library));
        Assert.HasCount(100, first["items"]!.AsArray());
        var cursor = first["next_cursor"]!.GetValue<string>();
        var second = await ServiceReadIntegrationHttp.ReadAsync(host, token, "entries.browse", ServiceReadIntegrationHttp.Browse(library, cursor));
        Assert.HasCount(37, second["items"]!.AsArray());
        var ids = first["items"]!.AsArray().Concat(second["items"]!.AsArray()).Select(item => item!["entry_id"]!.GetValue<string>()).ToArray();
        Assert.AreEqual(137, ids.Distinct().Count());
        var detail = ServiceReadIntegrationHttp.Library(library);
        detail["entry_id"] = ids[0];
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "entries.get", detail);
        Assert.HasCount(1, (await ServiceReadIntegrationHttp.ReadAsync(host, token, "assets.search", ServiceReadIntegrationHttp.Search(library)))["items"]!.AsArray());
        await ServiceReadIntegrationBoundaries.AssertAsync(host, token, administrator, library, cursor);
        await ServiceReadIntegrationIsolation.AssertAsync(host, token, administrator, library, cursor, settings);
        await host.RestartAsync();
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "libraries.get", ServiceReadIntegrationHttp.Library(library));
        Assert.AreEqual(200, (await TrialHostIntegrationHttp.SendAsync(host, administrator, HttpMethod.Get, "/assetlink/v1/auth/session")).Status);
        await ServiceReadIntegrationHttp.ManageAsync(host, "service-ungrant", principal, ServiceReadIntegrationHttp.Libraries(library));
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "entries.browse", ServiceReadIntegrationHttp.Browse(library, cursor), 404);
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "entries.get", detail, 404);
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "libraries.get", ServiceReadIntegrationHttp.Library(library), 404);
        Assert.IsEmpty((await ServiceReadIntegrationHttp.ReadAsync(host, token, "libraries.list", new JsonObject()))["items"]!.AsArray());
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "assets.search", ServiceReadIntegrationHttp.Search(library), 404);
        await ServiceReadIntegrationHttp.ManageAsync(host, "service-grant", principal, ServiceReadIntegrationHttp.Libraries(library));
        var rotated = await ServiceReadIntegrationHttp.ManageAsync(host, "service-rotate", principal, new JsonObject { ["credential_id"] = credential });
        var replacement = rotated["token"]!.GetValue<string>();
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "libraries.list", new JsonObject(), 401);
        await ServiceReadIntegrationHttp.ReadAsync(host, replacement, "libraries.get", ServiceReadIntegrationHttp.Library(library));
        await ServiceReadIntegrationHttp.ManageAsync(host, "service-revoke", principal, new JsonObject { ["credential_id"] = rotated["credential_id"]!.DeepClone() });
        await host.RestartAsync();
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "libraries.list", new JsonObject(), 401);
        await ServiceReadIntegrationHttp.ReadAsync(host, replacement, "libraries.list", new JsonObject(), 401);
        var final = await ServiceReadIntegrationHttp.ManageAsync(host, "service-issue", principal);
        await ServiceReadIntegrationHttp.ManageAsync(host, "service-disable", principal);
        await host.RestartAsync();
        await ServiceReadIntegrationHttp.ReadAsync(host, final["token"]!.GetValue<string>(), "libraries.list", new JsonObject(), 401);
        assets.VerifyUnchanged();
    }
}
