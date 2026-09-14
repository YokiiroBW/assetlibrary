using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public interface IServiceReadCredentialStore
{
    ValueTask<bool> CreatePrincipalAsync(Guid principalId, string displayName, ServiceReadOperation operation, CancellationToken cancellationToken);
    ValueTask<bool> IssueAsync(Guid principalId, Guid credentialId, Guid? replacedCredentialId,
        AuthenticationSecretDigest digest, DateTimeOffset expiresAt, ServiceReadOperation operation, CancellationToken cancellationToken);
    ValueTask<ServiceReadIdentity?> AuthenticateAsync(AuthenticationSecretDigest digest, CancellationToken cancellationToken);
    ValueTask<bool> RevokeAsync(Guid principalId, Guid credentialId, ServiceReadOperation operation, CancellationToken cancellationToken);
    ValueTask<bool> DisableAsync(Guid principalId, ServiceReadOperation operation, CancellationToken cancellationToken);
}
