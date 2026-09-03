using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class SandboxOperationChangedFactsTests
{
    [TestMethod]
    public async Task SourceMutationAfterPreflightPreservesSourceAndTarget()
    {
        using var scenario = new SandboxScenario();
        var operation = scenario.TargetCase(
            PhysicalOperationKind.Move,
            "changing_source",
            "incoming/changing.bin",
            "changing_target",
            "library/changing.bin");
        var prepared = await scenario.Service.PrepareAsync(
            operation.Request,
            CancellationToken.None);
        await File.WriteAllBytesAsync(
            scenario.Fixture.Resolve(operation.Source),
            "changed-after-preflight"u8.ToArray());

        var result = await scenario.Service.ExecuteAsync(
            operation.Request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(OperationPlanState.Failed, result.State);
        Assert.AreEqual(OperationItemFailure.SourceChanged, result.Items[0].Failure);
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(operation.Source)));
        Assert.IsFalse(File.Exists(scenario.Fixture.Resolve(operation.Target)));
    }
}

[TestClass]
public sealed class SandboxOperationCollisionTests
{
    [TestMethod]
    public async Task TargetAppearingAfterPreflightIsNeverOverwritten()
    {
        using var scenario = new SandboxScenario();
        var operation = scenario.TargetCase(
            PhysicalOperationKind.Copy,
            "race_source",
            "incoming/race.bin",
            "race_target",
            "library/race.bin");
        var prepared = await scenario.Service.PrepareAsync(
            operation.Request,
            CancellationToken.None);
        var existing = "appeared-concurrently"u8.ToArray();
        var targetPath = scenario.Fixture.Resolve(operation.Target);
        SandboxFilePrimitives.EnsureParent(scenario.Fixture.Boundary, targetPath);
        await File.WriteAllBytesAsync(targetPath, existing);

        var result = await scenario.Service.ExecuteAsync(
            operation.Request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(OperationItemFailure.TargetExists, result.Items[0].Failure);
        CollectionAssert.AreEqual(
            existing,
            await File.ReadAllBytesAsync(targetPath));
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(operation.Source)));
    }
}

[TestClass]
public sealed class SandboxOperationPolicyChangeTests
{
    [TestMethod]
    public async Task ProtectionAddedAfterPreflightStillFailsClosed()
    {
        using var scenario = new SandboxScenario();
        var operation = scenario.TargetCase(
            PhysicalOperationKind.Move,
            "protected_source",
            "library/protected.bin",
            "protected_target",
            "library/moved.bin");
        var prepared = await scenario.Service.PrepareAsync(
            operation.Request,
            CancellationToken.None);
        scenario.AccessPolicy.Protect(operation.Source);

        var result = await scenario.Service.ExecuteAsync(
            operation.Request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(OperationItemFailure.Protected, result.Items[0].Failure);
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(operation.Source)));
        Assert.IsFalse(File.Exists(scenario.Fixture.Resolve(operation.Target)));
    }

    [TestMethod]
    public async Task CapacityLossAfterPreflightStillFailsClosed()
    {
        using var scenario = new SandboxScenario();
        var operation = scenario.TargetCase(
            PhysicalOperationKind.Copy,
            "space_source",
            "incoming/space.bin",
            "space_target",
            "library/space.bin");
        var prepared = await scenario.Service.PrepareAsync(
            operation.Request,
            CancellationToken.None);
        scenario.AccessPolicy.AvailableBytesOverride = 0;

        var result = await scenario.Service.ExecuteAsync(
            operation.Request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(OperationItemFailure.InsufficientSpace, result.Items[0].Failure);
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(operation.Source)));
        Assert.IsFalse(File.Exists(scenario.Fixture.Resolve(operation.Target)));
    }
}
