using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using static AssetLibrary.WebGateway.Tests.AdministratorBootstrapRecoveryTestScenario;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class AdministratorBootstrapRecoveryContractTests
{
    [TestMethod]
    public void AuthorizationProofIsBoundedOwnedAndRedacted()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new OutOfBandAuthorizationProof(new byte[15]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new OutOfBandAuthorizationProof(new byte[1025]));

        using var proof = Proof();
        Assert.AreEqual(32, proof.Value.Length);
        Assert.AreEqual("[redacted]", proof.ToString());
        proof.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = proof.Value);
    }

    [TestMethod]
    public void ResultsRequireAccountStateOnlyForAppliedOutcomes()
    {
        var account = AuthenticationTestData.Account(
            accountName: "bootstrap-admin",
            isSystemAdministrator: true);

        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new AdministratorBootstrapRecoveryResult(
                AdministratorBootstrapRecoveryOutcome.Applied,
                wasReplayed: false));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new AdministratorBootstrapRecoveryResult(
                AdministratorBootstrapRecoveryOutcome.StateConflict,
                wasReplayed: false,
                account));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new AdministratorBootstrapRecoveryResult(
                AdministratorBootstrapRecoveryOutcome.Applied,
                wasReplayed: false,
                AuthenticationTestData.Account()));
        Assert.AreEqual(
            AdministratorBootstrapRecoveryOutcome.AuthorizationRejected,
            AdministratorBootstrapRecoveryResult.Rejected(
                AdministratorBootstrapRecoveryOutcome.AuthorizationRejected).Outcome);
    }

    private static OutOfBandAuthorizationProof Proof() =>
        new(Enumerable.Repeat((byte)0x57, 32).ToArray());
}

[TestClass]
public sealed class AdministratorBootstrapRecoveryServiceTests
{
    [TestMethod]
    public async Task AuthorizationFailuresStopBeforeRiskDerivationAndStorage()
    {
        foreach (var scenario in new[]
                 {
                     (
                         Result: OutOfBandAuthorizationVerificationResult.Rejected(),
                         Outcome: AdministratorBootstrapRecoveryOutcome.AuthorizationRejected
                     ),
                     (
                         Result: OutOfBandAuthorizationVerificationResult.Expired(),
                         Outcome: AdministratorBootstrapRecoveryOutcome.AuthorizationRejected
                     ),
                     (
                         Result: OutOfBandAuthorizationVerificationResult.Unavailable(),
                         Outcome: AdministratorBootstrapRecoveryOutcome.DependencyUnavailable
                     ),
                     (
                         Result: new OutOfBandAuthorizationVerificationResult(
                             (OutOfBandAuthorizationVerificationStatus)999),
                         Outcome: AdministratorBootstrapRecoveryOutcome.DependencyUnavailable
                     ),
                 })
        {
            var verifier = new FakeOutOfBandAuthorizationVerifier
            {
                Verify = (_, _) => ValueTask.FromResult(scenario.Result),
            };
            var store = new FakeAdministratorBootstrapRecoveryStore();
            var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
            var deriver = new FakeLocalCredentialDeriver();
            var service = Service(store, verifier, risk, deriver);
            using var proof = Proof();
            using var secret = EligibleSecret();

            var result = await Bootstrap(service, proof, secret);

            Assert.AreEqual(scenario.Outcome, result.Outcome);
            Assert.AreEqual(1, verifier.Calls);
            Assert.AreEqual(0, risk.Calls);
            Assert.AreEqual(0, deriver.Calls);
            Assert.AreEqual(0, store.BootstrapCalls);
        }
    }

