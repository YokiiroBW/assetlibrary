using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal sealed class AdministratorBootstrapRecoveryService
{
    private static readonly TimeSpan MaximumOperationTimeout = TimeSpan.FromSeconds(5);
    private readonly IAdministratorBootstrapRecoveryStore store;
    private readonly IOutOfBandAuthorizationVerifier authorizationVerifier;
    private readonly ILocalSecretRiskChecker riskChecker;
    private readonly ILocalCredentialDeriver credentialDeriver;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan operationTimeout;
    private readonly ILogger<AdministratorBootstrapRecoveryService> logger;
    private readonly AdministratorRecoveryMutation? recoveryPreparation;

    public AdministratorBootstrapRecoveryService(
        IAdministratorBootstrapRecoveryStore store,
        IOutOfBandAuthorizationVerifier authorizationVerifier,
        ILocalSecretRiskChecker riskChecker,
        ILogger<AdministratorBootstrapRecoveryService>? logger = null,
        AdministratorRecoveryMutation? recoveryPreparation = null)
        : this(
            store,
            authorizationVerifier,
            riskChecker,
            new Pbkdf2LocalCredentialDeriver(),
            TimeProvider.System,
            MaximumOperationTimeout,
            logger ?? NullLogger<AdministratorBootstrapRecoveryService>.Instance,
            recoveryPreparation)
    {
    }

    internal AdministratorBootstrapRecoveryService(
        IAdministratorBootstrapRecoveryStore store,
        IOutOfBandAuthorizationVerifier authorizationVerifier,
        ILocalSecretRiskChecker riskChecker,
        ILocalCredentialDeriver credentialDeriver,
        TimeProvider timeProvider,
        TimeSpan operationTimeout,
        ILogger<AdministratorBootstrapRecoveryService> logger,
        AdministratorRecoveryMutation? recoveryPreparation = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.authorizationVerifier = authorizationVerifier
            ?? throw new ArgumentNullException(nameof(authorizationVerifier));
        this.riskChecker = riskChecker ?? throw new ArgumentNullException(nameof(riskChecker));
        this.credentialDeriver = credentialDeriver
            ?? throw new ArgumentNullException(nameof(credentialDeriver));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (operationTimeout <= TimeSpan.Zero || operationTimeout > MaximumOperationTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        }

        this.operationTimeout = operationTimeout;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.recoveryPreparation = recoveryPreparation;
    }

    public ValueTask<AdministratorBootstrapRecoveryResult> BootstrapFirstAdministratorAsync(
        Guid operationId,
        LocalAccountName accountName,
        LocalAccountDisplayName displayName,
        DateTimeOffset authorizationExpiresAt,
        OutOfBandAuthorizationProof proof,
        LocalSecret newSecret,
        CancellationToken cancellationToken)
    {
        ValidateMutation(operationId, proof, newSecret);
        if (string.IsNullOrEmpty(displayName.Value))
        {
            throw new ArgumentException(
                "A bootstrap administrator display name is required.",
                nameof(displayName));
        }

        var request = new OutOfBandAuthorizationRequest(
            AdministratorBootstrapRecoveryAction.BootstrapFirstAdministrator,
            operationId,
            accountName,
            authorizationExpiresAt);
        return ExecuteAsync(
            "bootstrap_first_administrator",
            request,
            proof,
            newSecret,
            (authorization, credential, token) => store.BootstrapAsync(
                authorization,
                Guid.NewGuid(),
                displayName,
                credential,
                token),
            cancellationToken);
    }

    public ValueTask<AdministratorBootstrapRecoveryResult> RecoverAdministratorAsync(
        Guid operationId,
        LocalAccountName accountName,
        long expectedCredentialVersion,
        DateTimeOffset authorizationExpiresAt,
        OutOfBandAuthorizationProof proof,
        LocalSecret newSecret,
        CancellationToken cancellationToken)
    {
        ValidateMutation(operationId, proof, newSecret);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedCredentialVersion);
        var request = new OutOfBandAuthorizationRequest(
            AdministratorBootstrapRecoveryAction.RecoverAdministrator,
            operationId,
            accountName,
            authorizationExpiresAt);
        return ExecuteAsync(
            "recover_administrator",
            request,
            proof,
            newSecret,
            (authorization, credential, token) => store.RecoverAsync(
                authorization,
                expectedCredentialVersion,
                credential,
                token),
            cancellationToken);
    }

    public ValueTask<AdministratorBootstrapRecoveryResult> RecoverAdministratorAsync(
        Guid operationId,
        LocalAccountName accountName,
        DateTimeOffset authorizationExpiresAt,
        OutOfBandAuthorizationProof proof,
        LocalSecret newSecret,
        CancellationToken cancellationToken)
    {
        ValidateMutation(operationId, proof, newSecret);
        var request = new OutOfBandAuthorizationRequest(
            AdministratorBootstrapRecoveryAction.RecoverAdministrator,
            operationId,
            accountName,
            authorizationExpiresAt);
        return ExecuteAsync(
            "recover_administrator",
            request,
            proof,
            newSecret,
            RecoverWithCurrentVersionAsync,
            cancellationToken);
    }

    private ValueTask<AdministratorBootstrapRecoveryResult> RecoverWithCurrentVersionAsync(
        VerifiedOutOfBandAuthorization authorization,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken) =>
        recoveryPreparation?.ExecuteAsync(authorization, credential, cancellationToken)
        ?? ValueTask.FromResult(DependencyUnavailable());

    private async ValueTask<AdministratorBootstrapRecoveryResult> ExecuteAsync(
        string action,
        OutOfBandAuthorizationRequest request,
        OutOfBandAuthorizationProof proof,
        LocalSecret secret,
        Func<VerifiedOutOfBandAuthorization, LocalCredentialEnrollmentMaterial,
            CancellationToken, ValueTask<AdministratorBootstrapRecoveryResult>> mutation,
        CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(operationTimeout);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            var verification = await authorizationVerifier.VerifyAsync(
                request,
                proof,
                deadline.Token).ConfigureAwait(false);
            var authorizationResult = ValidateAuthorization(request, verification);
            if (authorizationResult.Authorization is null)
            {
                return Complete(action, authorizationResult.Result!, started);
            }

            var authorization = authorizationResult.Authorization;
            if (!secret.MeetsEnrollmentLengthPolicy)
            {
                return Complete(
                    action,
                    AdministratorBootstrapRecoveryResult.Rejected(
                        AdministratorBootstrapRecoveryOutcome.SecretRejected),
                    started);
            }

            var risk = await riskChecker.EvaluateAsync(secret, deadline.Token).ConfigureAwait(false);
            if (risk is LocalSecretRisk.Common or LocalSecretRisk.Compromised)
            {
                return Complete(
                    action,
                    AdministratorBootstrapRecoveryResult.Rejected(
                        AdministratorBootstrapRecoveryOutcome.SecretRejected),
                    started);
            }

            if (risk != LocalSecretRisk.Allowed)
            {
                return Complete(action, DependencyUnavailable(), started);
            }

            deadline.Token.ThrowIfCancellationRequested();
            if (authorization.ExpiresAt <= timeProvider.GetUtcNow())
            {
                return Complete(action, AuthorizationRejected(), started);
            }

            using var credential = credentialDeriver.Derive(secret);
            deadline.Token.ThrowIfCancellationRequested();
            if (authorization.ExpiresAt <= timeProvider.GetUtcNow())
            {
                return Complete(action, AuthorizationRejected(), started);
            }

            var result = await mutation(authorization, credential, deadline.Token)
                .ConfigureAwait(false);
            if (result is null
                || (result.Outcome == AdministratorBootstrapRecoveryOutcome.Applied
                    && result.Account?.AccountName != request.TargetAccountName))
            {
                return Complete(action, DependencyUnavailable(), started);
            }

            return Complete(action, result, started);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            LogDependencyFailure(action, started);
            return DependencyUnavailable();
        }
        catch (Exception)
        {
            LogDependencyFailure(action, started);
            return DependencyUnavailable();
        }
    }

    private AuthorizationValidation ValidateAuthorization(
        OutOfBandAuthorizationRequest request,
        OutOfBandAuthorizationVerificationResult? verification)
    {
        if (verification is null)
        {
            return AuthorizationValidation.Failed(DependencyUnavailable());
        }

        if (verification.Status is OutOfBandAuthorizationVerificationStatus.Rejected
            or OutOfBandAuthorizationVerificationStatus.Expired)
        {
            return AuthorizationValidation.Failed(AuthorizationRejected());
        }

        if (verification.Status != OutOfBandAuthorizationVerificationStatus.Verified)
        {
            return AuthorizationValidation.Failed(DependencyUnavailable());
        }

        var authorization = verification.Authorization;
        if (authorization is null
            || !authorization.Matches(request)
            || authorization.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return AuthorizationValidation.Failed(AuthorizationRejected());
        }

        return AuthorizationValidation.Succeeded(authorization);
    }

    private AdministratorBootstrapRecoveryResult Complete(
        string action,
        AdministratorBootstrapRecoveryResult result,
        long started) => AdministratorBootstrapRecoveryLog.CompleteResult(logger, action, result, started);

    private void LogDependencyFailure(string action, long started) =>
        AdministratorBootstrapRecoveryLog.FailedAfter(logger, action, started);

    private static AdministratorBootstrapRecoveryResult AuthorizationRejected() =>
        AdministratorBootstrapRecoveryResult.Rejected(
            AdministratorBootstrapRecoveryOutcome.AuthorizationRejected);

    private static AdministratorBootstrapRecoveryResult DependencyUnavailable() =>
        AdministratorBootstrapRecoveryResult.Rejected(
            AdministratorBootstrapRecoveryOutcome.DependencyUnavailable);

    private static void ValidateMutation(
        Guid operationId,
        OutOfBandAuthorizationProof proof,
        LocalSecret secret)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("An administrator operation ID is required.", nameof(operationId));
        }

        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(secret);
    }

    private sealed record AuthorizationValidation(
        VerifiedOutOfBandAuthorization? Authorization,
        AdministratorBootstrapRecoveryResult? Result)
    {
        public static AuthorizationValidation Succeeded(
            VerifiedOutOfBandAuthorization authorization) => new(authorization, null);

        public static AuthorizationValidation Failed(
            AdministratorBootstrapRecoveryResult result) => new(null, result);
    }
}
