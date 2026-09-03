using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Application;

public interface IOperationPlanStore
{
    ValueTask<OperationPrepareResult> SavePreparedAsync(
        PreparedOperationPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<OperationAcquireResult> TryAcquireAsync(
        OperationPlanId planId,
        OperationConfirmationDigest confirmation,
        OperationExecutionRight executionRight,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<OperationCompletionResult> CompleteAsync(
        OperationPlanId planId,
        OperationExecutionRight executionRight,
        OperationPlanResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IOperationPreflightPort
{
    ValueTask<IReadOnlyList<OperationItemPreflight>> InspectAsync(
        OperationPlanRequest request,
        CancellationToken cancellationToken);
}

public interface IOperationItemExecutorPort
{
    ValueTask<OperationPortReceipt> ExecuteAsync(
        OperationPlanRequest request,
        OperationItemRequest item,
        OperationItemPreflight preflight,
        OperationExecutionRight executionRight,
        CancellationToken cancellationToken);
}

public interface IOperationEventSink
{
    void Record(OperationAuditEvent auditEvent);
}
