using System.Net;
using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class PwnedPasswordsCacheTests
{
    [TestMethod]
    public async Task FreshPrefixRangeIsReusedWithoutASecondRequest()
    {
        const string secretText = "cached compromised synthetic secret";
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse(secretText, matchingCount: 7))));
        using var checker = PwnedPasswordsTestData.Checker(handler);
        using var secret = new LocalSecret(secretText.AsSpan());

        Assert.AreEqual(
            LocalSecretRisk.Compromised,
            await checker.EvaluateAsync(secret, CancellationToken.None));
        Assert.AreEqual(
            LocalSecretRisk.Compromised,
            await checker.EvaluateAsync(secret, CancellationToken.None));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task CacheStoresThePrefixRangeRatherThanOnePasswordsOutcome()
    {
        const string compromisedText = "cache-prefix-synthetic-1014";
        const string allowedText = "cache-prefix-synthetic-2592";
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse(compromisedText, matchingCount: 5))));
        using var checker = PwnedPasswordsTestData.Checker(handler);
        using var compromised = new LocalSecret(compromisedText.AsSpan());
        using var allowed = new LocalSecret(allowedText.AsSpan());
        using var compromisedQuery = PwnedPasswordQuery.Create(compromised);
        using var allowedQuery = PwnedPasswordQuery.Create(allowed);
        Assert.AreEqual(compromisedQuery.Prefix, allowedQuery.Prefix);
        Assert.AreNotEqual(
            new string(compromisedQuery.Suffix),
            new string(allowedQuery.Suffix));

        Assert.AreEqual(
            LocalSecretRisk.Compromised,
            await checker.EvaluateAsync(compromised, CancellationToken.None));
        Assert.AreEqual(
            LocalSecretRisk.Allowed,
            await checker.EvaluateAsync(allowed, CancellationToken.None));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task FailedResponsesAreNotCached()
    {
        const string secretText = "failure cache synthetic secret";
        var responses = new Queue<HttpResponseMessage>(
        [
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse(secretText)),
        ]);
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(responses.Dequeue()));
        using var checker = PwnedPasswordsTestData.Checker(handler);
        using var secret = new LocalSecret(secretText.AsSpan());

        Assert.AreEqual(
            LocalSecretRisk.Unavailable,
            await checker.EvaluateAsync(secret, CancellationToken.None));
        Assert.AreEqual(
            LocalSecretRisk.Allowed,
            await checker.EvaluateAsync(secret, CancellationToken.None));
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task ExpiredAllowedRangeIsNeverUsedAsOfflineFallbackAfterWallClockRollback()
    {
        const string secretText = "stale allow synthetic secret";
        var now = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
        var time = new MutableGatewayTimeProvider(now);
        var responses = new Queue<HttpResponseMessage>(
        [
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse(secretText)),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        ]);
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(responses.Dequeue()));
        using var checker = PwnedPasswordsTestData.Checker(
            handler,
            PwnedPasswordsTestData.Options(cacheLifetime: TimeSpan.FromMinutes(1)),
            time);
        using var secret = new LocalSecret(secretText.AsSpan());

        Assert.AreEqual(
            LocalSecretRisk.Allowed,
            await checker.EvaluateAsync(secret, CancellationToken.None));
        time.RewindUtc(TimeSpan.FromDays(1));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.AreEqual(
            LocalSecretRisk.Unavailable,
            await checker.EvaluateAsync(secret, CancellationToken.None));
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task CapacityEvictsTheLeastRecentlyUsedPrefix()
    {
        const string firstText = "first lru synthetic secret";
        const string secondText = "second lru synthetic secret";
        const string thirdText = "third lru synthetic secret";
        var body = PwnedPasswordsTestData.ValidResponse("unrelated response seed");
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            PwnedPasswordsTestData.TextResponse(body)));
        using var checker = PwnedPasswordsTestData.Checker(
            handler,
            PwnedPasswordsTestData.Options(cacheCapacity: 2));
        using var first = new LocalSecret(firstText.AsSpan());
        using var second = new LocalSecret(secondText.AsSpan());
        using var third = new LocalSecret(thirdText.AsSpan());
        using var firstQuery = PwnedPasswordQuery.Create(first);
        using var secondQuery = PwnedPasswordQuery.Create(second);
        using var thirdQuery = PwnedPasswordQuery.Create(third);
        Assert.AreEqual(3, new[]
        {
            firstQuery.Prefix,
            secondQuery.Prefix,
            thirdQuery.Prefix,
        }.Distinct(StringComparer.Ordinal).Count());

        _ = await checker.EvaluateAsync(first, CancellationToken.None);
        _ = await checker.EvaluateAsync(second, CancellationToken.None);
        _ = await checker.EvaluateAsync(first, CancellationToken.None);
        _ = await checker.EvaluateAsync(third, CancellationToken.None);
        _ = await checker.EvaluateAsync(second, CancellationToken.None);

        Assert.AreEqual(4, handler.Calls);
    }

    [TestMethod]
    public async Task ConcurrentCacheChurnRemainsBoundedAndConsistent()
    {
        var body = PwnedPasswordsTestData.ValidResponse("concurrency response seed");
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            PwnedPasswordsTestData.TextResponse(body)));
        using var checker = PwnedPasswordsTestData.Checker(
            handler,
            PwnedPasswordsTestData.Options(cacheCapacity: 2));
        var values = Enumerable.Range(0, 24)
            .Select(index => $"concurrent synthetic secret {index:D2}")
            .ToArray();

        var outcomes = await Task.WhenAll(values.Select(async value =>
        {
            using var secret = new LocalSecret(value.AsSpan());
            return await checker.EvaluateAsync(secret, CancellationToken.None);
        }));

        Assert.IsTrue(outcomes.All(static outcome => outcome == LocalSecretRisk.Allowed));
        Assert.AreEqual(values.Length, handler.Calls);
    }

    [TestMethod]
    public async Task DisposedCheckerRejectsFurtherEvaluation()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse("disposed synthetic secret"))));
        var checker = PwnedPasswordsTestData.Checker(handler);
        checker.Dispose();
        using var secret = new LocalSecret("disposed synthetic secret".AsSpan());

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
            await checker.EvaluateAsync(secret, CancellationToken.None));
    }
}

