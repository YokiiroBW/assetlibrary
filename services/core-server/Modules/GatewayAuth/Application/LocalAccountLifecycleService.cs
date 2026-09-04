using System.Diagnostics;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal sealed class LocalAccountLifecycleService
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);
    private readonly ILocalAccountLifecycleStore store;
    private readonly ILocalSecretRiskChecker riskChecker;
    private readonly ILocalCredentialDeriver credentialDeriver;
    private readonly ILogger<LocalAccountLifecycleService> logger;

    public LocalAccountLifecycleService(
        ILocalAccountLifecycleStore store,
        ILocalSecretRiskChecker riskChecker,
        ILogger<LocalAccountLifecycleService>? logger = null)
        : this(
            store,
            riskChecker,
            new Pbkdf2LocalCredentialDeriver(),
            logger ?? NullLogger<LocalAccountLifecycleService>.Instance)
    {
    }

    internal LocalAccountLifecycleService(
        ILocalAccountLifecycleStore store,
        ILocalSecretRiskChecker riskChecker,
        ILocalCredentialDeriver credentialDeriver,
        ILogger<LocalAccountLifecycleService> logger)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.riskChecker = riskChecker ?? throw new ArgumentNullException(nameof(riskChecker));
        this.credentialDeriver = credentialDeriver
            ?? throw new ArgumentNullException(nameof(credentialDeriver));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<LocalAccountLifecycleResult> FindAsync(
        AuthenticatedIdentity actor,
        LocalAccountName accountName,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            actor,
            "find",
            token => store.FindAsync(actor, accountName, token),
            cancellationToken);

    public ValueTask<LocalAccountLifecycleResult> ProvisionAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        LocalAccountDisplayName displayName,
        bool isSystemAdministrator,
        LocalSecret newSecret,
        CancellationToken cancellationToken)
    {
        ValidateMutation(operationId, newSecret);
        return ExecuteSecretMutationAsync(
            actor,
            "provision",
            newSecret,
            (credential, token) => store.ProvisionAsync(
                actor,
                operationId,
                Guid.NewGuid(),
                accountName,
                displayName,
                isSystemAdministrator,
                credential,
                token),
            cancellationToken);
    }

    public ValueTask<LocalAccountLifecycleResult> ReplaceCredentialAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        long expectedCredentialVersion,
        bool enableAccount,
        LocalSecret newSecret,
        CancellationToken cancellationToken)
    {
        ValidateMutation(operationId, newSecret);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedCredentialVersion);

        return ExecuteSecretMutationAsync(
            actor,
            "replace_credential",
            newSecret,
            (credential, token) => store.ReplaceCredentialAsync(
                actor,
                operationId,
                accountName,
                expectedCredentialVersion,
                enableAccount,
                credential,
                token),
            cancellationToken);
    }

    public ValueTask<LocalAccountLifecycleResult> SetEnabledAsync(
        AuthenticatedIdentity actor,
        Guid operationId,
        LocalAccountName accountName,
        long expectedPrincipalSessionVersion,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A lifecycle operation ID is required.", nameof(operationId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedPrincipalSessionVersion);

        return ExecuteAsync(
            actor,
            enabled ? "enable" : "disable",
            token => store.SetEnabledAsync(
                actor,
                operationId,
                accountName,
                expectedPrincipalSessionVersion,
                enabled,
                token),
            cancellationToken);
    }

    private async ValueTask<LocalAccountLifecycleResult> ExecuteSecretMutationAsync(
        AuthenticatedIdentity actor,
        string action,
        LocalSecret secret,
        Func<LocalCredentialEnrollmentMaterial, CancellationToken,
            ValueTask<LocalAccountLifecycleResult>> mutation,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            actor,
            action,
            async token =>
            {
                if (!secret.MeetsEnrollmentLengthPolicy)
                {
                    return LocalAccountLifecycleResult.Rejected(
                        LocalAccountLifecycleOutcome.SecretRejected);
                }

                var risk = await riskChecker.EvaluateAsync(secret, token).ConfigureAwait(false);
                if (risk is LocalSecretRisk.Common or LocalSecretRisk.Compromised)
                {
                    return LocalAccountLifecycleResult.Rejected(
                        LocalAccountLifecycleOutcome.SecretRejected);
                }

                if (risk != LocalSecretRisk.Allowed)
                {
                    return LocalAccountLifecycleResult.Rejected(
                        LocalAccountLifecycleOutcome.DependencyUnavailable);
                }

                token.ThrowIfCancellationRequested();
                using var credential = credentialDeriver.Derive(secret);
                token.ThrowIfCancellationRequested();
                return await mutation(credential, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<LocalAccountLifecycleResult> ExecuteAsync(
        AuthenticatedIdentity actor,
        string action,
        Func<CancellationToken, ValueTask<LocalAccountLifecycleResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(operation);
        if (!actor.IsSystemAdministrator)
        {
            return LocalAccountLifecycleResult.Rejected(
                LocalAccountLifecycleOutcome.Unauthorized);
        }

        var started = Stopwatch.GetTimestamp();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(OperationTimeout);
        try
        {
            var result = await operation(deadline.Token).ConfigureAwait(false);
            var elapsedMilliseconds =
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (logger.IsEnabled(LogLevel.Information))
            {
                LocalAccountLifecycleLog.Completed(
                    logger,
                    action,
                    (int)result.Outcome,
                    elapsedMilliseconds);
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            LogDependencyFailure(action, started);
            return LocalAccountLifecycleResult.Rejected(
                LocalAccountLifecycleOutcome.DependencyUnavailable);
        }
        catch (Exception)
        {
            LogDependencyFailure(action, started);
            return LocalAccountLifecycleResult.Rejected(
                LocalAccountLifecycleOutcome.DependencyUnavailable);
        }
    }

    private void LogDependencyFailure(string action, long started)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LocalAccountLifecycleLog.DependencyFailed(
                logger,
                action,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private static void ValidateMutation(Guid operationId, LocalSecret secret)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A lifecycle operation ID is required.", nameof(operationId));
        }

        ArgumentNullException.ThrowIfNull(secret);
    }
}
