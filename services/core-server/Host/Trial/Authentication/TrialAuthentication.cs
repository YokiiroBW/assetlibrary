using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace AssetLibrary.CoreServer.Hosting;

internal static class TrialAuthentication
{
    public static void Configure(
        IServiceCollection services,
        GatewayAuthenticationRuntime runtime,
        IDataProtectionProvider protection,
        Uri publicOrigin)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(runtime);
        services.AddSingleton(new TrialAuthenticationServices(runtime.LocalAuthentication, runtime.BrowserSessions));
        services.AddSingleton(new TrialBrowserCookieCodec(protection, TimeProvider.System));
        services.AddSingleton(new TrialRequestTrust(publicOrigin));
        services.AddSingleton<TrialLoginLimiter>();
    }

    public static void Use(WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.UseMiddleware<TrialAuthenticationMiddleware>();
    }

    public static void Map(WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.MapPost("/assetlink/v1/auth/login", TrialAuthenticationEndpoints.LoginAsync);
        application.MapGet("/assetlink/v1/auth/session", TrialAuthenticationEndpoints.Session);
        application.MapPost("/assetlink/v1/auth/logout", TrialAuthenticationEndpoints.LogoutAsync);
    }

    public static AuthenticatedIdentity? CurrentIdentity(HttpContext context) =>
        TrialAuthenticatedRequest.Get(context)?.Identity;

    public static bool TryGetCurrentAdministrator(HttpContext context, out AuthenticatedIdentity? identity)
    {
        var current = CurrentIdentity(context);
        identity = CurrentAdministratorPolicy.Allows(current) ? current : null;
        return identity is not null;
    }
}

internal sealed record TrialAuthenticationServices(
    LocalAuthenticationService LocalAuthentication,
    BrowserSessionService BrowserSessions);
