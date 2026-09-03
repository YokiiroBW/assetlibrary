using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class SandboxTrashRestoreTests
{
    [TestMethod]
    public async Task TrashNeverDeletesAndCanBeRestoredWithoutOverwrite()
    {
        using var scenario = new SandboxScenario();
        var source = scenario.Write("trash_source", "library/trash.bin", PayloadTestData.Payload);
        var trashRequest = SandboxScenario.Request(
            PhysicalOperationKind.Trash,
            source,
            null,
            PayloadTestData.Facts());
        var trashed = await scenario.RunAsync(trashRequest);
        var trashPath = scenario.Fixture.InternalTrashPath(
            trashRequest.PlanId,
            trashRequest.Items[0].ItemId);

        Assert.AreEqual(OperationPlanState.Completed, trashed.State);
        Assert.IsFalse(File.Exists(scenario.Fixture.Resolve(source)));
        Assert.IsTrue(File.Exists(trashPath));

        var trashToken = scenario.Fixture.Register(
            "trash_payload",
            scenario.Fixture.Boundary.ToRelative(trashPath));
        var restoreTarget = scenario.Empty("restore_target", "library/restored.bin");
        var restoreRequest = SandboxScenario.Request(
            PhysicalOperationKind.Restore,
            trashToken,
            restoreTarget,
            PayloadTestData.Facts());
        var restored = await scenario.RunAsync(restoreRequest);

        Assert.AreEqual(OperationPlanState.Completed, restored.State);
        Assert.IsFalse(File.Exists(trashPath));
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(restoreTarget)));
    }

    [TestMethod]
    public async Task RestoreCollisionPreservesTrashAndExistingTarget()
    {
        using var scenario = new SandboxScenario();
        var source = scenario.Write("restore_source", "trash/payload.bin", PayloadTestData.Payload);
        var target = scenario.Write(
            "restore_existing",
            "library/existing.bin",
            "existing"u8.ToArray());
        var request = SandboxScenario.Request(
            PhysicalOperationKind.Restore,
            source,
            target,
            PayloadTestData.Facts());
        var prepared = await scenario.Service.PrepareAsync(request, CancellationToken.None);

        Assert.AreEqual(OperationPlanState.Rejected, prepared.State);
        Assert.IsTrue(File.Exists(scenario.Fixture.Resolve(source)));
        CollectionAssert.AreEqual(
            "existing"u8.ToArray(),
            await File.ReadAllBytesAsync(scenario.Fixture.Resolve(target)));
    }
}
