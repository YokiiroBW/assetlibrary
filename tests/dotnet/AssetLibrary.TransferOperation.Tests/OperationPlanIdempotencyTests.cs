using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class OperationPlanIdempotencyTests
{
    [TestMethod]
    public async Task SameIntentReturnsOriginalPreparedPlan()
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        var store = new MemoryOperationStore();
        var preflight = new StubPreflightPort();
        var service = OperationTestData.Service(store, preflight, new StubOperationExecutor());
        var original = OperationTestData.Plan(deadline: deadline);
        var first = await service.PrepareAsync(original, CancellationToken.None);
        var replay = original with
        {
            PlanId = OperationPlanId.New(),
            Items = [OperationTestData.Item()],
        };

        var second = await service.PrepareAsync(replay, CancellationToken.None);

        Assert.AreEqual(first.Request.PlanId, second.Request.PlanId);
        Assert.AreEqual(first.Preflight.Confirmation, second.Preflight.Confirmation);
        Assert.AreEqual(2, preflight.Calls);
    }

    [TestMethod]
    public async Task ReusedKeyWithDifferentIntentIsRejected()
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            new StubPreflightPort(),
            new StubOperationExecutor());
        await service.PrepareAsync(
            OperationTestData.Plan(deadline: deadline),
            CancellationToken.None);
        var conflicting = OperationTestData.Plan(
            items: [OperationTestData.Item(target: "different_target")],
            deadline: deadline);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.PrepareAsync(
                conflicting,
                CancellationToken.None));
    }
}
