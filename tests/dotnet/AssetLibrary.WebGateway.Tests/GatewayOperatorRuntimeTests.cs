using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class GatewayOperatorRuntimeTests
{
    [TestMethod]
    public async Task ExpiredOperatorRequestIsRejectedBeforeKeyDatabaseOrRiskDependencies()
    {
        using var sandbox = new GatewayAuthorizationSandbox();
        await using var database = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1");
        using var runtime = GatewayAuthenticationComposition.Create(
            database, sandbox.Protection(), sandbox.Configuration, NullLoggerFactory.Instance);
        var request = new AdministratorOperatorRequest(Guid.NewGuid(), Guid.NewGuid(),
            AdministratorBootstrapRecoveryAction.BootstrapFirstAdministrator, new LocalAccountName("bootstrap-admin"),
            new LocalAccountDisplayName("Trial administrator"), DateTimeOffset.UtcNow.AddSeconds(-1));
        using var secret = new LocalSecret("expired request should not reach dependencies");
        var result = await runtime.BootstrapAdministratorAsync(request, secret, CancellationToken.None);
        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.AuthorizationRejected, result.Outcome);
        Assert.IsFalse(File.Exists(sandbox.Configuration.KeyFilePath));
    }
}
