using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

// The Host supplies an action-bound local operator proof before management calls.
public sealed class ServiceReadAuthenticationService
{
    private readonly IServiceReadCredentialStore store;
    private readonly TimeProvider clock;
    private readonly TimeSpan maximumLifetime;

    public ServiceReadAuthenticationService(IServiceReadCredentialStore store,
        int maximumLifetimeDays = 365, TimeProvider? timeProvider = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        if (maximumLifetimeDays is < 30 or > 365) throw new ArgumentOutOfRangeException(nameof(maximumLifetimeDays));
        maximumLifetime = TimeSpan.FromDays(maximumLifetimeDays);
        clock = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<bool> CreatePrincipalAsync(Guid principalId, string displayName,
        ServiceReadOperation operation, CancellationToken cancellationToken)
    {
        ValidatePrincipal(principalId);
        ArgumentNullException.ThrowIfNull(operation);
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200 || displayName.Any(char.IsControl))
            throw new ArgumentException("Service display name is invalid.", nameof(displayName));
        return store.CreatePrincipalAsync(principalId, displayName, operation, cancellationToken);
    }

    public async ValueTask<IssuedServiceReadCredential?> IssueAsync(Guid principalId,
        Guid? replacedCredentialId, TimeSpan? lifetime, ServiceReadOperation operation, CancellationToken cancellationToken)
    {
        ValidatePrincipal(principalId);
        ArgumentNullException.ThrowIfNull(operation);
        var duration = lifetime ?? TimeSpan.FromDays(30);
        if (duration <= TimeSpan.Zero || duration > maximumLifetime || replacedCredentialId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        var token = new ServiceReadToken(RandomNumberGenerator.GetBytes(32));
        try
        {
            using var digest = AuthenticationSecretDigest.Create(token.Value);
            var credentialId = Guid.NewGuid();
            var expiresAt = clock.GetUtcNow().Add(duration);
            if (await store.IssueAsync(principalId, credentialId, replacedCredentialId, digest,
                expiresAt, operation, cancellationToken).ConfigureAwait(false))
                return new IssuedServiceReadCredential(credentialId, token, expiresAt);
            token.Dispose();
            return null;
        }
        catch
        {
            token.Dispose();
            throw;
        }
    }

    public async ValueTask<ServiceReadIdentity?> AuthenticateAsync(ServiceReadToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        using var digest = AuthenticationSecretDigest.Create(token.Value);
        return await store.AuthenticateAsync(digest, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<bool> RevokeAsync(Guid principalId, Guid credentialId, ServiceReadOperation operation, CancellationToken cancellationToken)
    {
        ValidatePrincipal(principalId);
        ValidatePrincipal(credentialId);
        ArgumentNullException.ThrowIfNull(operation);
        return store.RevokeAsync(principalId, credentialId, operation, cancellationToken);
    }

    public ValueTask<bool> DisableAsync(Guid principalId, ServiceReadOperation operation, CancellationToken cancellationToken)
    {
        ValidatePrincipal(principalId);
        ArgumentNullException.ThrowIfNull(operation);
        return store.DisableAsync(principalId, operation, cancellationToken);
    }

    private static void ValidatePrincipal(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("Service identifier is invalid.", nameof(value));
    }
}
