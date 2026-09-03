using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.TaskHealth.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 3, 1, 0, 0, TimeSpan.Zero);

    public static TaskLeaseIdentity TaskIdentity(string owner = "worker.01", long generation = 1) =>
        new(new LeaseOwner(owner), LeaseToken.New(), generation);

    public static OutboxLeaseIdentity OutboxIdentity(string owner = "publisher.01", long generation = 1) =>
        new(new LeaseOwner(owner), LeaseToken.New(), generation);

    public static DurableTaskEnqueueRequest TaskRequest(DateTimeOffset? notBefore = null) =>
        new(
            DurableTaskId.New(),
            new TaskIdempotencyKey("test-task-key"),
            new TaskTypeName("scan.initial"),
            new JsonObjectPayload("{\"library\":\"sandbox\"}"),
            TaskPriority.P2,
            3,
            notBefore);

    public static OutboxEnqueueRequest OutboxRequest(DateTimeOffset? notBefore = null) =>
        new(
            OutboxEventId.New(),
            new ModuleName("AssetIdentity"),
            new EventTypeName("asset.indexed"),
            Guid.NewGuid(),
            1,
            new JsonObjectPayload("{\"asset\":\"sandbox\"}"),
            Now.ToOffset(TimeSpan.FromHours(8)),
            NotBefore: notBefore);
}
