using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class LocalAccountLifecycleContractTests
{
    [TestMethod]
    public void DisplayNamesAreTrimmedBoundedAndControlFree()
    {
        Assert.AreEqual("Managed user", new LocalAccountDisplayName("Managed user").Value);

        foreach (var invalid in new[]
                 {
                     string.Empty,
                     " managed",
                     "managed ",
                     "managed\nuser",
                     new string('a', LocalAccountDisplayName.MaximumLength + 1),
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => _ = new LocalAccountDisplayName(invalid));
        }
    }

    [TestMethod]
    public void LifecycleStateRejectsEmptyIdentifiersAndVersions()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _ = new LocalAccountState(
            Guid.Empty,
            new LocalAccountName("managed-user"),
            new LocalAccountDisplayName("Managed user"),
            false,
            true,
            1,
            1));
        Assert.ThrowsExactly<ArgumentException>(() => _ = new LocalAccountState(
            Guid.NewGuid(),
            new LocalAccountName("managed-user"),
            new LocalAccountDisplayName("Managed user"),
            false,
            true,
            0,
            1));
    }
}

[TestClass]
public sealed class LocalAccountLifecycleServiceTests
{
    [TestMethod]
    public async Task NonAdministratorFailsBeforeRiskOrStorageAccess()
    {
        var store = new FakeLocalAccountLifecycleStore();
        var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
        var deriver = new FakeLocalCredentialDeriver();
        var service = Service(store, risk, deriver);
        using var secret = EligibleSecret();

        var result = await service.ProvisionAsync(
            AuthenticationTestData.Identity(),
            Guid.NewGuid(),
            new LocalAccountName("managed-user"),
            new LocalAccountDisplayName("Managed user"),
            false,
            secret,
            CancellationToken.None);

        Assert.AreEqual(LocalAccountLifecycleOutcome.Unauthorized, result.Outcome);
        Assert.AreEqual(0, risk.Calls);
        Assert.AreEqual(0, deriver.Calls);
        Assert.AreEqual(0, store.ProvisionCalls);
    }

    [TestMethod]
    public async Task ShortCommonCompromisedAndUnavailableSecretsNeverReachStorage()
    {
        foreach (var scenario in new[]
                 {
                     (Secret: "too-short", Risk: LocalSecretRisk.Allowed,
                         Outcome: LocalAccountLifecycleOutcome.SecretRejected, RiskCalls: 0),
                     (Secret: "common password value", Risk: LocalSecretRisk.Common,
                         Outcome: LocalAccountLifecycleOutcome.SecretRejected, RiskCalls: 1),
                     (Secret: "compromised password value", Risk: LocalSecretRisk.Compromised,
                         Outcome: LocalAccountLifecycleOutcome.SecretRejected, RiskCalls: 1),
                     (Secret: "risk source unavailable", Risk: LocalSecretRisk.Unavailable,
                         Outcome: LocalAccountLifecycleOutcome.DependencyUnavailable, RiskCalls: 1),
                 })
        {
            var store = new FakeLocalAccountLifecycleStore();
            var risk = new FakeLocalSecretRiskChecker(scenario.Risk);
            var deriver = new FakeLocalCredentialDeriver();
            var service = Service(store, risk, deriver);
            using var secret = new LocalSecret(scenario.Secret.AsSpan());

            var result = await service.ProvisionAsync(
                AuthenticationTestData.Administrator(),
                Guid.NewGuid(),
                new LocalAccountName("managed-user"),
                new LocalAccountDisplayName("Managed user"),
                false,
                secret,
                CancellationToken.None);

            Assert.AreEqual(scenario.Outcome, result.Outcome, scenario.Risk.ToString());
            Assert.AreEqual(scenario.RiskCalls, risk.Calls);
            Assert.AreEqual(0, deriver.Calls);
            Assert.AreEqual(0, store.ProvisionCalls);
        }
    }

    [TestMethod]
    public async Task AllowedProvisionDerivesOnceWritesOnceAndDisposesMaterial()
    {
        var operationId = Guid.NewGuid();
        var store = new FakeLocalAccountLifecycleStore();
        var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed);
        var deriver = new FakeLocalCredentialDeriver();
        var service = Service(store, risk, deriver);
        using var secret = EligibleSecret();

        var result = await service.ProvisionAsync(
            AuthenticationTestData.Administrator(),
            operationId,
            new LocalAccountName("Managed-User"),
            new LocalAccountDisplayName("Managed user"),
            false,
            secret,
            CancellationToken.None);

