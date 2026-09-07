using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

public sealed class GatewayAuthenticationRuntime : IDisposable
{
    private readonly AdministratorBootstrapRecoveryService administrators;
    private readonly IOperatorAuthorizationIssuer authorizationIssuer;
    private readonly IGatewayAuthorizationKeyLifecycle keys;
    private readonly IDisposable riskOwner;

    internal GatewayAuthenticationRuntime(
        LocalAuthenticationService localAuthentication,
        BrowserSessionService browserSessions,
        AdministratorBootstrapRecoveryService administrators,
        IOperatorAuthorizationIssuer authorizationIssuer,
        IGatewayAuthorizationKeyLifecycle keys,
        IDisposable riskOwner)
    {
        LocalAuthentication = localAuthentication;
        BrowserSessions = browserSessions;
        this.administrators = administrators;
        this.authorizationIssuer = authorizationIssuer;
        this.keys = keys;
        this.riskOwner = riskOwner;
    }

    public LocalAuthenticationService LocalAuthentication { get; }

    public BrowserSessionService BrowserSessions { get; }

    public ValueTask<LocalSignInResult> SignInAsync(
        LocalAccountName accountName, LocalSecret secret, CancellationToken cancellationToken) =>
        LocalAuthentication.SignInAsync(accountName, secret, cancellationToken);

    public ValueTask<SessionAuthenticationResult> AuthenticateReadAsync(
        BrowserSessionToken sessionToken, CancellationToken cancellationToken) =>
        BrowserSessions.AuthenticateForReadAsync(sessionToken, cancellationToken);

    public ValueTask<SessionAuthenticationResult> AuthenticateMutationAsync(
        BrowserSessionToken sessionToken, BrowserCsrfToken csrfToken, CancellationToken cancellationToken) =>
        BrowserSessions.AuthenticateForMutationAsync(sessionToken, csrfToken, cancellationToken);

    public ValueTask<bool> SignOutAsync(
        BrowserSessionToken sessionToken, BrowserCsrfToken csrfToken, CancellationToken cancellationToken) =>
        BrowserSessions.SignOutAsync(sessionToken, csrfToken, cancellationToken);

    public ValueTask<GatewayAuthorizationKeyState> InitializeAuthorizationKeyAsync(
        CancellationToken cancellationToken) => keys.InitializeAsync(cancellationToken);

    public ValueTask<GatewayAuthorizationKeyState> RotateAuthorizationKeyAsync(
        CancellationToken cancellationToken) => keys.RotateAsync(cancellationToken);

    public ValueTask<AdministratorBootstrapRecoveryResult> BootstrapAdministratorAsync(
        AdministratorOperatorRequest request, LocalSecret secret, CancellationToken cancellationToken) =>
        ExecuteOperatorAsync(
            AdministratorBootstrapRecoveryAction.BootstrapFirstAdministrator,
            request, secret, cancellationToken);

    public ValueTask<AdministratorBootstrapRecoveryResult> RecoverAdministratorAsync(
        AdministratorOperatorRequest request, LocalSecret secret, CancellationToken cancellationToken) =>
        ExecuteOperatorAsync(
            AdministratorBootstrapRecoveryAction.RecoverAdministrator,
            request, secret, cancellationToken);

    private async ValueTask<AdministratorBootstrapRecoveryResult> ExecuteOperatorAsync(
        AdministratorBootstrapRecoveryAction expectedAction,
        AdministratorOperatorRequest request,
        LocalSecret secret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(secret);
        if (request.Action != expectedAction)
        {
            throw new ArgumentException("The operator request has the wrong action.", nameof(request));
        }

        using var deadline = AuthenticationOperationDeadline.Create(cancellationToken);
        try
        {
            using var proof = await authorizationIssuer.IssueAsync(request, deadline.Token).ConfigureAwait(false);
            return expectedAction == AdministratorBootstrapRecoveryAction.BootstrapFirstAdministrator
                ? await administrators.BootstrapFirstAdministratorAsync(
                    request.OperationId, request.AccountName, request.DisplayName!.Value,
                    request.ExpiresAt, proof, secret, deadline.Token).ConfigureAwait(false)
                : await administrators.RecoverAdministratorAsync(
                    request.OperationId, request.AccountName, request.ExpiresAt,
                    proof, secret, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperatorAuthorizationRejectedException)
        {
            return AdministratorBootstrapRecoveryResult.Rejected(
                AdministratorBootstrapRecoveryOutcome.AuthorizationRejected);
        }
        catch (Exception)
        {
            return AdministratorBootstrapRecoveryResult.Rejected(
                AdministratorBootstrapRecoveryOutcome.DependencyUnavailable);
        }
    }

    public void Dispose()
    {
        LocalAuthentication.Dispose();
        riskOwner.Dispose();
    }
}
