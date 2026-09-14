using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using AssetLibrary.Modules.LibraryStorage.Application;
using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.LibraryStorage.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace AssetLibrary.CoreServer.Hosting.Trial;

internal static class TrialServiceReadComposition
{
    public static void Configure(IServiceCollection services, TrialConfiguration configuration, TrialDatabaseConnections connections)
    {
        services.AddSingleton(ServiceReadAuthenticationComposition.Create(connections.Gateway, configuration.ServiceReadMaximumLifetimeDays));
        services.AddSingleton<ServiceReadAuthenticationHandler>();
        services.AddSingleton(provider => ServiceReadOperatorComposition.Create(
            new GatewayAuthorizationConfiguration(configuration.AuthorizationKeyFile, configuration.DeploymentId),
            provider.GetRequiredService<IDataProtectionProvider>()));
        services.AddSingleton<IServiceLibraryReadGrantManagement>(new ServiceLibraryReadGrantService(
            new PostgresServiceLibraryReadGrantStore(connections.Library), TimeProvider.System));
    }
}
