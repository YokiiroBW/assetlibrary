using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

[TestClass]
public sealed class OperationPlanTimingTests
{
    [TestMethod]
    public async Task RequestDeadlineMarksEveryRemainderDistinctly()
    {
        var now = DateTimeOffset.UtcNow;
        var time = new MutableTimeProvider(now);
        var executor = new StubOperationExecutor
        {
            Handler = (_, _, _) =>
            {
                time.Current = now.AddMinutes(2);
                throw new OperationCanceledException();
            },
        };
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            new StubPreflightPort(),
            executor,
            timeProvider: time);
        var request = OperationTestData.Plan(
            items: [
                OperationTestData.Item("source_one", "target_one"),
                OperationTestData.Item("source_two", "target_two"),
            ],
            deadline: now.AddMinutes(1));
        var prepared = await service.PrepareAsync(request, CancellationToken.None);

        var result = await service.ExecuteAsync(
            request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(now.AddMinutes(3)),
            CancellationToken.None);

        Assert.AreEqual(OperationPlanState.Cancelled, result.State);
        Assert.IsTrue(
            result.Items.All(item => item.Failure == OperationItemFailure.DeadlineExceeded));
    }

    [TestMethod]
    public async Task ExpiredPreflightCannotInvokeExecutor()
    {
        var now = DateTimeOffset.UtcNow;
        var time = new MutableTimeProvider(now);
        var executor = new StubOperationExecutor();
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            new StubPreflightPort(),
            executor,
            timeProvider: time);
        var request = OperationTestData.Plan(deadline: now.AddMinutes(10));
        var prepared = await service.PrepareAsync(request, CancellationToken.None);
        time.Current = now.AddMinutes(3);

        var result = await service.ExecuteAsync(
            request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(now.AddMinutes(10)),
            CancellationToken.None);

        Assert.AreEqual(OperationPlanState.ManualReview, result.State);
        Assert.AreEqual(OperationItemFailure.StaleExecutionRight, result.Items[0].Failure);
        Assert.AreEqual(0, executor.Calls);
    }

}

[TestClass]
public sealed class OperationCompletionValidationTests
{
    [TestMethod]
    public async Task StructurallyEqualRehydratedCompletionIsAccepted()
    {
        var store = new MemoryOperationStore
        {
            CompletionOverride = result =>
                new OperationCompletionResult(
                    OperationCompletionStatus.Accepted,
                    new OperationPlanResult(
                        result.PlanId,
                        result.State,
                        result.Items.ToArray())),
        };
        var service = OperationTestData.Service(
            store,
            new StubPreflightPort(),
            new StubOperationExecutor());
        var prepared = await service.PrepareAsync(
            OperationTestData.Plan(),
            CancellationToken.None);

        var result = await service.ExecuteAsync(
            prepared.Request.PlanId,
            prepared.Preflight.Confirmation,
            OperationTestData.Right(),
            CancellationToken.None);

        Assert.AreEqual(OperationPlanState.Completed, result.State);
    }
}

[TestClass]
public sealed class OperationPreflightTimingTests
{
    [TestMethod]
    public async Task TimeSpentInspectingCannotExtendRequestDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        var time = new MutableTimeProvider(now);
        var preflight = new StubPreflightPort
        {
            Handler = (request, cancellationToken) =>
            {
                time.Current = now.AddMinutes(2);
                return StubPreflightPort.WithDecision(OperationPreflightDecision.Ready)
                    .InspectAsync(request, cancellationToken);
            },
        };
        var service = OperationTestData.Service(
            new MemoryOperationStore(),
            preflight,
            new StubOperationExecutor(),
            timeProvider: time);

        await Assert.ThrowsExactlyAsync<TimeoutException>(
            async () => await service.PrepareAsync(
                OperationTestData.Plan(deadline: now.AddMinutes(1)),
                CancellationToken.None));
    }
}
