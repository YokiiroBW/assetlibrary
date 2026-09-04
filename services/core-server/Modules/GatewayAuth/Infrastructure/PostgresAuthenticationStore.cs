using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Npgsql;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

public sealed class PostgresAuthenticationStore
    : ILocalCredentialStore,
      IBrowserSessionStore,
      ILocalAccountLifecycleStore,
      IAdministratorBootstrapRecoveryStore
{
    private readonly PostgresLocalCredentialStore credentials;
    private readonly PostgresBrowserSessionStore sessions;
    private readonly PostgresLocalAccountLifecycleStore lifecycle;
    private readonly PostgresAdministratorBootstrapRecoveryStore administratorRecovery;

    public PostgresAuthenticationStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        credentials = new PostgresLocalCredentialStore(dataSource);
        sessions = new PostgresBrowserSessionStore(dataSource);
        lifecycle = new PostgresLocalAccountLifecycleStore(dataSource);
        administratorRecovery = new PostgresAdministratorBootstrapRecoveryStore(dataSource);
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

    ValueTask<LocalAccountLifecycleResult> ILocalAccountLifecycleStore.FindAsync(
        AuthenticatedIdentity actor,
        LocalAccountName accountName,
        CancellationToken cancellationToken) =>
        lifecycle.FindAsync(actor, accountName, cancellationToken);

    ValueTask<LocalAccountLifecycleResult> ILocalAccountLifecycleStore.ProvisionAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        Guid requestedPrincipalId,
        LocalAccountName accountName,
        LocalAccountDisplayName displayName,
        bool isSystemAdministrator,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken) =>
        lifecycle.ProvisionAsync(
            actor,
            operationId,
            requestedPrincipalId,
            accountName,
            displayName,
            isSystemAdministrator,
            credential,
            cancellationToken);

    ValueTask<LocalAccountLifecycleResult> ILocalAccountLifecycleStore.ReplaceCredentialAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        long expectedCredentialVersion,
        bool enableAccount,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken) =>
        lifecycle.ReplaceCredentialAsync(
            actor,
            operationId,
            accountName,
            expectedCredentialVersion,
            enableAccount,
            credential,
            cancellationToken);

    ValueTask<LocalAccountLifecycleResult> ILocalAccountLifecycleStore.SetEnabledAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        long expectedPrincipalSessionVersion,
        bool enabled,
        CancellationToken cancellationToken) =>
        lifecycle.SetEnabledAsync(
            actor,
            operationId,
            accountName,
            expectedPrincipalSessionVersion,
            enabled,
            cancellationToken);

    ValueTask<AdministratorBootstrapRecoveryResult>
        IAdministratorBootstrapRecoveryStore.BootstrapAsync(
            VerifiedOutOfBandAuthorization authorization,
            Guid requestedPrincipalId,
            LocalAccountDisplayName displayName,
            LocalCredentialEnrollmentMaterial credential,
            CancellationToken cancellationToken) =>
        administratorRecovery.BootstrapAsync(
            authorization,
            requestedPrincipalId,
            displayName,
            credential,
            cancellationToken);

    ValueTask<AdministratorBootstrapRecoveryResult>
        IAdministratorBootstrapRecoveryStore.RecoverAsync(
            VerifiedOutOfBandAuthorization authorization,
            long expectedCredentialVersion,
            LocalCredentialEnrollmentMaterial credential,
            CancellationToken cancellationToken) =>
        administratorRecovery.RecoverAsync(
            authorization,
            expectedCredentialVersion,
            credential,
            cancellationToken);
}
