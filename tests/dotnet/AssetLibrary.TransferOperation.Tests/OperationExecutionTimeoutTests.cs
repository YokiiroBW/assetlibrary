using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class OperationExecutionTimeoutTests
{
    [TestMethod]
    public async Task ServiceTimeoutIsDistinctFromCallerCancellation()
    {
        var executor = new StubOperationExecutor
        {
            Handler = static async (request, item, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new OperationPortReceipt(
                    request.PlanId,
                    item.ItemId,
                    OperationPortOutcome.Completed,
                    item.ExpectedSource);
            },
        };
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            new StubPreflightPort(),
            executor,
            timeout: TimeSpan.FromMilliseconds(25));
        var prepared = await service.PrepareAsync(
            OperationTestData.Plan(),
            CancellationToken.None);

        var result = await service.ExecuteAsync(
            prepared.Request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(OperationPlanState.Cancelled, result.State);
        Assert.IsTrue(
            result.Items.All(item => item.Failure == OperationItemFailure.DeadlineExceeded));
    }
}