        Assert.AreEqual(LocalAccountLifecycleOutcome.Applied, result.Outcome);
        Assert.AreEqual(1, risk.Calls);
        Assert.AreEqual(1, deriver.Calls);
        Assert.AreEqual(1, store.ProvisionCalls);
        Assert.AreEqual(operationId, store.LastOperationId);
        Assert.AreEqual("managed-user", store.LastAccountName.Value);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = deriver.LastMaterial!.Salt);
        Assert.AreEqual("[redacted]", deriver.LastMaterial!.ToString());
    }

    [TestMethod]
    public async Task ReplaceAndEnabledStateCarryExpectedVersions()
    {
        var store = new FakeLocalAccountLifecycleStore();
        var service = Service(
            store,
            new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed),
            new FakeLocalCredentialDeriver());
        using var secret = EligibleSecret();
        var actor = AuthenticationTestData.Administrator();

        var replaced = await service.ReplaceCredentialAsync(
            actor,
            Guid.NewGuid(),
            new LocalAccountName("managed-user"),
            expectedCredentialVersion: 7,
            enableAccount: true,
            secret,
            CancellationToken.None);
        Assert.AreEqual(LocalAccountLifecycleOutcome.Applied, replaced.Outcome);
        Assert.AreEqual(7, store.LastExpectedVersion);
        Assert.IsTrue(store.LastBoolean);
        Assert.AreEqual(1, store.ReplaceCalls);

        var disabled = await service.SetEnabledAsync(
            actor,
            Guid.NewGuid(),
            new LocalAccountName("managed-user"),
            expectedPrincipalSessionVersion: 9,
            enabled: false,
            CancellationToken.None);
        Assert.AreEqual(LocalAccountLifecycleOutcome.Applied, disabled.Outcome);
        Assert.AreEqual(9, store.LastExpectedVersion);
        Assert.IsFalse(store.LastBoolean);
        Assert.AreEqual(1, store.SetEnabledCalls);
    }

    [TestMethod]
    public async Task RiskAndStoreFailuresReturnOnlyDependencyUnavailable()
    {
        var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed)
        {
            Evaluate = _ => throw new InvalidOperationException("sensitive provider detail"),
        };
        var store = new FakeLocalAccountLifecycleStore();
        var service = Service(store, risk, new FakeLocalCredentialDeriver());
        using var secret = EligibleSecret();

        var riskFailure = await service.ProvisionAsync(
            AuthenticationTestData.Administrator(),
            Guid.NewGuid(),
            new LocalAccountName("managed-user"),
            new LocalAccountDisplayName("Managed user"),
            false,
            secret,
            CancellationToken.None);
        Assert.AreEqual(LocalAccountLifecycleOutcome.DependencyUnavailable, riskFailure.Outcome);
        Assert.AreEqual(0, store.ProvisionCalls);

        store.Execute = _ => throw new InvalidOperationException("sensitive database detail");
        var storeFailure = await Service(
            store,
            new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed),
            new FakeLocalCredentialDeriver()).ProvisionAsync(
                AuthenticationTestData.Administrator(),
                Guid.NewGuid(),
                new LocalAccountName("managed-user"),
                new LocalAccountDisplayName("Managed user"),
                false,
                secret,
                CancellationToken.None);
        Assert.AreEqual(LocalAccountLifecycleOutcome.DependencyUnavailable, storeFailure.Outcome);
    }

    [TestMethod]
    public async Task CallerCancellationPropagatesWithoutWriting()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = new FakeLocalAccountLifecycleStore();
        var service = Service(
            store,
            new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed),
            new FakeLocalCredentialDeriver());
        using var secret = EligibleSecret();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await service.ProvisionAsync(
                AuthenticationTestData.Administrator(),
                Guid.NewGuid(),
                new LocalAccountName("managed-user"),
                new LocalAccountDisplayName("Managed user"),
                false,
                secret,
                cancellation.Token));
        Assert.AreEqual(0, store.ProvisionCalls);
    }

    private static LocalAccountLifecycleService Service(
        ILocalAccountLifecycleStore store,
        ILocalSecretRiskChecker risk,
        ILocalCredentialDeriver deriver) =>
        new(store, risk, deriver, NullLogger<LocalAccountLifecycleService>.Instance);

    private static LocalSecret EligibleSecret() =>
        new("V01-012 eligible synthetic secret".AsSpan());
}

[TestClass]
public sealed class LocalAccountLifecycleDeadlineTests
{
    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async Task RiskTimeoutFailsClosedWithoutWriting()
    {
        var risk = new FakeLocalSecretRiskChecker(LocalSecretRisk.Allowed)
        {
            Evaluate = async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return LocalSecretRisk.Allowed;
            },
        };
        var store = new FakeLocalAccountLifecycleStore();
        var service = new LocalAccountLifecycleService(
            store,
            risk,
            new FakeLocalCredentialDeriver(),
            NullLogger<LocalAccountLifecycleService>.Instance);
        using var secret = new LocalSecret("V01-012 eligible synthetic secret".AsSpan());

        var result = await service.ProvisionAsync(
            AuthenticationTestData.Administrator(),
            Guid.NewGuid(),
            new LocalAccountName("managed-user"),
            new LocalAccountDisplayName("Managed user"),
            false,
            secret,
            CancellationToken.None);

        Assert.AreEqual(LocalAccountLifecycleOutcome.DependencyUnavailable, result.Outcome);
        Assert.AreEqual(1, risk.Calls);
        Assert.AreEqual(0, store.ProvisionCalls);
    }
}

[TestClass]
public sealed class LocalCredentialDeriverTests
{
    [TestMethod]
    public void Pbkdf2DerivationUsesFreshSaltAndMatchesTheFrozenVerifier()
    {
        using var secret = new LocalSecret("V01-012 derivation verification secret".AsSpan());
        var deriver = new Pbkdf2LocalCredentialDeriver();
        var verifier = new Pbkdf2LocalSecretVerifier();
        using var first = deriver.Derive(secret);
        using var second = deriver.Derive(secret);

        CollectionAssert.AreNotEqual(first.Salt.ToArray(), second.Salt.ToArray());
        CollectionAssert.AreNotEqual(first.Digest.ToArray(), second.Digest.ToArray());
        Assert.IsTrue(verifier.Verify(
            secret,
            first.Iterations,
            first.Salt.Span,
            first.Digest.Span));
        Assert.AreEqual("[redacted]", first.ToString());
    }
}
