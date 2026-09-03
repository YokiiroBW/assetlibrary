using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class OperationPlanPreparationSuccessTests
{
    [TestMethod]
    public async Task PrepareIsReadOnlyAndBindsExactPreflightToConfirmation()
    {
        var store = new MemoryOperationStore();
        var preflight = new StubPreflightPort();
        var executor = new StubOperationExecutor();
        var service = OperationTestData.Service(store, preflight, executor);
        var request = OperationTestData.Plan();

        var prepared = await service.PrepareAsync(request, CancellationToken.None);

        Assert.AreEqual(OperationPlanState.AwaitingConfirmation, prepared.State);
        Assert.AreEqual(1, preflight.Calls);
        Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual(request.PlanId, prepared.Preflight.PlanId);
    }

    [TestMethod]
    public async Task ChangedPhysicalFactsProduceDifferentConfirmation()
    {
        var firstService = OperationTestData.Service(
            new MemoryOperationStore(),
            new StubPreflightPort(),
            new StubOperationExecutor());
        var firstRequest = OperationTestData.Plan(idempotencyKey: "first");
        var first = await firstService.PrepareAsync(firstRequest, CancellationToken.None);
        var changedFacts = PayloadTestData.Facts("changed"u8.ToArray());
        var changedRequest = OperationTestData.Plan(
            items: [OperationTestData.Item(facts: changedFacts)],
            idempotencyKey: "second");
        var secondService = OperationTestData.Service(
            new MemoryOperationStore(),
            new StubPreflightPort(),
            new StubOperationExecutor());

        var second = await secondService.PrepareAsync(changedRequest, CancellationToken.None);

        Assert.AreNotEqual(first.Preflight.Confirmation, second.Preflight.Confirmation);
    }
}
