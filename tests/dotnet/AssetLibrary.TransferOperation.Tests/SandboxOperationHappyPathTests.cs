using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class SandboxOperationHappyPathTests
{
    [TestMethod]
    public async Task CopyPreservesSourceAndVerifiesTarget()
    {
        using var scenario = new SandboxScenario();
        var operation = scenario.TargetCase(
            PhysicalOperationKind.Copy,
            "copy_source",
            "incoming/copy.bin",
            "copy_target",
            "library/copy.bin");

        var result = await scenario.RunAsync(operation.Request);

        Assert.AreEqual(OperationPlanState.Completed, result.State);
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(operation.Source)));
        Assert.AreEqual(
            operation.Request.Items[0].ExpectedSource,
            await scenario.HashAsync(operation.Target));
    }

    [TestMethod]
    public async Task MoveCommitsTargetBeforeSourceEntersTrash()
    {
        using var scenario = new SandboxScenario();
        var operation = scenario.TargetCase(
            PhysicalOperationKind.Move,
            "move_source",
            "incoming/move.bin",
            "move_target",
            "library/move.bin");

        var result = await scenario.RunAsync(operation.Request);

        Assert.AreEqual(OperationPlanState.Completed, result.State);
        SandboxOperationAssertions.MovedToTrash(scenario, operation);
    }

    [TestMethod]
    public async Task RenameUsesNoReplaceAndLeavesNoTrashCopy()
    {
        using var scenario = new SandboxScenario();
        var operation = scenario.TargetCase(
            PhysicalOperationKind.Rename,
            "rename_source",
            "library/old.bin",
            "rename_target",
            "library/new.bin");

        var result = await scenario.RunAsync(operation.Request);

        Assert.AreEqual(OperationPlanState.Completed, result.State);
        Assert.IsFalse(File.Exists(scenario.Fixture.Resolve(operation.Source)));
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(operation.Target)));
        Assert.IsFalse(File.Exists(
            scenario.Fixture.InternalTrashPath(
                operation.Request.PlanId,
                operation.Request.Items[0].ItemId)));
    }
}
