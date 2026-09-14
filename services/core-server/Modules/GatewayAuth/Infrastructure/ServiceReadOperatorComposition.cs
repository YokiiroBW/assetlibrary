using System.Security.Cryptography;
using System.Text.Json;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

public static class ServiceReadOperatorComposition
{
    public static IServiceReadOperatorAuthorization Create(GatewayAuthorizationConfiguration configuration,
        IDataProtectionProvider protection, TimeProvider? clock = null) =>
        new FileServiceReadOperatorAuthorization(new ProtectedGatewayAuthorizationKeyStore(
            configuration, protection, clock ?? TimeProvider.System), configuration.DeploymentId, clock ?? TimeProvider.System);
}

internal sealed class FileServiceReadOperatorAuthorization(
    ProtectedGatewayAuthorizationKeyStore keys, Guid deploymentId, TimeProvider clock) : IServiceReadOperatorAuthorization
{
    public async ValueTask<OutOfBandAuthorizationProof> IssueAsync(ServiceReadOperatorRequest request, CancellationToken cancellationToken)
    {
        if (!IsLive(request)) throw new UnauthorizedAccessException("Service operator authorization expired.");
        using var key = await keys.ReadAsync(cancellationToken).ConfigureAwait(false);
        var bytes = new byte[49];
        try
        {
            // Separate version and signed purpose: bootstrap/recovery proofs cannot authorize service actions.
            bytes[0] = 2;
            key.State.KeyId.TryWriteBytes(bytes.AsSpan(1, 16));
            HMACSHA256.HashData(key.Secret, Payload(request), bytes.AsSpan(17));
            return new OutOfBandAuthorizationProof(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public async ValueTask<bool> VerifyAsync(ServiceReadOperatorRequest request, OutOfBandAuthorizationProof proof,
        CancellationToken cancellationToken)
    {
        if (!IsLive(request) || proof.Value.Length != 49 || proof.Value.Span[0] != 2) return false;
        using var key = await keys.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (new Guid(proof.Value.Span.Slice(1, 16)) != key.State.KeyId) return false;
        var expected = HMACSHA256.HashData(key.Secret, Payload(request));
        try { return CryptographicOperations.FixedTimeEquals(expected, proof.Value.Span[17..]); }
        finally { CryptographicOperations.ZeroMemory(expected); }
    }

    private bool IsLive(ServiceReadOperatorRequest request) =>
        request.ExpiresAt > clock.GetUtcNow() && request.ExpiresAt - clock.GetUtcNow() <= TimeSpan.FromMinutes(15);

    private byte[] Payload(ServiceReadOperatorRequest request) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        purpose = "AssetLibrary.GatewayAuth.ServiceReadOperator.v1",
        deploymentId,
        request.AuthorizationId,
        request.OperationId,
        request.Action,
        request.PrincipalId,
        request.OperatorId,
        request.ExpiresAt,
        request.DisplayName,
        request.LibraryIds,
        request.CredentialId,
        request.LifetimeDays,
    });
}
