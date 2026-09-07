using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AssetLibrary.WebGateway.Tests;

internal sealed class TrialHostIntegrationAuthentication(GatewayAuthorizationConfiguration configuration) : IDisposable
{
    public const string Password = "V01-020 isolated administrator passphrase";
    public TrialHostIntegrationRisk Risk { get; } = new();

    public GatewayAuthenticationRuntime Create(NpgsqlDataSource database, IDataProtectionProvider protection, ILoggerFactory logging)
    {
        var store = new PostgresAuthenticationStore(database);
        var keys = new ProtectedGatewayAuthorizationKeyStore(configuration, protection, TimeProvider.System);
        var authorization = new FileOutOfBandAuthorization(keys, configuration.DeploymentId, TimeProvider.System);
        var risk = Risk.Checker();
        var issuer = new BrowserSessionIssuer(store, logging.CreateLogger<BrowserSessionIssuer>());
        var local = new LocalAuthenticationService(store, issuer, logging.CreateLogger<LocalAuthenticationService>());
        var sessions = new BrowserSessionService(store, logging.CreateLogger<BrowserSessionService>());
        var administrators = new AdministratorBootstrapRecoveryService(store, authorization, risk,
            logging.CreateLogger<AdministratorBootstrapRecoveryService>(),
            new AdministratorRecoveryMutation(store, new PostgresAdministratorRecoveryPreparationStore(database)));
        return new GatewayAuthenticationRuntime(local, sessions, administrators, authorization, keys, risk);
    }

    public void Dispose() => Risk.Dispose();
}

internal sealed class TrialHostIntegrationRisk : IDisposable
{
    private readonly List<HttpClient> clients = [];

    public bool Available { get; set; } = true;

    public PwnedPasswordsSecretRiskChecker Checker()
    {
        var handler = new RecordingHttpMessageHandler((request, token) =>
        {
            token.ThrowIfCancellationRequested();
            Assert.AreEqual("api.pwnedpasswords.com", request.RequestUri!.Host);
            return Task.FromResult(PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse(TrialHostIntegrationAuthentication.Password),
                Available ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.ServiceUnavailable));
        });
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        clients.Add(client);
        return new PwnedPasswordsSecretRiskChecker(client, new PwnedPasswordsSecretRiskOptions());
    }

    public void Dispose()
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }
    }
}