    [TestMethod]
    public async Task MismatchedOrExpiredAuthorizationStopsBeforeSecretProcessing()
    {
        Func<OutOfBandAuthorizationRequest, VerifiedOutOfBandAuthorization>[] mismatches =
        [
            request => Authorization(
                request,
                action: AdministratorBootstrapRecoveryAction.RecoverAdministrator),
            request => Authorization(request, operationId: Guid.NewGuid()),
            request => Authorization(
                request,
                targetAccountName: new LocalAccountName("other-admin")),
            request => Authorization(request, expiresAt: request.ExpiresAt.AddSeconds(1)),
        ];

        foreach (var mismatch in mismatches)
        {
            var verifier = new FakeOutOfBandAuthorizationVerifier
            {
                Verify = (request, _) => ValueTask.FromResult(
                    OutOfBandAuthorizationVerificationResult.Verified(mismatch(request))),
            };
            var store = new FakeAdministratorBootstrapRecoveryStore();
            var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
            var deriver = new FakeLocalCredentialDeriver();
            var service = Service(store, verifier, risk, deriver);
            using var proof = Proof();
            using var secret = EligibleSecret();

            var result = await Bootstrap(service, proof, secret);

            Assert.AreEqual(
                AdministratorBootstrapRecoveryOutcome.AuthorizationRejected,
                result.Outcome);
            Assert.AreEqual(0, risk.Calls);
            Assert.AreEqual(0, deriver.Calls);
            Assert.AreEqual(0, store.BootstrapCalls);
        }

        var expiredVerifier = new FakeOutOfBandAuthorizationVerifier();
        var expiredStore = new FakeAdministratorBootstrapRecoveryStore();
        var expiredRisk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
        var expiredDeriver = new FakeLocalCredentialDeriver();
        var expiredService = Service(
            expiredStore,
            expiredVerifier,
            expiredRisk,
            expiredDeriver);
        using var expiredProof = Proof();
        using var expiredSecret = EligibleSecret();

        var expired = await Bootstrap(
            expiredService,
            expiredProof,
            expiredSecret,
            authorizationExpiresAt: Now);

        Assert.AreEqual(
            AdministratorBootstrapRecoveryOutcome.AuthorizationRejected,
            expired.Outcome);
        Assert.AreEqual(0, expiredRisk.Calls);
        Assert.AreEqual(0, expiredDeriver.Calls);
        Assert.AreEqual(0, expiredStore.BootstrapCalls);
    }

}

[TestClass]
public sealed class AdministratorBootstrapRecoveryOperationTests
{
    [TestMethod]
    public async Task AllowedBootstrapBindsRequestDerivesOnceAndWritesAdministratorOnce()
    {
        var operationId = Guid.NewGuid();
        var verifier = new FakeOutOfBandAuthorizationVerifier();
        var store = new FakeAdministratorBootstrapRecoveryStore();
        var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
        var deriver = new FakeLocalCredentialDeriver();
        var service = Service(store, verifier, risk, deriver);
        using var proof = Proof();
        using var secret = EligibleSecret();

        var result = await service.BootstrapFirstAdministratorAsync(
            operationId,
            new LocalAccountName("Bootstrap-Admin"),
            new LocalAccountDisplayName("Bootstrap administrator"),
            Now.AddMinutes(5),
            proof,
            secret,
            CancellationToken.None);

        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.Applied, result.Outcome);
        Assert.AreEqual(1, verifier.Calls);
        Assert.AreEqual(32, verifier.ObservedProofLength);
        Assert.AreEqual(
            AdministratorBootstrapRecoveryAction.BootstrapFirstAdministrator,
            verifier.LastRequest!.Action);
        Assert.AreEqual(operationId, verifier.LastRequest.OperationId);
        Assert.AreEqual("bootstrap-admin", verifier.LastRequest.TargetAccountName.Value);
        Assert.AreEqual(Now.AddMinutes(5), verifier.LastRequest.ExpiresAt);
        Assert.AreEqual(1, risk.Calls);
        Assert.AreEqual(1, deriver.Calls);
        Assert.AreEqual(1, store.BootstrapCalls);
        Assert.AreNotEqual(Guid.Empty, store.LastRequestedPrincipalId);
        Assert.AreEqual("Bootstrap administrator", store.LastDisplayName.Value);
        Assert.IsTrue(result.Account!.IsSystemAdministrator);
        Assert.IsTrue(result.Account.IsEnabled);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = store.LastMaterial!.Salt);
    }

    [TestMethod]
    public async Task RecoveryCarriesExpectedVersionAndRecoveryBinding()
    {
        var operationId = Guid.NewGuid();
        var verifier = new FakeOutOfBandAuthorizationVerifier();
        var store = new FakeAdministratorBootstrapRecoveryStore();
        var service = Service(
            store,
            verifier,
            new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed),
            new FakeLocalCredentialDeriver());
        using var proof = Proof();
        using var secret = EligibleSecret();

        var result = await service.RecoverAdministratorAsync(
            operationId,
            new LocalAccountName("Bootstrap-Admin"),
            expectedCredentialVersion: 7,
            Now.AddMinutes(5),
            proof,
            secret,
            CancellationToken.None);

        Assert.AreEqual(AdministratorBootstrapRecoveryOutcome.Applied, result.Outcome);
        Assert.AreEqual(0, store.BootstrapCalls);
        Assert.AreEqual(1, store.RecoverCalls);
        Assert.AreEqual(7, store.LastExpectedCredentialVersion);
        Assert.AreEqual(
            AdministratorBootstrapRecoveryAction.RecoverAdministrator,
            store.LastAuthorization!.Action);
        Assert.AreEqual(operationId, store.LastAuthorization.OperationId);
        Assert.AreEqual("bootstrap-admin", store.LastAuthorization.TargetAccountName.Value);
    }

}

