using AssetLibrary.Modules.GatewayAuth.Application;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

public static class ServiceReadAuthenticationComposition
{
    public static ServiceReadAuthenticationService Create(NpgsqlDataSource gatewayDataSource,
        int maximumLifetimeDays = 365, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayDataSource);
        return new ServiceReadAuthenticationService(new PostgresServiceReadCredentialStore(gatewayDataSource),
            maximumLifetimeDays, timeProvider);
    }
}
