using AssetLibrary.Modules.GatewayAuth.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

public static class GatewayAuthenticationComposition
{
    public static GatewayAuthenticationRuntime Create(
        NpgsqlDataSource gatewayDataSource,
        IDataProtectionProvider protection,
        GatewayAuthorizationConfiguration authorization,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(gatewayDataSource);
        ArgumentNullException.ThrowIfNull(protection);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        var store = new PostgresAuthenticationStore(gatewayDataSource);
        var keys = new ProtectedGatewayAuthorizationKeyStore(authorization, protection, TimeProvider.System);
        var outOfBand = new FileOutOfBandAuthorization(keys, authorization.DeploymentId, TimeProvider.System);
        var risk = new PwnedPasswordsSecretRiskChecker();
        var issuer = new BrowserSessionIssuer(store, loggerFactory.CreateLogger<BrowserSessionIssuer>());
        var authentication = new LocalAuthenticationService(
            store, issuer, loggerFactory.CreateLogger<LocalAuthenticationService>());
        var sessions = new BrowserSessionService(store, loggerFactory.CreateLogger<BrowserSessionService>());
        var administrators = new AdministratorBootstrapRecoveryService(
            store, outOfBand, risk, loggerFactory.CreateLogger<AdministratorBootstrapRecoveryService>(),
            new AdministratorRecoveryMutation(store, new PostgresAdministratorRecoveryPreparationStore(gatewayDataSource)));
        return new GatewayAuthenticationRuntime(authentication, sessions, administrators, outOfBand, keys, risk);
    }
}
