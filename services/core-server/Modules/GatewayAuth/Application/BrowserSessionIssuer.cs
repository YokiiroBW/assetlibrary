using System.Security.Cryptography;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal interface IAuthenticationSecretGenerator
{
    byte[] Generate(int byteLength);
}

internal sealed class CryptographicAuthenticationSecretGenerator : IAuthenticationSecretGenerator
{
    public byte[] Generate(int byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(byteLength, BrowserSessionToken.ByteLength);
        return RandomNumberGenerator.GetBytes(byteLength);
    }
}

public sealed class BrowserSessionIssuer
{
    private const int MaximumTokenAttempts = 3;
    private readonly IBrowserSessionStore store;
    private readonly IAuthenticationSecretGenerator secrets;
    private readonly ILogger<BrowserSessionIssuer> logger;

    public BrowserSessionIssuer(
        IBrowserSessionStore store,
        ILogger<BrowserSessionIssuer> logger)
        : this(store, new CryptographicAuthenticationSecretGenerator(), logger)
    {
    }

    internal BrowserSessionIssuer(
        IBrowserSessionStore store,
        IAuthenticationSecretGenerator secrets,
        ILogger<BrowserSessionIssuer> logger)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<BrowserSessionCredentials?> IssueAsync(
        VerifiedPrimaryIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        using var deadline = AuthenticationOperationDeadline.Create(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < MaximumTokenAttempts; attempt++)
            {
                var sessionToken = new BrowserSessionToken(
                    secrets.Generate(BrowserSessionToken.ByteLength));
                var csrfToken = new BrowserCsrfToken(
                    secrets.Generate(BrowserCsrfToken.ByteLength));
                BrowserSessionCreateResult result;
                try
                {
                    using var sessionDigest = AuthenticationSecretDigest.Create(sessionToken.Value);
                    using var csrfDigest = AuthenticationSecretDigest.Create(csrfToken.Value);
                    result = await store.CreateAsync(
                        identity,
                        sessionDigest,
                        csrfDigest,
                        deadline.Token).ConfigureAwait(false);
                }
                catch
                {
                    sessionToken.Dispose();
                    csrfToken.Dispose();
                    throw;
                }

                switch (result.Outcome)
                {
                    case BrowserSessionCreateOutcome.Created:
                        try
                        {
                            return new BrowserSessionCredentials(
                                sessionToken,
                                csrfToken,
                                result.IssuedAt!.Value,
                                result.IdleExpiresAt!.Value,
                                result.AbsoluteExpiresAt!.Value);
                        }
                        catch
                        {
                            sessionToken.Dispose();
                            csrfToken.Dispose();
                            throw;
                        }

                    case BrowserSessionCreateOutcome.IdentityStale:
                        sessionToken.Dispose();
                        csrfToken.Dispose();
                        return null;

                    case BrowserSessionCreateOutcome.TokenConflict:
                        sessionToken.Dispose();
                        csrfToken.Dispose();
                        break;

                    default:
                        sessionToken.Dispose();
                        csrfToken.Dispose();
                        throw new InvalidOperationException(
                            "The browser session store returned an unknown outcome.");
                }
            }

            throw new InvalidOperationException("A unique browser session token could not be allocated.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AuthenticationLog.OperationTimedOut(logger);
            throw new TimeoutException("The authentication operation exceeded its deadline.");
        }
    }
}

internal static class AuthenticationOperationDeadline
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static CancellationTokenSource Create(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        return deadline;
    }
}
