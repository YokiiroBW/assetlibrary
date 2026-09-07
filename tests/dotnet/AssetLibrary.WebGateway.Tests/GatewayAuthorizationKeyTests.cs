using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using static AssetLibrary.WebGateway.Tests.GatewayAuthorizationKeyTests;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class GatewayAuthorizationKeyTests
{
    [TestMethod]
    public async Task EncryptedKeyAndBoundProofSurviveProviderRestartAndRotationRevokesOldProof()
    {
        using var sandbox = new GatewayAuthorizationSandbox();
        var clock = new MutableGatewayTimeProvider(DateTimeOffset.UtcNow);
        var keys = new ProtectedGatewayAuthorizationKeyStore(sandbox.Configuration, sandbox.Protection(), clock);
        var original = await keys.InitializeAsync(CancellationToken.None);
        var request = Request(clock.GetUtcNow());
        var authorizer = new FileOutOfBandAuthorization(keys, sandbox.Configuration.DeploymentId, clock);
        using var proof = await authorizer.IssueAsync(request, CancellationToken.None);
        using (var material = await keys.ReadAsync(CancellationToken.None))
        {
            var bytes = await File.ReadAllBytesAsync(sandbox.Configuration.KeyFilePath);
            Assert.AreEqual(-1, bytes.AsSpan().IndexOf(material.Secret));
        }

        var restarted = new ProtectedGatewayAuthorizationKeyStore(sandbox.Configuration, sandbox.Protection(), clock);
        var verifier = new FileOutOfBandAuthorization(restarted, sandbox.Configuration.DeploymentId, clock);
        var accepted = await verifier.VerifyAsync(ToVerification(request), proof, CancellationToken.None);
        Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Verified, accepted.Status);
        Assert.AreEqual(request.AuthorizationId, accepted.Authorization!.AuthorizationId);

        var rotated = await restarted.RotateAsync(CancellationToken.None);
        Assert.AreNotEqual(original.KeyId, rotated.KeyId);
        Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Rejected,
            (await authorizer.VerifyAsync(ToVerification(request), proof, CancellationToken.None)).Status);
        using var replacement = await authorizer.IssueAsync(request, CancellationToken.None);
        Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Verified,
            (await verifier.VerifyAsync(ToVerification(request), replacement, CancellationToken.None)).Status);
        Assert.IsEmpty(Directory.GetFiles(sandbox.Path, "*.tmp"));
    }

    [TestMethod]
    public async Task EveryAuthorizationBindingAndProofTamperingFailsClosed()
    {
        using var sandbox = new GatewayAuthorizationSandbox();
        var clock = new MutableGatewayTimeProvider(DateTimeOffset.UtcNow);
        var keys = new ProtectedGatewayAuthorizationKeyStore(sandbox.Configuration, sandbox.Protection(), clock);
        await keys.InitializeAsync(CancellationToken.None);
        var authorizer = new FileOutOfBandAuthorization(keys, sandbox.Configuration.DeploymentId, clock);
        var request = Request(clock.GetUtcNow());
        using var proof = await authorizer.IssueAsync(request, CancellationToken.None);
        OutOfBandAuthorizationRequest[] changed =
        [
            new(request.Action, Guid.NewGuid(), request.AccountName, request.ExpiresAt),
            new(AdministratorBootstrapRecoveryAction.RecoverAdministrator,
                request.OperationId, request.AccountName, request.ExpiresAt),
            new(request.Action, request.OperationId, new LocalAccountName("another-admin"), request.ExpiresAt),
            new(request.Action, request.OperationId, request.AccountName, request.ExpiresAt.AddSeconds(1)),
        ];
        foreach (var mismatch in changed)
        {
            Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Rejected,
                (await authorizer.VerifyAsync(mismatch, proof, CancellationToken.None)).Status);
        }

        var changedBytes = proof.Value.ToArray();
        changedBytes[^1] ^= 1;
        using var tampered = new OutOfBandAuthorizationProof(changedBytes);
        Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Rejected,
            (await authorizer.VerifyAsync(ToVerification(request), tampered, CancellationToken.None)).Status);
        var otherDeployment = new FileOutOfBandAuthorization(keys, Guid.NewGuid(), clock);
        Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Rejected,
            (await otherDeployment.VerifyAsync(ToVerification(request), proof, CancellationToken.None)).Status);
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Expired,
            (await authorizer.VerifyAsync(ToVerification(request), proof, CancellationToken.None)).Status);
    }

    internal static AdministratorOperatorRequest Request(DateTimeOffset now) => new(
        Guid.NewGuid(), Guid.NewGuid(), AdministratorBootstrapRecoveryAction.BootstrapFirstAdministrator,
        new LocalAccountName("bootstrap-admin"), new LocalAccountDisplayName("Trial administrator"), now.AddMinutes(5));

    internal static OutOfBandAuthorizationRequest ToVerification(AdministratorOperatorRequest request) =>
        new(request.Action, request.OperationId, request.AccountName, request.ExpiresAt);
}