[TestClass]
public sealed class AdministratorBootstrapRecoverySecretTests
{
    [TestMethod]
    public async Task IneligibleOrUnavailableSecretsNeverReachDerivationOrStorage()
    {
        foreach (var scenario in new[]
                 {
                     (
                         Secret: "too-short",
                         Risk: LocalSecretRisk.Allowed,
                         Outcome: AdministratorBootstrapRecoveryOutcome.SecretRejected,
                         RiskCalls: 0
                     ),
                     (
                         Secret: "common password value",
                         Risk: LocalSecretRisk.Common,
                         Outcome: AdministratorBootstrapRecoveryOutcome.SecretRejected,
                         RiskCalls: 1
                     ),
                     (
                         Secret: "compromised password value",
                         Risk: LocalSecretRisk.Compromised,
                         Outcome: AdministratorBootstrapRecoveryOutcome.SecretRejected,
                         RiskCalls: 1
                     ),
                     (
                         Secret: "risk source unavailable",
                         Risk: LocalSecretRisk.Unavailable,
                         Outcome: AdministratorBootstrapRecoveryOutcome.DependencyUnavailable,
                         RiskCalls: 1
                     ),
                 })
        {
            var verifier = new FakeOutOfBandAuthorizationVerifier();
            var store = new FakeAdministratorBootstrapRecoveryStore();
            var risk = new FakeLocalSecretRiskChecker(scenario.Risk);
            var deriver = new FakeLocalCredentialDeriver();
            var service = Service(store, verifier, risk, deriver);
            using var proof = Proof();
            using var secret = new LocalSecret(scenario.Secret.AsSpan());

            var result = await Bootstrap(service, proof, secret);

            Assert.AreEqual(scenario.Outcome, result.Outcome);
            Assert.AreEqual(1, verifier.Calls);
            Assert.AreEqual(scenario.RiskCalls, risk.Calls);
            Assert.AreEqual(0, deriver.Calls);
            Assert.AreEqual(0, store.BootstrapCalls);
        }
    }

    [TestMethod]
    public async Task ExpiryBeforeOrAfterDerivationStopsBeforeStorage()
    {
        var timeBeforeDerivation = new MutableGatewayTimeProvider(Now);
        var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed)
        {
            Evaluate = _ =>
            {
                timeBeforeDerivation.Advance(TimeSpan.FromMinutes(6));
                return ValueTask.FromResult(LocalSecretRisk.Allowed);
            },
        };
        var firstStore = new FakeAdministratorBootstrapRecoveryStore();
        var firstDeriver = new FakeLocalCredentialDeriver();
        var firstService = Service(
            firstStore,
            new FakeOutOfBandAuthorizationVerifier(),
            risk,
            firstDeriver,
            timeBeforeDerivation);
        using var firstProof = Proof();
        using var firstSecret = EligibleSecret();

        var beforeDerivation = await Bootstrap(firstService, firstProof, firstSecret);

        Assert.AreEqual(
            AdministratorBootstrapRecoveryOutcome.AuthorizationRejected,
            beforeDerivation.Outcome);
        Assert.AreEqual(0, firstDeriver.Calls);
        Assert.AreEqual(0, firstStore.BootstrapCalls);

