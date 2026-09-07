using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Packaging.Tests;

internal sealed class TrialAuthenticationStore : ILocalCredentialStore, IBrowserSessionStore, IDisposable
{
    public const string Password = "a bounded trial authentication passphrase";
    private readonly byte[] salt = RandomNumberGenerator.GetBytes(16);
    private readonly byte[] digest;
    private int credentialCalls;
    private int authenticationCalls;

    public TrialAuthenticationStore() => digest = Rfc2898DeriveBytes.Pbkdf2(
        Password, salt, 600_000, HashAlgorithmName.SHA256, 32);

    public bool Available { get; set; } = true;

    public bool Revoked { get; set; }

    public bool Administrator { get; set; } = true;

    public Guid PrincipalId { get; set; } = Guid.NewGuid();

    public TaskCompletionSource? HoldCredentials { get; set; }

    public int CredentialCalls => Volatile.Read(ref credentialCalls);

    public int AuthenticationCalls => Volatile.Read(ref authenticationCalls);

    public AuthenticatedIdentity Identity => new(PrincipalId, new AuthenticatedSubject("local:trial-admin"),
        "Trial administrator", Administrator, 1, PrimaryAuthenticationMethod.LocalAccount);

    public async ValueTask<LocalCredentialMaterial?> FindAsync(
        LocalAccountName accountName, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref credentialCalls);
        Check(cancellationToken);
        if (HoldCredentials is not null)
        {
            await HoldCredentials.Task.WaitAsync(cancellationToken);
        }

        return accountName.Value == "trial-admin"
            ? new LocalCredentialMaterial(new VerifiedPrimaryIdentity(Identity, 1),
                "pbkdf2-sha256", 600_000, salt, digest, canAttempt: true)
            : null;
    }

    public ValueTask RecordFailureAsync(
        LocalAccountName accountName, long observedCredentialVersion, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        return ValueTask.CompletedTask;
    }

    public ValueTask<BrowserSessionCreateResult> CreateAsync(
        VerifiedPrimaryIdentity identity, AuthenticationSecretDigest sessionDigest,
        AuthenticationSecretDigest csrfDigest, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        Revoked = false;
        var now = DateTimeOffset.UtcNow;
        return ValueTask.FromResult(BrowserSessionCreateResult.Created(now, now.AddMinutes(30), now.AddHours(12)));
    }

    public ValueTask<AuthenticatedIdentity?> AuthenticateAsync(
        AuthenticationSecretDigest sessionDigest, AuthenticationSecretDigest? csrfDigest,
        bool requireCsrf, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref authenticationCalls);
        Check(cancellationToken);
        Assert.IsTrue(requireCsrf);
        Assert.IsNotNull(csrfDigest);
        return ValueTask.FromResult(Revoked ? null : Identity);
    }

    public ValueTask<bool> RevokeAsync(
        AuthenticationSecretDigest sessionDigest, AuthenticationSecretDigest csrfDigest,
        CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        var wasActive = !Revoked;
        Revoked = true;
        return ValueTask.FromResult(wasActive);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(salt);
        CryptographicOperations.ZeroMemory(digest);
    }

    private void Check(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Available)
        {
            throw new InvalidOperationException("sensitive-database-details-for-test");
        }
    }
}
