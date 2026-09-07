using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal interface IOperatorAuthorizationIssuer
{
    ValueTask<OutOfBandAuthorizationProof> IssueAsync(
        AdministratorOperatorRequest request,
        CancellationToken cancellationToken);
}

internal sealed class OperatorAuthorizationRejectedException : InvalidOperationException
{
    public OperatorAuthorizationRejectedException()
        : base("The operator authorization is invalid or expired.")
    {
    }
}

internal interface IGatewayAuthorizationKeyLifecycle
{
    ValueTask<GatewayAuthorizationKeyState> InitializeAsync(CancellationToken cancellationToken);

    ValueTask<GatewayAuthorizationKeyState> RotateAsync(CancellationToken cancellationToken);
}

internal enum AdministratorRecoveryPreparationOutcome
{
    Ready = 0,
    AuthorizationRejected = 1,
    StateConflict = 2,
    RequestConflict = 3,
}

internal sealed record AdministratorRecoveryPreparationResult(
    AdministratorRecoveryPreparationOutcome Outcome,
    long? ExpectedCredentialVersion = null);

internal interface IAdministratorRecoveryPreparationStore
{
    ValueTask<AdministratorRecoveryPreparationResult> PrepareRecoveryAsync(
        VerifiedOutOfBandAuthorization authorization,
        CancellationToken cancellationToken);
}