        var timeAfterDerivation = new MutableGatewayTimeProvider(Now);
        var secondStore = new FakeAdministratorBootstrapRecoveryStore();
        var secondDeriver = new FakeLocalCredentialDeriver
        {
            OnDerive = () => timeAfterDerivation.Advance(TimeSpan.FromMinutes(6)),
        };
        var secondService = Service(
            secondStore,
            new FakeOutOfBandAuthorizationVerifier(),
            new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed),
            secondDeriver,
            timeAfterDerivation);
        using var secondProof = Proof();
        using var secondSecret = EligibleSecret();

        var afterDerivation = await Bootstrap(secondService, secondProof, secondSecret);

        Assert.AreEqual(
            AdministratorBootstrapRecoveryOutcome.AuthorizationRejected,
            afterDerivation.Outcome);
        Assert.AreEqual(1, secondDeriver.Calls);
        Assert.AreEqual(0, secondStore.BootstrapCalls);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = secondDeriver.LastMaterial!.Salt);
    }

}

[TestClass]
public sealed class AdministratorBootstrapRecoveryDependencyTests
{
    [TestMethod]
    public async Task DependencyExceptionsReturnOnlyUnavailableAndNeverEscapeDetails()
    {
        var verifier = new FakeOutOfBandAuthorizationVerifier
        {
            Verify = (_, _) => throw new InvalidOperationException("sensitive verifier detail"),
        };
        var verifierStore = new FakeAdministratorBootstrapRecoveryStore();
        var verifierRisk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
        var verifierDeriver = new FakeLocalCredentialDeriver();
        var verifierService = Service(
            verifierStore,
            verifier,
            verifierRisk,
            verifierDeriver);
        using var proof = Proof();
        using var secret = EligibleSecret();

        var verifierFailure = await Bootstrap(verifierService, proof, secret);

        Assert.AreEqual(
            AdministratorBootstrapRecoveryOutcome.DependencyUnavailable,
            verifierFailure.Outcome);
        Assert.AreEqual(0, verifierRisk.Calls);
        Assert.AreEqual(0, verifierDeriver.Calls);
        Assert.AreEqual(0, verifierStore.BootstrapCalls);

        var storage = new FakeAdministratorBootstrapRecoveryStore
        {
            Execute = _ => throw new InvalidOperationException("sensitive database detail"),
        };
        var storageService = Service(
            storage,
            new FakeOutOfBandAuthorizationVerifier(),
            new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed),
            new FakeLocalCredentialDeriver());

        var storageFailure = await Bootstrap(storageService, proof, secret);

        Assert.AreEqual(
            AdministratorBootstrapRecoveryOutcome.DependencyUnavailable,
            storageFailure.Outcome);
        Assert.AreEqual(1, storage.BootstrapCalls);
    }

    [TestMethod]
    public async Task AppliedStateForAnotherAccountFailsClosed()
    {
        var store = new FakeAdministratorBootstrapRecoveryStore
        {
            Result = new AdministratorBootstrapRecoveryResult(
                AdministratorBootstrapRecoveryOutcome.Applied,
                wasReplayed: false,
                AuthenticationTestData.Account(
                    accountName: "different-admin",
                    isSystemAdministrator: true)),
        };
        var service = Service(
            store,
            new FakeOutOfBandAuthorizationVerifier(),
            new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed),
            new FakeLocalCredentialDeriver());
        using var proof = Proof();
        using var secret = EligibleSecret();

        var result = await Bootstrap(service, proof, secret);

        Assert.AreEqual(
            AdministratorBootstrapRecoveryOutcome.DependencyUnavailable,
            result.Outcome);
        Assert.AreEqual(1, store.BootstrapCalls);
    }
}

[TestClass]
public sealed class AdministratorBootstrapRecoveryCancellationTests
{
    [TestMethod]
    public async Task CallerCancellationPropagatesWithoutAnyDownstreamWrite()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var verifier = new FakeOutOfBandAuthorizationVerifier();
        var store = new FakeAdministratorBootstrapRecoveryStore();
        var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
        var deriver = new FakeLocalCredentialDeriver();
        var service = Service(store, verifier, risk, deriver);
        using var proof = Proof();
        using var secret = EligibleSecret();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await Bootstrap(service, proof, secret, cancellationToken: cancellation.Token));

        Assert.AreEqual(0, risk.Calls);
        Assert.AreEqual(0, deriver.Calls);
        Assert.AreEqual(0, store.BootstrapCalls);
    }

}

