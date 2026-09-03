using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class OperationPlanRejectedPreflightTests
{
    [TestMethod]
    public async Task RejectedPreflightCannotExecute()
    {
        var preflight = StubPreflightPort.WithDecision(OperationPreflightDecision.Protected);
        var executor = new StubOperationExecutor();
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            preflight,
            executor);
        var prepared = await service.PrepareAsync(OperationTestData.Plan(), CancellationToken.None);

        Assert.AreEqual(OperationPlanState.Rejected, prepared.State);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.ExecuteAsync(
                prepared.Request.PlanId,
                prepared.Preflight.Confirmation,
                OperationTestData.Right(),
                CancellationToken.None));
        Assert.AreEqual(0, executor.Calls);
    }
}

[TestClass]
public sealed class OperationPlanMalformedPreflightTests
{
    [TestMethod]
    public async Task UntrustedPreflightCannotOmitAnItem()
    {
        var preflight = new StubPreflightPort
        {
            Handler = static (_, _) =>
                ValueTask.FromResult<IReadOnlyList<OperationItemPreflight>>([]),
        };
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            preflight,
            new StubOperationExecutor());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.PrepareAsync(
                OperationTestData.Plan(),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadyPreflightCannotUnderstateRequiredCapacity()
    {
        var preflight = new StubPreflightPort
        {
            Handler = static (request, _) =>
                ValueTask.FromResult<IReadOnlyList<OperationItemPreflight>>(
                    request.Items.Select(item =>
                        new OperationItemPreflight(
                            item.ItemId,
                            OperationPreflightDecision.Ready,
                            item.ExpectedSource,
                            0,
                            0)).ToArray()),
        };
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            preflight,
            new StubOperationExecutor());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.PrepareAsync(
                OperationTestData.Plan(),
                CancellationToken.None));
    }
}
