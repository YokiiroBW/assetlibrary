namespace AssetLibrary.TransferOperation.Tests;

internal static class SandboxOperationAssertions
{
    public static void MovedToTrash(
        SandboxScenario scenario,
        SandboxTargetOperationCase operation)
    {
        Assert.IsFalse(File.Exists(scenario.Fixture.Resolve(operation.Source)));
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(operation.Target)));
        Assert.IsTrue(File.Exists(
            scenario.Fixture.InternalTrashPath(
                operation.Request.PlanId,
                operation.Request.Items[0].ItemId)));
    }
}
