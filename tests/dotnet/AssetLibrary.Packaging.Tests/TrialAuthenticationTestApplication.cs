using System.Net;
using System.Security.Cryptography.X509Certificates;
using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.Modules.GatewayAuth.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.Packaging.Tests;

internal static class TrialAuthenticationTestApplication
{
    public static WebApplication Build(Uri origin, X509Certificate2 certificate, TrialAuthenticationStore store,
        out LocalAuthenticationService authentication)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
            server.Listen(IPAddress.Loopback, origin.Port, endpoint => endpoint.UseHttps(certificate)));
        authentication = TrialAuthenticationTestComposition.Configure(builder.Services, store, origin);
        var application = builder.Build();
        TrialAuthentication.Use(application);
        TrialAuthentication.Map(application);
        MapAssertions(application);
        return application;
    }

    private static void MapAssertions(WebApplication application)
    {
        application.MapPost("/assetlink/v1/control", (HttpContext context) => Results.Json(new
        {
            principal_id = TrialAuthentication.CurrentIdentity(context)!.PrincipalId,
        }));
        application.MapPost("/assetlink/v1/test-admin", (HttpContext context) =>
            TrialAuthentication.TryGetCurrentAdministrator(context, out var identity)
                ? Results.Json(new { principal_id = identity!.PrincipalId })
                : TrialAuthenticationResponses.Forbidden());
    }
}
