using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Domain;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal enum LocalSecretRisk
{
    Allowed = 0,
    Common = 1,
    Compromised = 2,
    Unavailable = 3,
}

internal interface ILocalSecretRiskChecker
{
    ValueTask<LocalSecretRisk> EvaluateAsync(
        LocalSecret secret,
        CancellationToken cancellationToken);
}

internal interface ILocalCredentialDeriver
{
    LocalCredentialEnrollmentMaterial Derive(LocalSecret secret);
}

internal sealed class LocalCredentialEnrollmentMaterial : IDisposable
{
    private readonly LocalCredentialHashMaterial hashMaterial;

    public LocalCredentialEnrollmentMaterial(
        string algorithm,
        int iterations,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> digest)
    {
        hashMaterial = new LocalCredentialHashMaterial(algorithm, iterations, salt, digest);
    }

    public string Algorithm => hashMaterial.Algorithm;

    public int Iterations => hashMaterial.Iterations;

    internal ReadOnlyMemory<byte> Salt => hashMaterial.Salt;

    internal ReadOnlyMemory<byte> Digest => hashMaterial.Digest;

    public void Dispose() => hashMaterial.Dispose();

    public override string ToString() => "[redacted]";
}

internal interface ILocalAccountLifecycleStore
{
    ValueTask<LocalAccountLifecycleResult> FindAsync(
        AuthenticatedIdentity actor,
        LocalAccountName accountName,
        CancellationToken cancellationToken);

    ValueTask<LocalAccountLifecycleResult> ProvisionAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        Guid requestedPrincipalId,
        LocalAccountName accountName,
        LocalAccountDisplayName displayName,
        bool isSystemAdministrator,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken);

    ValueTask<LocalAccountLifecycleResult> ReplaceCredentialAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        long expectedCredentialVersion,
        bool enableAccount,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken);

    ValueTask<LocalAccountLifecycleResult> SetEnabledAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        long expectedPrincipalSessionVersion,
        bool enabled,
        CancellationToken cancellationToken);
}
