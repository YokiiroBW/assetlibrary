using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Infrastructure;

internal sealed class FileOutOfBandAuthorization(
    ProtectedGatewayAuthorizationKeyStore keys,
    Guid deploymentId,
    TimeProvider clock) : IOutOfBandAuthorizationVerifier, IOperatorAuthorizationIssuer
{
    private const int ProofLength = 65;
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly ProtectedGatewayAuthorizationKeyStore keys = keys ?? throw new ArgumentNullException(nameof(keys));
    private readonly TimeProvider clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async ValueTask<OutOfBandAuthorizationProof> IssueAsync(
        AdministratorOperatorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsLive(request.ExpiresAt))
        {
            throw new OperatorAuthorizationRejectedException();
        }

        using var key = await keys.ReadAsync(cancellationToken).ConfigureAwait(false);
        var proof = new byte[ProofLength];
        try
        {
            proof[0] = 1;
            key.State.KeyId.TryWriteBytes(proof.AsSpan(1, 16));
            request.AuthorizationId.TryWriteBytes(proof.AsSpan(17, 16));
            var payload = Payload(
                key.State.KeyId, request.AuthorizationId,
                request.Action, request.OperationId, request.AccountName, request.ExpiresAt);
            HMACSHA256.HashData(key.Secret, payload, proof.AsSpan(33));
            return new OutOfBandAuthorizationProof(proof);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(proof);
        }
    }

    public async ValueTask<OutOfBandAuthorizationVerificationResult> VerifyAsync(
        OutOfBandAuthorizationRequest request,
        OutOfBandAuthorizationProof proof,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(proof);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsLive(request.ExpiresAt))
        {
            return OutOfBandAuthorizationVerificationResult.Expired();
        }

        if (proof.Value.Length != ProofLength || proof.Value.Span[0] != 1)
        {
            return OutOfBandAuthorizationVerificationResult.Rejected();
        }

        try
        {
            using var key = await keys.ReadAsync(cancellationToken).ConfigureAwait(false);
            var keyId = new Guid(proof.Value.Span.Slice(1, 16));
            var authorizationId = new Guid(proof.Value.Span.Slice(17, 16));
            if (keyId != key.State.KeyId || authorizationId == Guid.Empty)
            {
                return OutOfBandAuthorizationVerificationResult.Rejected();
            }

            var payload = Payload(
                keyId, authorizationId, request.Action, request.OperationId,
                request.TargetAccountName, request.ExpiresAt);
            var digest = HMACSHA256.HashData(key.Secret, payload);
            try
            {
                return CryptographicOperations.FixedTimeEquals(digest, proof.Value.Span[33..])
                    ? OutOfBandAuthorizationVerificationResult.Verified(new VerifiedOutOfBandAuthorization(
                        authorizationId, request.Action, request.OperationId, request.TargetAccountName, request.ExpiresAt))
                    : OutOfBandAuthorizationVerificationResult.Rejected();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(digest);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return OutOfBandAuthorizationVerificationResult.Unavailable();
        }
    }

    private bool IsLive(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - clock.GetUtcNow();
        return remaining > TimeSpan.Zero && remaining <= MaximumLifetime;
    }

    private byte[] Payload(
        Guid keyId,
        Guid authorizationId,
        AdministratorBootstrapRecoveryAction action,
        Guid operationId,
        LocalAccountName accountName,
        DateTimeOffset expiresAt) =>
        Encoding.UTF8.GetBytes(string.Join('\n',
            "AssetLibrary.GatewayAuth.OutOfBandAuthorization.v1",
            deploymentId.ToString("N"),
            keyId.ToString("N"),
            authorizationId.ToString("N"),
            ((int)action).ToString(CultureInfo.InvariantCulture),
            operationId.ToString("N"),
            accountName.Value,
            expiresAt.UtcTicks.ToString(CultureInfo.InvariantCulture)));
}