[TestClass]
public sealed class PwnedPasswordsLifecycleIntegrationTests
{
    [TestMethod]
    public async Task CompromisedAndUnavailableProviderResultsNeverReachCredentialStorage()
    {
        const string compromisedText = "known compromised synthetic secret";
        var compromisedHandler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            PwnedPasswordsTestData.TextResponse(
                PwnedPasswordsTestData.ValidResponse(compromisedText, matchingCount: 9))));
        using var compromisedChecker = PwnedPasswordsTestData.Checker(compromisedHandler);
        var compromisedStore = new FakeLocalAccountLifecycleStore();
        var compromisedDeriver = new FakeLocalCredentialDeriver();
        using var compromised = new LocalSecret(compromisedText.AsSpan());

        var rejected = await Service(
            compromisedStore,
            compromisedChecker,
            compromisedDeriver).ProvisionAsync(
                AuthenticationTestData.Administrator(),
                Guid.NewGuid(),
                new LocalAccountName("managed-user"),
                new LocalAccountDisplayName("Managed user"),
                false,
                compromised,
                CancellationToken.None);

        Assert.AreEqual(LocalAccountLifecycleOutcome.SecretRejected, rejected.Outcome);
        Assert.AreEqual(0, compromisedDeriver.Calls);
        Assert.AreEqual(0, compromisedStore.ProvisionCalls);

        var unavailableHandler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var unavailableChecker = PwnedPasswordsTestData.Checker(unavailableHandler);
        var unavailableStore = new FakeLocalAccountLifecycleStore();
        var unavailableDeriver = new FakeLocalCredentialDeriver();
        using var unavailable = new LocalSecret("unavailable provider synthetic secret".AsSpan());

        var failed = await Service(
            unavailableStore,
            unavailableChecker,
            unavailableDeriver).ProvisionAsync(
                AuthenticationTestData.Administrator(),
                Guid.NewGuid(),
                new LocalAccountName("managed-user"),
                new LocalAccountDisplayName("Managed user"),
                false,
                unavailable,
                CancellationToken.None);

        Assert.AreEqual(LocalAccountLifecycleOutcome.DependencyUnavailable, failed.Outcome);
        Assert.AreEqual(0, unavailableDeriver.Calls);
        Assert.AreEqual(0, unavailableStore.ProvisionCalls);
    }

    private static LocalAccountLifecycleService Service(
        ILocalAccountLifecycleStore store,
        ILocalSecretRiskChecker risk,
        ILocalCredentialDeriver deriver) =>
        new(store, risk, deriver, NullLogger<LocalAccountLifecycleService>.Instance);
}
