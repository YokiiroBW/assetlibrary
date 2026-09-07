using AssetLibrary.CoreServer.Hosting;
using AssetLibrary.Modules.GatewayAuth.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.Packaging.Tests;

internal static class TrialAuthenticationTestComposition
{
    public static LocalAuthenticationService Configure(IServiceCollection services,
        TrialAuthenticationStore store, Uri origin)
    {
        var issuer = new BrowserSessionIssuer(store, NullLogger<BrowserSessionIssuer>.Instance);
        var authentication = new LocalAuthenticationService(store, issuer, NullLogger<LocalAuthenticationService>.Instance);
        services.AddSingleton(new TrialAuthenticationServices(authentication,
            new BrowserSessionService(store, NullLogger<BrowserSessionService>.Instance)));
        services.AddSingleton(new TrialBrowserCookieCodec(new EphemeralDataProtectionProvider(), TimeProvider.System));
        services.AddSingleton(new TrialRequestTrust(origin));
        services.AddSingleton<TrialLoginLimiter>();
        return authentication;
    }
}
