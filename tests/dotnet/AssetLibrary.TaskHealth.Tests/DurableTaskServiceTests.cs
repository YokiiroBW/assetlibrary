using AssetLibrary.Modules.TaskHealth.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.TaskHealth.Tests;

[TestClass]
public sealed class DurableTaskEnqueueServiceTests
{
    [TestMethod]
    public async Task EnqueueNormalizesScheduleAndUsesOneClockObservation()
    {
        var store = new StubDurableTaskStore();
        var service = Service(store);
        var request = TestData.TaskRequest(
            new DateTimeOffset(2026, 9, 3, 9, 30, 0, TimeSpan.FromHours(8)));

        var result = await service.EnqueueAsync(request, CancellationToken.None);

        Assert.AreEqual(DurableTaskEnqueueStatus.Created, result.Status);
        Assert.AreEqual(TimeSpan.Zero, store.LastEnqueueRequest?.NotBefore?.Offset);
        Assert.AreEqual(
            new DateTimeOffset(2026, 9, 3, 1, 30, 0, TimeSpan.Zero),
            store.LastEnqueueRequest?.NotBefore);
        Assert.AreEqual(TestData.Now, store.LastNow);
    }

    [TestMethod]
    public async Task CreatedResultMustReturnTheRequestedTaskIdWithoutLeakingTheIdempotencyKey()
    {
        var store = new StubDurableTaskStore
        {
            EnqueueResult = new(DurableTaskId.New(), DurableTaskEnqueueStatus.Created),
        };
        var service = Service(store);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.EnqueueAsync(TestData.TaskRequest(), CancellationToken.None).AsTask());

        Assert.DoesNotContain("test-task-key", exception.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ClaimRejectsConfiguredBatchOverflowBeforeCallingTheStore()
    {
        var store = new StubDurableTaskStore();
        var service = Service(store, new TaskHealthExecutionLimits(TimeSpan.FromSeconds(1), 2));

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.ClaimAsync(
            new DurableTaskClaimRequest(new LeaseOwner("worker.01"), TimeSpan.FromSeconds(30), 3),
            CancellationToken.None).AsTask());

        Assert.AreEqual(0, store.CallCount);
    }

    private static DurableTaskService Service(
        StubDurableTaskStore store,
        TaskHealthExecutionLimits? limits = null) =>
        new(
            store,
            new FixedTimeProvider(TestData.Now),
            limits ?? TaskHealthExecutionLimits.Default,
            new TaskHealthLogger(NullLogger<TaskHealthLogger>.Instance));
}

[TestClass]
public sealed class DurableTaskLeaseServiceTests
{

    [TestMethod]
    public async Task ClaimRejectsDuplicateOrOverlongStoreLeases()
    {
        var store = new StubDurableTaskStore();
        var request = new DurableTaskClaimRequest(new LeaseOwner("worker.01"), TimeSpan.FromSeconds(30), 2);
        var duplicateId = DurableTaskId.New();
        store.ClaimResult =
        [
            Lease(duplicateId, request.Worker, TestData.Now.AddSeconds(30)),
            Lease(duplicateId, request.Worker, TestData.Now.AddSeconds(31)),
        ];
        var service = Service(store);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.ClaimAsync(request, CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task HeartbeatRejectsAnAcceptedLeaseOutsideTheRequestedWindow()
    {
        var store = new StubDurableTaskStore
        {
            HeartbeatResult = new(
                TaskLeaseMutationStatus.Accepted,
                false,
                TestData.Now.AddSeconds(31)),
        };
        var service = Service(store);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.HeartbeatAsync(
            new DurableTaskHeartbeatRequest(
                DurableTaskId.New(),
                TestData.TaskIdentity(),
                TimeSpan.FromSeconds(30)),
            CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task FinishRejectsFailureShapesBeforeCallingTheStore()
    {
        var store = new StubDurableTaskStore();
        var service = Service(store);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.FinishAsync(
            new DurableTaskFinishRequest(
                DurableTaskId.New(),
                TestData.TaskIdentity(),
                DurableTaskFinishKind.PermanentFailure),
            CancellationToken.None).AsTask());

        Assert.AreEqual(0, store.CallCount);
    }

    [TestMethod]
    public async Task ReclaimRejectsImpossibleStoreCount()
    {
        var store = new StubDurableTaskStore { ReclaimResult = 3 };
        var service = Service(store);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.ReclaimExpiredAsync(2, CancellationToken.None).AsTask());
    }

    private static DurableTaskService Service(
        StubDurableTaskStore store,
        TaskHealthExecutionLimits? limits = null) =>
        new(
            store,
            new FixedTimeProvider(TestData.Now),
            limits ?? TaskHealthExecutionLimits.Default,
            new TaskHealthLogger(NullLogger<TaskHealthLogger>.Instance));

    private static DurableTaskLease Lease(
        DurableTaskId taskId,
        LeaseOwner owner,
        DateTimeOffset leaseUntil) =>
        new(
            taskId,
            new TaskTypeName("scan.initial"),
            new JsonObjectPayload("{}"),
            TaskPriority.P2,
            1,
            3,
            new TaskLeaseIdentity(owner, LeaseToken.New(), 1),
            leaseUntil,
            false);
}

[TestClass]
public sealed class DurableTaskTimeoutTests
{
    [TestMethod]
    public async Task StoreCancellationAtTheBoundBecomesAnExplicitTimeout()
    {
        var store = new StubDurableTaskStore { BlockEnqueue = true };
        var service = Service(store, new TaskHealthExecutionLimits(TimeSpan.FromMilliseconds(30)));

        var exception = await Assert.ThrowsExactlyAsync<TaskHealthOperationTimeoutException>(() =>
            service.EnqueueAsync(TestData.TaskRequest(), CancellationToken.None).AsTask());

        Assert.AreEqual("task_enqueue", exception.Operation);
    }

    [TestMethod]
    public async Task CallerCancellationKeepsCancellationSemantics()
    {
        var store = new StubDurableTaskStore { BlockEnqueue = true };
        var service = Service(store, new TaskHealthExecutionLimits(TimeSpan.FromSeconds(5)));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.EnqueueAsync(TestData.TaskRequest(), cancellation.Token).AsTask());
    }

    private static DurableTaskService Service(
        StubDurableTaskStore store,
        TaskHealthExecutionLimits limits) =>
        new(
            store,
            new FixedTimeProvider(TestData.Now),
            limits,
            new TaskHealthLogger(NullLogger<TaskHealthLogger>.Instance));
}
