using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class PostgresAdministratorBootstrapRecoveryStoreIntegrationTests
{
    [TestMethod]
    public async Task PostgresStoreCompletesAdministratorBootstrapAndRecoveryRoundTrip()
    {
        var connection = Environment.GetEnvironmentVariable(
            PostgresAuthenticationStoreIntegrationTests.ConnectionEnvironment);
        if (connection is null)
        {
            return;
        }

        Assert.IsFalse(string.IsNullOrWhiteSpace(connection));
        await using var dataSource = NpgsqlDataSource.Create(connection);
        var store = new PostgresAuthenticationStore(dataSource);
        var service = new AdministratorBootstrapRecoveryService(
            store,
            new FakeOutOfBandAuthorizationVerifier(),
            new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed),
            NullLogger<AdministratorBootstrapRecoveryService>.Instance);
        var accountName = new LocalAccountName("bootstrap-integration");
        using var initialProof = new OutOfBandAuthorizationProof(new byte[32]);
        using var initialSecret = new LocalSecret(
            "V01-014 initial administrator integration secret".AsSpan());

        var bootstrap = await service.BootstrapFirstAdministratorAsync(
            Guid.NewGuid(),
            accountName,
            new LocalAccountDisplayName("Bootstrap integration administrator"),
            DateTimeOffset.UtcNow.AddMinutes(5),
            initialProof,
            initialSecret,
            CancellationToken.None);

        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.Applied, bootstrap.Outcome);
        Assert.IsTrue(bootstrap.Account!.IsSystemAdministrator);
        Assert.IsTrue(bootstrap.Account.IsEnabled);
        Assert.AreEqual(1, bootstrap.Account.CredentialVersion);
        Assert.AreEqual(1, bootstrap.Account.PrincipalSessionVersion);

        using var recoveryProof = new OutOfBandAuthorizationProof(
            Enumerable.Repeat((byte)0x72, 32).ToArray());
        using var replacementSecret = new LocalSecret(
            "V01-014 replacement administrator integration secret".AsSpan());
        var recovery = await service.RecoverAdministratorAsync(
            Guid.NewGuid(),
            accountName,
            expectedCredentialVersion: 1,
            DateTimeOffset.UtcNow.AddMinutes(5),
            recoveryProof,
            replacementSecret,
            CancellationToken.None);

        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.Applied, recovery.Outcome);
        Assert.IsTrue(recovery.Account!.IsEnabled);
        Assert.AreEqual(2, recovery.Account.CredentialVersion);
        Assert.AreEqual(2, recovery.Account.PrincipalSessionVersion);
    }
}
