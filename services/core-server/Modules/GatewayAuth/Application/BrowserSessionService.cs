using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public sealed class BrowserSessionService(
    IBrowserSessionStore store,
    ILogger<BrowserSessionService> logger)
{
    private readonly IBrowserSessionStore store =
        store ?? throw new ArgumentNullException(nameof(store));
    private readonly ILogger<BrowserSessionService> logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public ValueTask<SessionAuthenticationResult> AuthenticateForReadAsync(
        BrowserSessionToken sessionToken,
        CancellationToken cancellationToken) =>
        AuthenticateAsync(sessionToken, null, requireCsrf: false, cancellationToken);

    public ValueTask<SessionAuthenticationResult> AuthenticateForMutationAsync(
        BrowserSessionToken sessionToken,
        BrowserCsrfToken csrfToken,
        CancellationToken cancellationToken) =>
        AuthenticateAsync(
            sessionToken,
            csrfToken ?? throw new ArgumentNullException(nameof(csrfToken)),
            requireCsrf: true,
            cancellationToken);

    public async ValueTask<bool> SignOutAsync(
        BrowserSessionToken sessionToken,
        BrowserCsrfToken csrfToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionToken);
        ArgumentNullException.ThrowIfNull(csrfToken);
        using var deadline = AuthenticationOperationDeadline.Create(cancellationToken);
        using var sessionDigest = AuthenticationSecretDigest.Create(sessionToken.Value);
        using var csrfDigest = AuthenticationSecretDigest.Create(csrfToken.Value);
        try
        {
            return await store.RevokeAsync(
                sessionDigest,
                csrfDigest,
                deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AuthenticationLog.OperationTimedOut(logger);
            throw new TimeoutException("The authentication operation exceeded its deadline.");
        }
    }

    private async ValueTask<SessionAuthenticationResult> AuthenticateAsync(
        BrowserSessionToken sessionToken,
        BrowserCsrfToken? csrfToken,
        bool requireCsrf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionToken);
        using var deadline = AuthenticationOperationDeadline.Create(cancellationToken);
        using var sessionDigest = AuthenticationSecretDigest.Create(sessionToken.Value);
        using var csrfDigest = csrfToken is null
            ? null
            : AuthenticationSecretDigest.Create(csrfToken.Value);
        try
        {
            var identity = await store.AuthenticateAsync(
                sessionDigest,
                csrfDigest,
                requireCsrf,
                deadline.Token).ConfigureAwait(false);
            if (identity is null)
            {
                AuthenticationLog.SessionRejected(logger);
                return SessionAuthenticationResult.Rejected();
            }

            return SessionAuthenticationResult.Succeeded(identity);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AuthenticationLog.OperationTimedOut(logger);
            throw new TimeoutException("The authentication operation exceeded its deadline.");
        }
    }
}
