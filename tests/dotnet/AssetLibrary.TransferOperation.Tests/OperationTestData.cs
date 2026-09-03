using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal static class OperationTestData
{
    public static OperationItemRequest Item(
        string source = "source_token",
        string? target = "target_token",
        PayloadFacts? facts = null) =>
        new(
            OperationItemId.New(),
            new TransferLocationToken(source),
            target is null ? null : new TransferLocationToken(target),
            facts ?? PayloadTestData.Facts());

    public static OperationPlanRequest Plan(
        PhysicalOperationKind operation = PhysicalOperationKind.Copy,
        IReadOnlyList<OperationItemRequest>? items = null,
        string idempotencyKey = "operation-test",
        DateTimeOffset? deadline = null) =>
        new(
            OperationPlanId.New(),
            new OperationIdempotencyKey(idempotencyKey),
            operation,
            items ?? [Item()],
            deadline ?? DateTimeOffset.UtcNow.AddMinutes(1));

    public static OperationExecutionRight Right(DateTimeOffset? expiresAt = null) =>
        new(
            new OperationWorker("worker_1"),
            OperationLeaseToken.New(),
            1,
            expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(1));

    public static OperationPlanService Service(
        MemoryOperationStore store,
        IOperationPreflightPort preflight,
        IOperationItemExecutorPort executor,
        CaptureOperationEvents? events = null,
        TimeProvider? timeProvider = null,
        TimeSpan? timeout = null) =>
        new(
            store,
            preflight,
            executor,
            events ?? new(),
            timeProvider ?? TimeProvider.System,
            new OperationExecutionLimits(
                timeout ?? TimeSpan.FromSeconds(2),
                TimeSpan.FromMinutes(2)));
}
