using System.Text.Json.Nodes;
using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationAuthorization
{
    public static async Task AssertBoundariesAsync(TrialHostIntegrationFixture host,
        TrialHostIntegrationSession administrator, Guid libraryId)
    {
        var wrongOrigin = await TrialHostIntegrationHttp.SendAsync(host, administrator, HttpMethod.Post,
            "/assetlink/v1/control", "{}", origin: "https://attacker.invalid");
        Assert.AreEqual(403, wrongOrigin.Status);
        var wrongCsrf = await TrialHostIntegrationHttp.SendAsync(host, administrator, HttpMethod.Post,
            "/assetlink/v1/control", "{}", csrf: new string('a', 43));
        Assert.AreEqual(403, wrongCsrf.Status);
        await TrialHostIntegrationAccounts.ProvisionReaderAsync(host, administrator);
        var reader = await TrialHostIntegrationHttp.SignInAsync(host, "trial-reader");
        var management = await TrialHostIntegrationHttp.ControlAsync(host, reader, "storage_sources.list", new JsonObject());
        Assert.AreEqual(403, management.Status);
        var hidden = await TrialHostIntegrationHttp.ControlAsync(host, reader, "entries.browse", new JsonObject
        {
            ["library_id"] = libraryId.ToString("D"),
            ["parent_relative_path"] = "",
        });
        Assert.AreEqual(404, hidden.Status);
        Assert.IsFalse(hidden.Payload.ToJsonString().Contains("真实只读试用", StringComparison.Ordinal));
        var libraries = await TrialHostIntegrationHttp.ControlAsync(host, reader, "libraries.list", new JsonObject());
        Assert.AreEqual(200, libraries.Status);
        Assert.IsEmpty(libraries.Payload["body"]!["items"]!.AsArray());
    }

}

internal static class TrialHostIntegrationAccounts
{
    public static async Task ProvisionReaderAsync(TrialHostIntegrationFixture host, TrialHostIntegrationSession session)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = session.Cookie;
        using var ticket = host.Services.GetRequiredService<TrialBrowserCookieCodec>().Read(context.Request)!;
        var authenticated = await host.Runtime.AuthenticateMutationAsync(ticket.SessionToken, ticket.CsrfToken, CancellationToken.None);
        Assert.IsNotNull(authenticated.Identity);
        var store = new PostgresAuthenticationStore(host.Services.GetRequiredService<NpgsqlDataSource>());
        using var risk = host.Authentication.Risk.Checker();
        var lifecycle = new LocalAccountLifecycleService(store, risk);
        using var secret = new LocalSecret(TrialHostIntegrationAuthentication.Password);
        var result = await lifecycle.ProvisionAsync(authenticated.Identity, Guid.NewGuid(), new LocalAccountName("trial-reader"),
            new LocalAccountDisplayName("隔离只读用户"), false, secret, CancellationToken.None);
        Assert.AreEqual(LocalAccountLifecycleOutcome.Applied, result.Outcome);
    }
}
