using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class SandboxMoveRecoveryTests
{
    [TestMethod]
    [DataRow((int)SandboxFaultPoint.AfterStageWritten)]
    [DataRow((int)SandboxFaultPoint.AfterTargetCommitted)]
    [DataRow((int)SandboxFaultPoint.BeforeSourceTrash)]
    [DataRow((int)SandboxFaultPoint.AfterSourceTrash)]
    public async Task MoveRecoveryUsesPhysicalFactsAndIsIdempotent(int faultValue)
    {
        using var scenario = new SandboxScenario();
        var operation = scenario.TargetCase(
            PhysicalOperationKind.Move,
            "recover_source",
            "incoming/recover.bin",
            "recover_target",
            "library/recover.bin");
        var prepared = await scenario.Service.PrepareAsync(
            operation.Request,
            CancellationToken.None);
        scenario.Faults.Point = (SandboxFaultPoint)faultValue;

        await Assert.ThrowsExactlyAsync<IOException>(
            async () => await scenario.Service.ExecuteAsync(
                operation.Request.PlanId,
                prepared.Preflight.Confirmation,
                OperationTestData.Right(),
                CancellationToken.None));

        var itemId = operation.Request.Items[0].ItemId;
        var first = await scenario.RecoverAsync(
            operation.Request.PlanId,
            itemId,
            CancellationToken.None);
        var second = await scenario.RecoverAsync(
            operation.Request.PlanId,
            itemId,
            CancellationToken.None);

        Assert.AreEqual(SandboxRecoveryOutcome.Complete, first.Outcome);
        Assert.AreEqual(SandboxRecoveryOutcome.Complete, second.Outcome);
        SandboxOperationAssertions.MovedToTrash(scenario, operation);
    }
}

[TestClass]
public sealed class SandboxRecoveryCollisionTests
{
    [TestMethod]
    public async Task RecoveryCollisionPreservesSourceStageAndUnexpectedTarget()
    {
        using var scenario = new SandboxScenario();
        var operation = scenario.TargetCase(
            PhysicalOperationKind.Move,
            "conflict_source",
            "incoming/conflict.bin",
            "conflict_target",
            "library/conflict.bin");
        var prepared = await scenario.Service.PrepareAsync(
            operation.Request,
            CancellationToken.None);
        scenario.Faults.Point = SandboxFaultPoint.AfterStageWritten;
        await Assert.ThrowsExactlyAsync<IOException>(
            async () => await scenario.Service.ExecuteAsync(
                operation.Request.PlanId,
                prepared.Preflight.Confirmation,
                OperationTestData.Right(),
                CancellationToken.None));
        var unexpected = "unexpected-target"u8.ToArray();
        var targetPath = scenario.Fixture.Resolve(operation.Target);
        SandboxFilePrimitives.EnsureParent(scenario.Fixture.Boundary, targetPath);
        await File.WriteAllBytesAsync(targetPath, unexpected);

        var itemId = operation.Request.Items[0].ItemId;
        var recovered = await scenario.RecoverAsync(
            operation.Request.PlanId,
            itemId,
            CancellationToken.None);
        var stage = scenario.Fixture.Boundary.ResolveInternal(
            "stage",
            operation.Request.PlanId.Value.ToString("N"),
            itemId.Value.ToString("N"),
            "payload.partial");

        Assert.AreEqual(SandboxRecoveryOutcome.Conflict, recovered.Outcome);
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(operation.Source)));
        Assert.IsTrue(File.Exists(stage));
        CollectionAssert.AreEqual(
            unexpected,
            await File.ReadAllBytesAsync(targetPath));
    }

    [TestMethod]
    public async Task CopyRecoveryReportsConflictWhenCommittedTargetLostItsSource()
    {
        using var scenario = new SandboxScenario();
        var operation = scenario.TargetCase(
            PhysicalOperationKind.Copy,
            "copy_recovery_source",
            "incoming/copy-recovery.bin",
            "copy_recovery_target",
            "library/copy-recovery.bin");
        var prepared = await scenario.Service.PrepareAsync(
            operation.Request,
            CancellationToken.None);
        scenario.Faults.Point = SandboxFaultPoint.AfterTargetCommitted;
        await Assert.ThrowsExactlyAsync<IOException>(
            async () => await scenario.Service.ExecuteAsync(
                operation.Request.PlanId,
                prepared.Preflight.Confirmation,
                OperationTestData.Right(),
                CancellationToken.None));
        SandboxFilePrimitives.DeleteEvidenceFile(
            scenario.Fixture.Boundary,
            scenario.Fixture.Resolve(operation.Source));

        var recovered = await scenario.RecoverAsync(
            operation.Request.PlanId,
            operation.Request.Items[0].ItemId,
            CancellationToken.None);

        Assert.AreEqual(SandboxRecoveryOutcome.Conflict, recovered.Outcome);
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(operation.Target)));
    }
}