internal static class AdministratorBootstrapRecoveryTestScenario
{
    public static readonly DateTimeOffset Now =
        new(2026, 9, 5, 6, 0, 0, TimeSpan.Zero);

    public static AdministratorBootstrapRecoveryService Service(
        IAdministratorBootstrapRecoveryStore store,
        IOutOfBandAuthorizationVerifier verifier,
        ILocalSecretRiskChecker risk,
        ILocalCredentialDeriver deriver,
        TimeProvider? timeProvider = null,
        TimeSpan? timeout = null) =>
        new(
            store,
            verifier,
            risk,
            deriver,
            timeProvider ?? new MutableGatewayTimeProvider(Now),
            timeout ?? TimeSpan.FromSeconds(1),
            NullLogger<AdministratorBootstrapRecoveryService>.Instance);

    public static ValueTask<AdministratorBootstrapRecoveryResult> Bootstrap(
        AdministratorBootstrapRecoveryService service,
        OutOfBandAuthorizationProof proof,
        LocalSecret secret,
        DateTimeOffset? authorizationExpiresAt = null,
        CancellationToken cancellationToken = default) =>
        service.BootstrapFirstAdministratorAsync(
            Guid.NewGuid(),
            new LocalAccountName("bootstrap-admin"),
            new LocalAccountDisplayName("Bootstrap administrator"),
            authorizationExpiresAt ?? Now.AddMinutes(5),
            proof,
            secret,
            cancellationToken);

    public static VerifiedOutOfBandAuthorization Authorization(
        OutOfBandAuthorizationRequest request,
        AdministratorBootstrapRecoveryAction? action = null,
        Guid? operationId = null,
        LocalAccountName? targetAccountName = null,
        DateTimeOffset? expiresAt = null) =>
        new(
            Guid.NewGuid(),
            action ?? request.Action,
            operationId ?? request.OperationId,
            targetAccountName ?? request.TargetAccountName,
            expiresAt ?? request.ExpiresAt);

    public static OutOfBandAuthorizationProof Proof() =>
        new(Enumerable.Repeat((byte)0x57, 32).ToArray());

    public static LocalSecret EligibleSecret() =>
        new("V01-014 eligible synthetic secret".AsSpan());
}

[TestClass]
public sealed class AdministratorBootstrapRecoveryDeadlineTests
{
    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async Task AuthorizationTimeoutFailsClosedBeforeSecretProcessing()
    {
        var verifier = new FakeOutOfBandAuthorizationVerifier
        {
            Verify = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return OutOfBandAuthorizationVerificationResult.Rejected();
            },
        };
        var store = new FakeAdministratorBootstrapRecoveryStore();
        var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
        var deriver = new FakeLocalCredentialDeriver();
        var service = new AdministratorBootstrapRecoveryService(
            store,
            verifier,
            risk,
            deriver,
            new MutableGatewayTimeProvider(
                new DateTimeOffset(2026, 9, 5, 6, 0, 0, TimeSpan.Zero)),
            TimeSpan.FromMilliseconds(50),
            NullLogger<AdministratorBootstrapRecoveryService>.Instance);
        using var proof = new OutOfBandAuthorizationProof(new byte[32]);
        using var secret = new LocalSecret("V01-014 eligible synthetic secret".AsSpan());

        var result = await service.BootstrapFirstAdministratorAsync(
            Guid.NewGuid(),
            new LocalAccountName("bootstrap-admin"),
            new LocalAccountDisplayName("Bootstrap administrator"),
            new DateTimeOffset(2026, 9, 5, 6, 5, 0, TimeSpan.Zero),
            proof,
            secret,
            CancellationToken.None);

        Assert.AreEqual(
            AdministratorBootstrapRecoveryOutcome.DependencyUnavailable,
            result.Outcome);
        Assert.AreEqual(1, verifier.Calls);
        Assert.AreEqual(0, risk.Calls);
        Assert.AreEqual(0, deriver.Calls);
        Assert.AreEqual(0, store.BootstrapCalls);
    }
}
