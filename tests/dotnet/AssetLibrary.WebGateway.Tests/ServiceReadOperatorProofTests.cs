using AssetLibrary.Modules.GatewayAuth.Contracts;
using AssetLibrary.Modules.GatewayAuth.Infrastructure;

namespace AssetLibrary.WebGateway.Tests;

[TestClass]
public sealed class ServiceReadOperatorProofTests
{
    [TestMethod]
    public async Task ServiceProofBindsEveryFieldAndRejectsRecoveryProofExpiryAndRotatedKey()
    {
        using var sandbox = new GatewayAuthorizationSandbox();
        var clock = new MutableGatewayTimeProvider(DateTimeOffset.UtcNow);
        var keys = await sandbox.InitializeKeysAsync(clock);
        var authorization = new FileServiceReadOperatorAuthorization(keys, sandbox.Configuration.DeploymentId, clock);
        var request = new ServiceReadOperatorRequest(Guid.NewGuid(), Guid.NewGuid(), ServiceReadOperatorAction.Issue,
            Guid.NewGuid(), "local-test-operator", clock.GetUtcNow().AddMinutes(5), lifetimeDays: 30);
        using var proof = await authorization.IssueAsync(request, CancellationToken.None);
        Assert.IsTrue(await authorization.VerifyAsync(request, proof, CancellationToken.None));
        ServiceReadOperatorRequest[] changed =
        [
            new(Guid.NewGuid(), request.OperationId, request.Action, request.PrincipalId, request.OperatorId, request.ExpiresAt, lifetimeDays: 30),
            new(request.AuthorizationId, Guid.NewGuid(), request.Action, request.PrincipalId, request.OperatorId, request.ExpiresAt, lifetimeDays: 30),
            new(request.AuthorizationId, request.OperationId, ServiceReadOperatorAction.Disable, request.PrincipalId, request.OperatorId, request.ExpiresAt),
            new(request.AuthorizationId, request.OperationId, request.Action, Guid.NewGuid(), request.OperatorId, request.ExpiresAt, lifetimeDays: 30),
            new(request.AuthorizationId, request.OperationId, request.Action, request.PrincipalId, "different-operator", request.ExpiresAt, lifetimeDays: 30),
            new(request.AuthorizationId, request.OperationId, request.Action, request.PrincipalId, request.OperatorId, request.ExpiresAt.AddSeconds(1), lifetimeDays: 30),
            new(request.AuthorizationId, request.OperationId, request.Action, request.PrincipalId, request.OperatorId, request.ExpiresAt, lifetimeDays: 31),
        ];
        foreach (var mismatch in changed)
            Assert.IsFalse(await authorization.VerifyAsync(mismatch, proof, CancellationToken.None));
        var legacy = new FileOutOfBandAuthorization(keys, sandbox.Configuration.DeploymentId, clock);
        using var legacyProof = await legacy.IssueAsync(GatewayAuthorizationKeyTests.Request(clock.GetUtcNow()), CancellationToken.None);
        Assert.IsFalse(await authorization.VerifyAsync(request, legacyProof, CancellationToken.None));
        var otherDeployment = new FileServiceReadOperatorAuthorization(keys, Guid.NewGuid(), clock);
        Assert.IsFalse(await otherDeployment.VerifyAsync(request, proof, CancellationToken.None));
        var bytes = proof.Value.ToArray();
        bytes[^1] ^= 1;
        using var tampered = new OutOfBandAuthorizationProof(bytes);
        Assert.IsFalse(await authorization.VerifyAsync(request, tampered, CancellationToken.None));
        await keys.RotateAsync(CancellationToken.None);
        Assert.IsFalse(await authorization.VerifyAsync(request, proof, CancellationToken.None));
        using var replacement = await authorization.IssueAsync(request, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.IsFalse(await authorization.VerifyAsync(request, replacement, CancellationToken.None));
    }
}
