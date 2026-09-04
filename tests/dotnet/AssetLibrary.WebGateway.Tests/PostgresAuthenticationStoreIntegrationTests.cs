using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class PostgresAuthenticationStoreIntegrationTests
{
    public const string ConnectionEnvironment = "ASSETLIBRARY_TEST_AUTH_CONNECTION";
    public const string SecretEnvironment = "ASSETLIBRARY_TEST_AUTH_SECRET";

    [TestMethod]
    public async Task PostgresStoreCompletesLocalSignInCsrfAndRevocationRoundTrip()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionEnvironment);
        var secretValue = Environment.GetEnvironmentVariable(SecretEnvironment);
        if (connection is null && secretValue is null)
        {
            return;
        }

        Assert.IsFalse(string.IsNullOrWhiteSpace(connection));
        Assert.IsNotNull(secretValue);

        await using var dataSource = NpgsqlDataSource.Create(connection);
        var store = new PostgresAuthenticationStore(dataSource);
        var issuer = new BrowserSessionIssuer(
            store,
            NullLogger<BrowserSessionIssuer>.Instance);
        using var authentication = new LocalAuthenticationService(
            store,
            issuer,
            NullLogger<LocalAuthenticationService>.Instance);
        var sessions = new BrowserSessionService(
            store,
            NullLogger<BrowserSessionService>.Instance);
        using var secret = new LocalSecret(secretValue.AsSpan());

        var signIn = await authentication.SignInAsync(
            new LocalAccountName("test-user"),
            secret,
            CancellationToken.None);

        Assert.AreEqual(LocalSignInStatus.Succeeded, signIn.Status);
        Assert.IsNotNull(signIn.Session);
        using var issued = signIn.Session;
        var read = await sessions.AuthenticateForReadAsync(
            issued.SessionToken,
            CancellationToken.None);
        var mutation = await sessions.AuthenticateForMutationAsync(
            issued.SessionToken,
            issued.CsrfToken,
            CancellationToken.None);
        Assert.IsTrue(read.IsAuthenticated);
        Assert.IsTrue(mutation.IsAuthenticated);
        Assert.AreEqual("local:test-user", read.Identity!.Subject.Value);
        Assert.IsTrue(await sessions.SignOutAsync(
            issued.SessionToken,
            issued.CsrfToken,
            CancellationToken.None));
        Assert.IsFalse((await sessions.AuthenticateForReadAsync(
            issued.SessionToken,
            CancellationToken.None)).IsAuthenticated);
    }
}
