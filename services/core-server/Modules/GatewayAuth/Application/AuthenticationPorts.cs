using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Domain;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public sealed record VerifiedPrimaryIdentity
{
    public VerifiedPrimaryIdentity(
        AuthenticatedIdentity identity,
        long? localCredentialVersion = null)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        if ((identity.AuthenticationMethod == PrimaryAuthenticationMethod.LocalAccount)
                != localCredentialVersion.HasValue
            || localCredentialVersion is <= 0)
        {
            throw new ArgumentException("A verified primary identity is invalid.");
        }

        LocalCredentialVersion = localCredentialVersion;
    }

    public AuthenticatedIdentity Identity { get; }

    public long? LocalCredentialVersion { get; }
}

public sealed class LocalCredentialMaterial : IDisposable
{
    private readonly LocalCredentialHashMaterial hashMaterial;

    public LocalCredentialMaterial(
        VerifiedPrimaryIdentity identity,
        string algorithm,
        int iterations,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> digest,
        bool canAttempt)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        if (identity.Identity.AuthenticationMethod != PrimaryAuthenticationMethod.LocalAccount)
        {
            throw new ArgumentException("Local credential material is invalid.");
        }

        hashMaterial = new LocalCredentialHashMaterial(algorithm, iterations, salt, digest);
        CanAttempt = canAttempt;
    }

    public VerifiedPrimaryIdentity Identity { get; }

    public string Algorithm => hashMaterial.Algorithm;

    public int Iterations => hashMaterial.Iterations;

    public bool CanAttempt { get; }

    internal ReadOnlySpan<byte> Salt => hashMaterial.Salt.Span;

    internal ReadOnlySpan<byte> Digest => hashMaterial.Digest.Span;

    public void Dispose() => hashMaterial.Dispose();

    public override string ToString() => "[redacted]";
}

internal sealed class LocalCredentialHashMaterial : IDisposable
{
    private byte[]? salt;
    private byte[]? digest;

    public LocalCredentialHashMaterial(
        string algorithm,
        int iterations,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> digest)
    {
        if (!string.Equals(algorithm, LocalSecretHashingPolicy.Algorithm, StringComparison.Ordinal)
            || iterations is < LocalSecretHashingPolicy.MinimumIterations
                or > LocalSecretHashingPolicy.MaximumIterations
            || salt.Length is < LocalSecretHashingPolicy.SaltBytes or > 64
            || digest.Length != LocalSecretHashingPolicy.DigestBytes)
        {
            throw new ArgumentException("Local credential material is invalid.");
        }

        Algorithm = algorithm;
        Iterations = iterations;
        this.salt = salt.ToArray();
        this.digest = digest.ToArray();
    }

    public string Algorithm { get; }

    public int Iterations { get; }

    public ReadOnlyMemory<byte> Salt => salt
        ?? throw new ObjectDisposedException(nameof(LocalCredentialHashMaterial));

    public ReadOnlyMemory<byte> Digest => digest
        ?? throw new ObjectDisposedException(nameof(LocalCredentialHashMaterial));

    public void Dispose()
    {
        var ownedSalt = Interlocked.Exchange(ref salt, null);
        var ownedDigest = Interlocked.Exchange(ref digest, null);
        if (ownedSalt is not null)
        {
            CryptographicOperations.ZeroMemory(ownedSalt);
        }

        if (ownedDigest is not null)
        {
            CryptographicOperations.ZeroMemory(ownedDigest);
        }
    }
}

public sealed class AuthenticationSecretDigest : IDisposable
{
    public const int ByteLength = 32;
    private byte[]? value;

    private AuthenticationSecretDigest(byte[] value) => this.value = value;

    internal ReadOnlyMemory<byte> Value => value
        ?? throw new ObjectDisposedException(nameof(AuthenticationSecretDigest));

    internal static AuthenticationSecretDigest Create(ReadOnlySpan<byte> secret) =>
        new(SHA256.HashData(secret));

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref value, null);
        if (owned is not null)
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public override string ToString() => "[redacted]";
}

public enum BrowserSessionCreateOutcome
{
    Created = 0,
    IdentityStale = 1,
    TokenConflict = 2,
}

public sealed record BrowserSessionCreateResult
{
    private static readonly TimeSpan MaximumIdleLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaximumAbsoluteLifetime = TimeSpan.FromHours(12);

    private BrowserSessionCreateResult(
        BrowserSessionCreateOutcome outcome,
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? idleExpiresAt = null,
        DateTimeOffset? absoluteExpiresAt = null)
    {
        Outcome = outcome;
        IssuedAt = issuedAt;
        IdleExpiresAt = idleExpiresAt;
        AbsoluteExpiresAt = absoluteExpiresAt;
    }

    public BrowserSessionCreateOutcome Outcome { get; }

    public DateTimeOffset? IssuedAt { get; }

    public DateTimeOffset? IdleExpiresAt { get; }

    public DateTimeOffset? AbsoluteExpiresAt { get; }

    public static BrowserSessionCreateResult Created(
        DateTimeOffset issuedAt,
        DateTimeOffset idleExpiresAt,
        DateTimeOffset absoluteExpiresAt)
    {
        if (idleExpiresAt <= issuedAt
            || absoluteExpiresAt <= idleExpiresAt
            || idleExpiresAt - issuedAt > MaximumIdleLifetime
            || absoluteExpiresAt - issuedAt > MaximumAbsoluteLifetime)
        {
            throw new ArgumentException("Browser session timestamps are invalid.");
        }

        return new(
            BrowserSessionCreateOutcome.Created,
            issuedAt,
            idleExpiresAt,
            absoluteExpiresAt);
    }

    public static BrowserSessionCreateResult IdentityStale() =>
        new(BrowserSessionCreateOutcome.IdentityStale);

    public static BrowserSessionCreateResult TokenConflict() =>
        new(BrowserSessionCreateOutcome.TokenConflict);
}

public interface ILocalCredentialStore
{
    ValueTask<LocalCredentialMaterial?> FindAsync(
        LocalAccountName accountName,
        CancellationToken cancellationToken);

    ValueTask RecordFailureAsync(
        LocalAccountName accountName,
        long observedCredentialVersion,
        CancellationToken cancellationToken);
}

public interface IBrowserSessionStore
{
    ValueTask<BrowserSessionCreateResult> CreateAsync(
        VerifiedPrimaryIdentity identity,
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest csrfDigest,
        CancellationToken cancellationToken);

    ValueTask<AuthenticatedIdentity?> AuthenticateAsync(
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest? csrfDigest,
        bool requireCsrf,
        CancellationToken cancellationToken);

    ValueTask<bool> RevokeAsync(
        AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest csrfDigest,
        CancellationToken cancellationToken);
}
