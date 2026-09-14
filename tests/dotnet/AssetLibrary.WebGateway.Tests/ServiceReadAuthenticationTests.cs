using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class ServiceReadAuthenticationTests
{
    [TestMethod]
    public async Task IssueUsesThirtyDaysDistinctOpaqueSecretsAndRedacts()
    {
        var clock = new FixedClock();
        var store = new CredentialStore();
        var service = new ServiceReadAuthenticationService(store, timeProvider: clock);
        using var first = await service.IssueAsync(Guid.NewGuid(), null, null, Operation(), CancellationToken.None);
        using var second = await service.IssueAsync(Guid.NewGuid(), null, null, Operation(), CancellationToken.None);
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(clock.GetUtcNow().AddDays(30), first.ExpiresAt);
        Assert.AreEqual(43, first.Token.Export().Length);
        Assert.AreNotEqual(first.Token.Export(), second.Token.Export());
        Assert.AreEqual("[redacted]", first.ToString());
        Assert.AreEqual("[redacted]", first.Token.ToString());
        using var parsed = ServiceReadToken.Parse(first.Token.Export());
        Assert.AreEqual(first.Token.Export(), parsed.Export());
        parsed.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => parsed.Export());
    }

    [TestMethod]
    public async Task ExpiryBoundsAndStoreFailureNeverReturnCredential()
    {
        var store = new CredentialStore { IssueAllowed = false };
        var service = new ServiceReadAuthenticationService(store, maximumLifetimeDays: 30);
        Assert.IsNull(await service.IssueAsync(Guid.NewGuid(), null, null, Operation(), CancellationToken.None));
        foreach (var duration in new[] { TimeSpan.Zero, TimeSpan.FromDays(-1), TimeSpan.FromDays(31) })
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
                await service.IssueAsync(Guid.NewGuid(), null, duration, Operation(), CancellationToken.None));
        Assert.AreEqual(1, store.IssueCalls);
        Assert.ThrowsExactly<ArgumentException>(() => ServiceReadToken.Parse("invalid"));
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceReadOperation("operator\n", Guid.NewGuid()));
    }

    [TestMethod]
    public async Task AuthenticationRechecksStoreEveryRequestAndPropagatesFailure()
    {
        var store = new CredentialStore();
        var service = new ServiceReadAuthenticationService(store);
        using var credential = await service.IssueAsync(Guid.NewGuid(), null, null, Operation(), CancellationToken.None);
        Assert.IsNotNull(credential);
        store.Identity = new ServiceReadIdentity(Guid.NewGuid(), new AuthenticatedSubject("service:test"));
        Assert.IsNotNull(await service.AuthenticateAsync(credential.Token, CancellationToken.None));
        store.Identity = null;
        Assert.IsNull(await service.AuthenticateAsync(credential.Token, CancellationToken.None));
        store.FailAuthentication = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await service.AuthenticateAsync(credential.Token, CancellationToken.None));
        Assert.AreEqual(3, store.AuthenticateCalls);
    }

    private static ServiceReadOperation Operation() => new("synthetic-operator", Guid.NewGuid());
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
    }
    private sealed class CredentialStore : IServiceReadCredentialStore
    {
        public bool IssueAllowed { get; set; } = true;
        public int IssueCalls { get; private set; }
        public int AuthenticateCalls { get; private set; }
        public bool FailAuthentication { get; set; }
        public ServiceReadIdentity? Identity { get; set; }
        public ValueTask<bool> CreatePrincipalAsync(Guid principalId, string displayName, ServiceReadOperation operation, CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask<bool> IssueAsync(Guid principalId, Guid credentialId, Guid? replacedCredentialId,
            AuthenticationSecretDigest digest, DateTimeOffset expiresAt, ServiceReadOperation operation, CancellationToken cancellationToken)
        {
            IssueCalls++;
            Assert.AreEqual("[redacted]", digest.ToString());
            return ValueTask.FromResult(IssueAllowed);
        }
        public ValueTask<ServiceReadIdentity?> AuthenticateAsync(AuthenticationSecretDigest digest, CancellationToken cancellationToken)
        {
            AuthenticateCalls++;
            if (FailAuthentication) throw new InvalidOperationException("Synthetic storage unavailable.");
            return ValueTask.FromResult(Identity);
        }
        public ValueTask<bool> RevokeAsync(Guid principalId, Guid credentialId, ServiceReadOperation operation, CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask<bool> DisableAsync(Guid principalId, ServiceReadOperation operation, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
}