[TestClass]
public sealed class GatewayAuthorizationKeyFailureTests
{
    [TestMethod]
    public async Task CorruptMissingOrUnrelatedProtectionKeyCannotAuthorizeOrSilentlyReinitialize()
    {
        using var sandbox = new GatewayAuthorizationSandbox();
        var clock = new MutableGatewayTimeProvider(DateTimeOffset.UtcNow);
        var keys = new ProtectedGatewayAuthorizationKeyStore(sandbox.Configuration, sandbox.Protection(), clock);
        await keys.InitializeAsync(CancellationToken.None);
        var request = Request(clock.GetUtcNow());
        var authorizer = new FileOutOfBandAuthorization(keys, sandbox.Configuration.DeploymentId, clock);
        using var proof = await authorizer.IssueAsync(request, CancellationToken.None);
        var wrongProtection = new ProtectedGatewayAuthorizationKeyStore(
            sandbox.Configuration, new EphemeralDataProtectionProvider(), clock);
        var wrongVerifier = new FileOutOfBandAuthorization(wrongProtection, sandbox.Configuration.DeploymentId, clock);
        Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Unavailable,
            (await wrongVerifier.VerifyAsync(ToVerification(request), proof, CancellationToken.None)).Status);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => keys.InitializeAsync(CancellationToken.None).AsTask());

        await File.WriteAllBytesAsync(sandbox.Configuration.KeyFilePath, new byte[] { 1, 2, 3 });
        Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Unavailable,
            (await authorizer.VerifyAsync(ToVerification(request), proof, CancellationToken.None)).Status);
        File.Delete(sandbox.Configuration.KeyFilePath);
        Assert.AreEqual(OutOfBandAuthorizationVerificationStatus.Unavailable,
            (await authorizer.VerifyAsync(ToVerification(request), proof, CancellationToken.None)).Status);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => keys.RotateAsync(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task ConcurrentInitializationPublishesExactlyOneKeyAndCancellationPropagates()
    {
        using var sandbox = new GatewayAuthorizationSandbox();
        var clock = new MutableGatewayTimeProvider(DateTimeOffset.UtcNow);
        var protection = sandbox.Protection();
        var keys = new ProtectedGatewayAuthorizationKeyStore(sandbox.Configuration, protection, clock);
        var other = new ProtectedGatewayAuthorizationKeyStore(sandbox.Configuration, protection, clock);
        var outcomes = await Task.WhenAll(Initialize(keys), Initialize(other));
        Assert.AreEqual(1, outcomes.Count(value => value));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => keys.RotateAsync(cancelled.Token).AsTask());
        var request = Request(clock.GetUtcNow());
        var authorizer = new FileOutOfBandAuthorization(keys, sandbox.Configuration.DeploymentId, clock);
        using var proof = await authorizer.IssueAsync(request, CancellationToken.None);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            authorizer.VerifyAsync(ToVerification(request), proof, cancelled.Token).AsTask());
    }

    private static async Task<bool> Initialize(ProtectedGatewayAuthorizationKeyStore keys)
    {
        try
        {
            await keys.InitializeAsync(CancellationToken.None);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

}
