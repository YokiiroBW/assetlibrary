using AssetLibrary.Modules.OperationTrash.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Application;

public sealed class OperationPlanService
{
    private readonly OperationPlanExecution execution;
    private readonly OperationPlanPreparation preparation;

    public OperationPlanService(
        IOperationPlanStore planStore,
        IOperationPreflightPort preflightPort,
        IOperationItemExecutorPort executorPort,
        IOperationEventSink eventSink,
        TimeProvider timeProvider,
        OperationExecutionLimits limits)
    {
        preparation = new(planStore, preflightPort, eventSink, timeProvider, limits);
        execution = new(planStore, executorPort, eventSink, timeProvider, limits);
    }

    public ValueTask<PreparedOperationPlan> PrepareAsync(
        OperationPlanRequest request,
        CancellationToken cancellationToken) =>
        preparation.PrepareAsync(request, cancellationToken);

    public ValueTask<OperationPlanResult> ExecuteAsync(
        OperationPlanId planId,
        OperationConfirmationDigest confirmation,
        OperationExecutionRight executionRight,
        CancellationToken cancellationToken) =>
        execution.ExecuteAsync(planId, confirmation, executionRight, cancellationToken);
}
