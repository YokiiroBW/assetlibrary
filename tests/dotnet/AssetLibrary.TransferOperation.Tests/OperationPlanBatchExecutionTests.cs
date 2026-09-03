using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class OperationPlanPartialExecutionTests
{
    [TestMethod]
    public async Task BatchMayCompletePartiallyWithoutHidingFailure()
    {
        var first = OperationTestData.Item("source_one", "target_one");
        var second = OperationTestData.Item("source_two", "target_two");
        var executor = new StubOperationExecutor
        {
            Handler = (request, item, _) =>
                ValueTask.FromResult(
                    item.ItemId == first.ItemId
                        ? new OperationPortReceipt(
                            request.PlanId,
                            item.ItemId,
                            OperationPortOutcome.Completed,
                            item.ExpectedSource)
                        : new OperationPortReceipt(
                            request.PlanId,
                            item.ItemId,
                            OperationPortOutcome.TargetExists,
                            null)),
        };
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            new StubPreflightPort(),
            executor);
        var prepared = await service.PrepareAsync(
            OperationTestData.Plan(items: [first, second]),
            CancellationToken.None);

        var result = await service.ExecuteAsync(
            prepared.Request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(OperationPlanState.PartiallyCompleted, result.State);
        Assert.IsTrue(result.Items[0].Succeeded);
        Assert.AreEqual(OperationItemFailure.TargetExists, result.Items[1].Failure);
    }
}

[TestClass]
public sealed class OperationPlanCancelledExecutionTests
{
    [TestMethod]
    public async Task CallerCancellationMarksUnattemptedItems()
    {
        var first = OperationTestData.Item("source_one", "target_one");
        var second = OperationTestData.Item("source_two", "target_two");
        using var cancelled = new CancellationTokenSource();
        var executor = StubOperationExecutor.Cancelling(cancelled);
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            new StubPreflightPort(),
            executor);
        var prepared = await service.PrepareAsync(
            OperationTestData.Plan(items: [first, second]),
            CancellationToken.None);

        var result = await service.ExecuteAsync(
            prepared.Request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            cancelled.Token);

        Assert.AreEqual(OperationPlanState.Cancelled, result.State);
        Assert.IsTrue(result.Items.All(item => item.Failure == OperationItemFailure.Cancelled));
    }
}
