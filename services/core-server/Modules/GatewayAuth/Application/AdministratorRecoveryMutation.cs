using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.Modules.GatewayAuth.Application;

internal sealed class AdministratorRecoveryMutation(
    IAdministratorBootstrapRecoveryStore store,
    IAdministratorRecoveryPreparationStore preparation)
{
    private readonly IAdministratorBootstrapRecoveryStore store =
        store ?? throw new ArgumentNullException(nameof(store));
    private readonly IAdministratorRecoveryPreparationStore preparation =
        preparation ?? throw new ArgumentNullException(nameof(preparation));

    public async ValueTask<AdministratorBootstrapRecoveryResult> ExecuteAsync(
        VerifiedOutOfBandAuthorization authorization,
        LocalCredentialEnrollmentMaterial credential,
        CancellationToken cancellationToken)
    {
        var prepared = await preparation.PrepareRecoveryAsync(authorization, cancellationToken).ConfigureAwait(false);
        if (prepared.Outcome == AdministratorRecoveryPreparationOutcome.Ready
            && prepared.ExpectedCredentialVersion is > 0)
        {
            return await store.RecoverAsync(
                authorization, prepared.ExpectedCredentialVersion.Value, credential, cancellationToken)
                .ConfigureAwait(false);
        }

        return AdministratorBootstrapRecoveryResult.Rejected(prepared.Outcome switch
        {
            AdministratorRecoveryPreparationOutcome.AuthorizationRejected =>
                AdministratorBootstrapRecoveryOutcome.AuthorizationRejected,
            AdministratorRecoveryPreparationOutcome.StateConflict => AdministratorBootstrapRecoveryOutcome.StateConflict,
            AdministratorRecoveryPreparationOutcome.RequestConflict => AdministratorBootstrapRecoveryOutcome.RequestConflict,
            _ => AdministratorBootstrapRecoveryOutcome.DependencyUnavailable,
        });
    }
}
