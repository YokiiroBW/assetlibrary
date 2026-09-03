using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class SandboxTransferIntegrationTests
{
    [TestMethod]
    public async Task MultiChunkCopyReopensAndStrongHashesTarget()
    {
        using var scenario = new SandboxScenario();
        var payload = DeterministicPayload(2 * 1024 * 1024 + 17);
        var facts = PayloadTestData.Facts(payload);
        var source = scenario.Write("source_large", "incoming/payload.bin", payload);
        var target = scenario.Empty("target_large", "library/payload.bin");
        var port = new SandboxTransferPayloadPort(
            scenario.Fixture,
            new SandboxStreamCopy(32 * 1024),
            new SandboxFileHash(32 * 1024));
        var request = TransferTestData.Request(facts: facts) with
        {
            Source = source,
            Target = target,
        };

        var result = await SandboxTransferComposition.CreateService(port).ExecuteAsync(
            request,
            TransferTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(TransferSessionState.Completed, result.State);
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(source)));
        Assert.AreEqual(facts, await new SandboxFileHash().ComputeAsync(
            scenario.Fixture.Resolve(target),
            CancellationToken.None));
        Assert.IsGreaterThan(1, port.LastChunkCount);
        Assert.IsLessThanOrEqualTo(32 * 1024, port.LastMaximumChunk);
    }

    [TestMethod]
    public async Task CancellationLeavesNoFormalTarget()
    {
        using var scenario = new SandboxScenario();
        var payload = DeterministicPayload(512 * 1024);
        var facts = PayloadTestData.Facts(payload);
        var source = scenario.Write("cancel_source", "incoming/cancel.bin", payload);
        var target = scenario.Empty("cancel_target", "library/cancel.bin");
        using var cancellation = new CancellationTokenSource();
        var copy = new SandboxStreamCopy(4096)
        {
            ChunkObserver = _ => cancellation.Cancel(),
        };
        var port = new SandboxTransferPayloadPort(
            scenario.Fixture,
            copy,
            new SandboxFileHash());
        var request = TransferTestData.Request(
            idempotencyKey: "cancel-transfer",
            facts: facts) with
        {
            Source = source,
            Target = target,
        };

        var result = await SandboxTransferComposition.CreateService(port).ExecuteAsync(
            request,
            TransferTestData.Right(),
            cancellation.Token);

        Assert.AreEqual(TransferSessionState.Cancelled, result.State);
        Assert.AreEqual(TransferFailureKind.Cancelled, result.Failure);
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(source)));
        Assert.IsFalse(File.Exists(scenario.Fixture.Resolve(target)));
        var stage = scenario.Fixture.Boundary.ResolveInternal(
            "stage",
            request.SessionId.Value.ToString("N"),
            "payload.partial");
        Assert.IsFalse(File.Exists(stage));
    }

    [TestMethod]
    public async Task ExistingTargetAndInsufficientSpaceFailClosed()
    {
        using var scenario = new SandboxScenario();
        var facts = PayloadTestData.Facts();
        var source = scenario.Write(
            "failure_source",
            "incoming/source.bin",
            PayloadTestData.Payload);
        var target = scenario.Write(
            "existing_target",
            "library/target.bin",
            "existing"u8.ToArray());
        var collisionPort = new SandboxTransferPayloadPort(
            scenario.Fixture,
            new SandboxStreamCopy(),
            new SandboxFileHash());
        var collisionRequest = TransferTestData.Request(
            idempotencyKey: "target-collision",
            facts: facts) with
        {
            Source = source,
            Target = target,
        };

        var collision = await SandboxTransferComposition.CreateService(collisionPort).ExecuteAsync(
            collisionRequest,
            TransferTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(TransferFailureKind.TargetExists, collision.Failure);
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(source)));
        CollectionAssert.AreEqual(
            "existing"u8.ToArray(),
            await File.ReadAllBytesAsync(scenario.Fixture.Resolve(target)));

        var emptyTarget = scenario.Empty("space_target", "library/space.bin");
        var spacePort = new SandboxTransferPayloadPort(
            scenario.Fixture,
            new SandboxStreamCopy(),
            new SandboxFileHash())
        {
            AvailableBytesOverride = 0,
        };
        var spaceRequest = collisionRequest with
        {
            SessionId = TransferSessionId.New(),
            IdempotencyKey = new TransferIdempotencyKey("space-failure"),
            Target = emptyTarget,
        };
        var noSpace = await SandboxTransferComposition.CreateService(spacePort).ExecuteAsync(
            spaceRequest,
            TransferTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(TransferFailureKind.InsufficientSpace, noSpace.Failure);
        Assert.IsFalse(File.Exists(scenario.Fixture.Resolve(emptyTarget)));
    }

    private static byte[] DeterministicPayload(int length)
    {
        var payload = new byte[length];
        new Random(7007).NextBytes(payload);
        return payload;
    }
}
