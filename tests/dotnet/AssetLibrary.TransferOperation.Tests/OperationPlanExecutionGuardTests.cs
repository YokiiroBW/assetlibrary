using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class OperationPlanExecutionGuardTests
{
    [TestMethod]
    public async Task WrongConfirmationCannotExecute()
    {
        var executor = new StubOperationExecutor();
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            new StubPreflightPort(),
            executor);
        var prepared = await service.PrepareAsync(OperationTestData.Plan(), CancellationToken.None);
        var wrong = new OperationConfirmationDigest(new string('0', 64));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.ExecuteAsync(
                prepared.Request.PlanId,
                wrong,
                OperationTestData.Right(),
                CancellationToken.None));
        Assert.AreEqual(0, executor.Calls);
    }

    [TestMethod]
    public async Task StaleExecutionRightCannotInvokeExecutor()
    {
        var store = new MemoryOperationStore { RefuseAcquire = true };
        var executor = new StubOperationExecutor();
        var service = OperationTestData.Service(store, new StubPreflightPort(), executor);
        var prepared = await service.PrepareAsync(OperationTestData.Plan(), CancellationToken.None);

        var result = await service.ExecuteAsync(
            prepared.Request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(OperationPlanState.ManualReview, result.State);
        Assert.AreEqual(OperationItemFailure.StaleExecutionRight, result.Items.Single().Failure);
        Assert.AreEqual(0, executor.Calls);
    }

    [TestMethod]
    public async Task CompletedPlanStillRequiresItsExactConfirmation()
    {
        var executor = new StubOperationExecutor();
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            new StubPreflightPort(),
            executor);
        var prepared = await service.PrepareAsync(
            OperationTestData.Plan(),
            CancellationToken.None);
        _ = await service.ExecuteAsync(
            prepared.Request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.ExecuteAsync(
                prepared.Request.PlanId,
                new OperationConfirmationDigest(new string('0', 64)),
                OperationTestData.Right(),
                CancellationToken.None));
        Assert.AreEqual(1, executor.Calls);
    }
}
