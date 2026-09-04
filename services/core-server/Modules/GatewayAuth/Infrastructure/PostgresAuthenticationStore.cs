using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

public sealed class PostgresAuthenticationStore
    : ILocalCredentialStore, IBrowserSessionStore
{
    private readonly PostgresLocalCredentialStore credentials;
    private readonly PostgresBrowserSessionStore sessions;

    public PostgresAuthenticationStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        credentials = new PostgresLocalCredentialStore(dataSource);
        sessions = new PostgresBrowserSessionStore(dataSource);
    }

    public ValueTask<LocalCredentialMaterial?> FindAsync(
        LocalAccountName accountName,
        CancellationToken cancellationToken) =>
        credentials.FindAsync(accountName, cancellationToken);

    public ValueTask RecordFailureAsync(
        LocalAccountName accountName,
        long observedCredentialVersion,
        CancellationToken cancellationToken) =>
        credentials.RecordFailureAsync(
            accountName,
            observedCredentialVersion,
            cancellationToken);

    public ValueTask<BrowserSessionCreateResult> CreateAsync(
        VerifiedPrimaryIdentity identity,
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest csrfDigest,
        CancellationToken cancellationToken) =>
        sessions.CreateAsync(identity, sessionDigest, csrfDigest, cancellationToken);

    public ValueTask<AuthenticatedIdentity?> AuthenticateAsync(
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest? csrfDigest,
        bool requireCsrf,
        CancellationToken cancellationToken) =>
        sessions.AuthenticateAsync(
            sessionDigest,
            csrfDigest,
            requireCsrf,
            cancellationToken);

    public ValueTask<bool> RevokeAsync(
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest csrfDigest,
        CancellationToken cancellationToken) =>
        sessions.RevokeAsync(sessionDigest, csrfDigest, cancellationToken);
}
