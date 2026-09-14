using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace AssetLibrary.WebGateway.Tests;

internal static class ServiceReadIntegrationIsolation
{
    public static async Task AssertAsync(TrialHostIntegrationFixture host, string token,
        TrialHostIntegrationSession administrator, Guid library, string cursor, TrialHostIntegrationSettings settings)
    {
        var root = Directory.CreateDirectory(Path.Combine(settings.RuntimeRoot, "assets", "second")).FullName;
        var registered = await TrialHostIntegrationHttp.ControlAsync(host, administrator, "libraries.register", new JsonObject
        {
            ["root_path"] = root,
            ["display_name"] = "TS063 second isolation library",
            ["source_key"] = "fixtures",
        }, Guid.NewGuid());
        Assert.AreEqual(200, registered.Status);
        var secondLibrary = registered.Payload["body"]!["library_id"]!.GetValue<Guid>();
        var principal = Guid.NewGuid();
        await ServiceReadIntegrationHttp.ManageAsync(host, "service-create", principal, new JsonObject { ["display_name"] = "TS063 isolated second service" });
        var failure = await ServiceReadIntegrationHttp.ManageAsync(host, "service-grant", principal,
            ServiceReadIntegrationHttp.Libraries(secondLibrary, Guid.NewGuid()), (int)CoreServerExitCode.InvalidConfiguration);
        Assert.IsFalse(failure.ContainsKey("token"));
        var issued = await ServiceReadIntegrationHttp.ManageAsync(host, "service-issue", principal);
        var secondToken = issued["token"]!.GetValue<string>();
        Assert.IsEmpty((await ServiceReadIntegrationHttp.ReadAsync(host, secondToken, "libraries.list", new JsonObject()))["items"]!.AsArray());
        await ServiceReadIntegrationHttp.ManageAsync(host, "service-grant", principal, ServiceReadIntegrationHttp.Libraries(secondLibrary));
        var visible = await ServiceReadIntegrationHttp.ReadAsync(host, secondToken, "libraries.list", new JsonObject());
        Assert.HasCount(1, visible["items"]!.AsArray());
        Assert.AreEqual(secondLibrary, visible["items"]![0]!["library_id"]!.GetValue<Guid>());
        await ServiceReadIntegrationHttp.ReadAsync(host, secondToken, "libraries.get", ServiceReadIntegrationHttp.Library(library), 404);
        await ServiceReadIntegrationHttp.ReadAsync(host, token, "libraries.get", ServiceReadIntegrationHttp.Library(secondLibrary), 404);
        await ServiceReadIntegrationHttp.ManageAsync(host, "service-grant", principal, ServiceReadIntegrationHttp.Libraries(library));
        await ServiceReadIntegrationHttp.ReadAsync(host, secondToken, "entries.browse", ServiceReadIntegrationHttp.Browse(secondLibrary, cursor), 400);
        // Public application API accepts sub-day lifetimes; test real database expiry without changing machine time.
        using var expiring = await host.Services.GetRequiredService<ServiceReadAuthenticationService>().IssueAsync(
            principal, null, TimeSpan.FromSeconds(2), new ServiceReadOperation("TS063-expiry-fixture", Guid.NewGuid()), CancellationToken.None);
        Assert.IsNotNull(expiring);
        await ServiceReadIntegrationHttp.ReadAsync(host, expiring.Token.Export(), "libraries.list", new JsonObject());
        await Task.Delay(TimeSpan.FromSeconds(3));
        await ServiceReadIntegrationHttp.ReadAsync(host, expiring.Token.Export(), "libraries.list", new JsonObject(), 401);
        await host.RestartAsync();
        await ServiceReadIntegrationHttp.ReadAsync(host, expiring.Token.Export(), "libraries.list", new JsonObject(), 401);
    }
}
