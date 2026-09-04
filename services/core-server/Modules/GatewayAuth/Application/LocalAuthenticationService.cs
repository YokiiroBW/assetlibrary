using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Domain;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public sealed class LocalAuthenticationService : IDisposable
{
    private readonly ILocalCredentialStore credentials;
    private readonly BrowserSessionIssuer sessions;
    private readonly ILocalSecretVerifier verifier;
    private readonly ILogger<LocalAuthenticationService> logger;
    private byte[]? dummySalt;
    private byte[]? dummyDigest;

    public LocalAuthenticationService(
        ILocalCredentialStore credentials,
        BrowserSessionIssuer sessions,
        ILogger<LocalAuthenticationService> logger)
        : this(credentials, sessions, new Pbkdf2LocalSecretVerifier(), logger)
    {
    }

    internal LocalAuthenticationService(
        ILocalCredentialStore credentials,
        BrowserSessionIssuer sessions,
        ILocalSecretVerifier verifier,
        ILogger<LocalAuthenticationService> logger)
    {
        this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        dummySalt = RandomNumberGenerator.GetBytes(LocalSecretHashingPolicy.SaltBytes);
        dummyDigest = RandomNumberGenerator.GetBytes(LocalSecretHashingPolicy.DigestBytes);
    }

    public async ValueTask<LocalSignInResult> SignInAsync(
        LocalAccountName accountName,
        LocalSecret secret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        using var deadline = AuthenticationOperationDeadline.Create(cancellationToken);
        try
        {
            using var material = await credentials.FindAsync(accountName, deadline.Token)
                .ConfigureAwait(false);
            var matches = material is null
                ? verifier.Verify(
                    secret,
                    LocalSecretHashingPolicy.MinimumIterations,
                    DummySalt,
                    DummyDigest)
                : verifier.Verify(
                    secret,
                    material.Iterations,
                    material.Salt,
                    material.Digest);

            if (material is null || !matches || !material.CanAttempt)
            {
                if (material is not null && !matches && material.CanAttempt)
                {
                    await credentials.RecordFailureAsync(
                        accountName,
                        material.Identity.LocalCredentialVersion!.Value,
                        deadline.Token).ConfigureAwait(false);
                }

                AuthenticationLog.LocalSignInRejected(logger);
                return LocalSignInResult.Rejected();
            }

            var session = await sessions.IssueAsync(material.Identity, deadline.Token)
                .ConfigureAwait(false);
            if (session is null)
            {
                AuthenticationLog.LocalSignInRejected(logger);
                return LocalSignInResult.Rejected();
            }

            AuthenticationLog.LocalSignInSucceeded(logger);
            return LocalSignInResult.Succeeded(session);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AuthenticationLog.OperationTimedOut(logger);
            throw new TimeoutException("The authentication operation exceeded its deadline.");
        }
    }

    public void Dispose()
    {
        var salt = Interlocked.Exchange(ref dummySalt, null);
        var digest = Interlocked.Exchange(ref dummyDigest, null);
        if (salt is not null)
        {
            CryptographicOperations.ZeroMemory(salt);
        }

        if (digest is not null)
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private ReadOnlySpan<byte> DummySalt => dummySalt
        ?? throw new ObjectDisposedException(nameof(LocalAuthenticationService));

    private ReadOnlySpan<byte> DummyDigest => dummyDigest
        ?? throw new ObjectDisposedException(nameof(LocalAuthenticationService));
}
