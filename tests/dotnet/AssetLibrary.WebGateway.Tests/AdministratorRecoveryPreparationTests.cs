using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class AdministratorRecoveryPreparationTests
{
    [TestMethod]
    public async Task AutomaticRecoveryUsesPreparedVersionAndKeepsRiskBeforeEveryDatabaseCall()
    {
        var store = new FakeAdministratorBootstrapRecoveryStore();
        var preparation = new PreparationStore(new(AdministratorRecoveryPreparationOutcome.Ready, 7));
        var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
        var service = Service(store, preparation, risk, new FakeOutOfBandAuthorizationVerifier());
        using var proof = new OutOfBandAuthorizationProof(new byte[32]);
        using var secret = new LocalSecret("long trial account passphrase");

        var result = await Recover(service, proof, secret);

        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.Applied, result.Outcome);
        Assert.AreEqual(7L, store.LastExpectedCredentialVersion);
        Assert.AreEqual(1, preparation.Calls);
        Assert.AreEqual(1, risk.Calls);

        var unavailableRisk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Unavailable);
        var unavailable = Service(store, preparation, unavailableRisk, new FakeOutOfBandAuthorizationVerifier());
        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.DependencyUnavailable,
            (await Recover(unavailable, proof, secret)).Outcome);
        Assert.AreEqual(1, preparation.Calls);
        Assert.AreEqual(1, store.RecoverCalls);
    }

    [TestMethod]
    public async Task RejectedAuthorizationAndPreparationNeverMutateCredentials()
    {
        var store = new FakeAdministratorBootstrapRecoveryStore();
        var preparation = new PreparationStore(new(AdministratorRecoveryPreparationOutcome.RequestConflict));
        var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
        var verifier = new FakeOutOfBandAuthorizationVerifier
        {
            Verify = (_, _) => ValueTask.FromResult(OutOfBandAuthorizationVerificationResult.Rejected()),
        };
        using var proof = new OutOfBandAuthorizationProof(new byte[32]);
        using var secret = new LocalSecret("long trial account passphrase");
        var denied = await Recover(Service(store, preparation, risk, verifier), proof, secret);
        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.AuthorizationRejected, denied.Outcome);
        Assert.AreEqual(0, risk.Calls);
        Assert.AreEqual(0, preparation.Calls);

        var conflict = await Recover(
            Service(store, preparation, risk, new FakeOutOfBandAuthorizationVerifier()), proof, secret);
        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.RequestConflict, conflict.Outcome);
        Assert.AreEqual(0, store.RecoverCalls);
    }

    private static AdministratorBootstrapRecoveryService Service(
        FakeAdministratorBootstrapRecoveryStore store, PreparationStore preparation,
        FakeLocalSecretRiskChecker risk, FakeOutOfBandAuthorizationVerifier verifier) =>
        new(store, verifier, risk, new FakeLocalCredentialDeriver(), TimeProvider.System, TimeSpan.FromSeconds(5),
            NullLogger<AdministratorBootstrapRecoveryService>.Instance, new AdministratorRecoveryMutation(store, preparation));

    private static ValueTask<AdministratorBootstrapRecoveryResult> Recover(
        AdministratorBootstrapRecoveryService service, OutOfBandAuthorizationProof proof, LocalSecret secret) =>
        service.RecoverAdministratorAsync(Guid.NewGuid(), new LocalAccountName("bootstrap-admin"),
            DateTimeOffset.UtcNow.AddMinutes(5), proof, secret, CancellationToken.None);

    private sealed class PreparationStore(AdministratorRecoveryPreparationResult result) : IAdministratorRecoveryPreparationStore
    {
        public int Calls { get; private set; }

        public ValueTask<AdministratorRecoveryPreparationResult> PrepareRecoveryAsync(
            VerifiedOutOfBandAuthorization authorization, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(result);
        }
    }
}
