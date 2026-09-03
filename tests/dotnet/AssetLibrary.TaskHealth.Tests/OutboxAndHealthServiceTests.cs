using AssetLibrary.Modules.TaskHealth.Application;
using AssetLibrary.Modules.TaskHealth.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetLibrary.TaskHealth.Tests;

[TestClass]
public sealed class OutboxEnqueueAndClaimServiceTests
{
    [TestMethod]
    public async Task OutboxEnqueueNormalizesTimestampsAndRequiresTheStableEventId()
    {
        var store = new StubOutboxStore();
        var service = OutboxService(store);
        var request = TestData.OutboxRequest(
            new DateTimeOffset(2026, 9, 3, 9, 30, 0, TimeSpan.FromHours(8)));

        await service.EnqueueAsync(request, CancellationToken.None);

        Assert.AreEqual(TimeSpan.Zero, store.LastEnqueueRequest?.OccurredAt.Offset);
        Assert.AreEqual(TimeSpan.Zero, store.LastEnqueueRequest?.NotBefore?.Offset);
        Assert.AreEqual(TestData.Now, store.LastNow);

        store.EnqueueResult = new(OutboxEventId.New(), OutboxEnqueueStatus.Existing);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.EnqueueAsync(request, CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task OutboxClaimRejectsLeaseBeyondTheRequestedWindow()
    {
        var store = new StubOutboxStore();
        var request = new OutboxClaimRequest(
            new LeaseOwner("publisher.01"),
            TimeSpan.FromSeconds(30),
            1);
        store.ClaimResult = [Lease(request.Publisher, TestData.Now.AddSeconds(31))];

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            OutboxService(store).ClaimAsync(request, CancellationToken.None).AsTask());
    }

    private static DurableOutboxService OutboxService(StubOutboxStore store) =>
        new(
            store,
            new FixedTimeProvider(TestData.Now),
            TaskHealthExecutionLimits.Default,
            new TaskHealthLogger(NullLogger<TaskHealthLogger>.Instance));

    private static OutboxEventLease Lease(LeaseOwner owner, DateTimeOffset leaseUntil) =>
        new(
            OutboxEventId.New(),
            new ModuleName("AssetIdentity"),
            new EventTypeName("asset.indexed"),
            Guid.NewGuid(),
            1,
            new JsonObjectPayload("{}"),
            TestData.Now,
            1,
            3,
            new OutboxLeaseIdentity(owner, LeaseToken.New(), 1),
            leaseUntil);
}

[TestClass]
public sealed class OutboxMutationServiceTests
{

    [TestMethod]
    public async Task PublishAndReleaseRejectWrongAcceptedStates()
    {
        var store = new StubOutboxStore
        {
            PublishedResult = new(TaskLeaseMutationStatus.Accepted, OutboxEventState.Pending),
            ReleasedResult = new(TaskLeaseMutationStatus.Accepted, OutboxEventState.Published),
        };
        var service = OutboxService(store);
        var eventId = OutboxEventId.New();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.MarkPublishedAsync(
            new OutboxPublishRequest(eventId, TestData.OutboxIdentity()),
            CancellationToken.None).AsTask());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ReleaseAsync(
            new OutboxReleaseRequest(
                eventId,
                TestData.OutboxIdentity(),
                new FailureCode("provider.timeout"),
                TimeSpan.Zero),
            CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task OutboxReleaseRejectsUnboundedRetryBeforeTheStore()
    {
        var store = new StubOutboxStore();

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => OutboxService(store).ReleaseAsync(
            new OutboxReleaseRequest(
                OutboxEventId.New(),
                TestData.OutboxIdentity(),
                new FailureCode("provider.timeout"),
                TimeSpan.FromDays(1).Add(TimeSpan.FromTicks(1))),
            CancellationToken.None).AsTask());

        Assert.AreEqual(0, store.CallCount);
    }

    private static DurableOutboxService OutboxService(StubOutboxStore store) =>
        new(
            store,
            new FixedTimeProvider(TestData.Now),
            TaskHealthExecutionLimits.Default,
            new TaskHealthLogger(NullLogger<TaskHealthLogger>.Instance));
}

[TestClass]
public sealed class HealthStatusServiceTests
{

    [TestMethod]
    public async Task HealthWriteNormalizesObservationAndPreservesStoreStaleResult()
    {
        var store = new StubHealthStatusStore
        {
            Result = new(HealthStatusWriteStatus.Stale),
        };
        var service = HealthService(store);
        var update = new HealthStatusUpdate(
            new HealthScope(HealthScopeKind.Library, Guid.NewGuid()),
            new HealthComponentName("storage"),
            HealthState.Offline,
            new HealthReasonCode("storage.offline"),
            new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.FromHours(8)));

        var result = await service.WriteAsync(update, CancellationToken.None);

        Assert.AreEqual(HealthStatusWriteStatus.Stale, result.Status);
        Assert.AreEqual(TimeSpan.Zero, store.LastUpdate?.ObservedAt.Offset);
        Assert.AreEqual(TestData.Now, store.LastWrittenAt);
    }

    [TestMethod]
    public async Task InvalidNormalHealthReasonFailsBeforeTheStore()
    {
        var store = new StubHealthStatusStore();
        var update = new HealthStatusUpdate(
            HealthScope.System,
            new HealthComponentName("database"),
            HealthState.Normal,
            new HealthReasonCode("unexpected.reason"),
            TestData.Now);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            HealthService(store).WriteAsync(update, CancellationToken.None).AsTask());

        Assert.AreEqual(0, store.CallCount);
    }

    [TestMethod]
    public async Task DefaultHealthScopeFailsBeforeTheStore()
    {
        var store = new StubHealthStatusStore();
        var update = new HealthStatusUpdate(
            default,
            new HealthComponentName("database"),
            HealthState.Normal,
            null,
            TestData.Now);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            HealthService(store).WriteAsync(update, CancellationToken.None).AsTask());

        Assert.AreEqual(0, store.CallCount);
    }

    private static HealthStatusService HealthService(StubHealthStatusStore store) =>
        new(
            store,
            new FixedTimeProvider(TestData.Now),
            TaskHealthExecutionLimits.Default,
            new TaskHealthLogger(NullLogger<TaskHealthLogger>.Instance));

}
