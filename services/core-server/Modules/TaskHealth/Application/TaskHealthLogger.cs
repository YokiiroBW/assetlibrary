using AssetLibrary.Modules.TaskHealth.Contracts;
using Microsoft.Extensions.Logging;

namespace AssetLibrary.Modules.TaskHealth.Application;

public sealed class TaskHealthLogger(ILogger<TaskHealthLogger> logger)
{
    private static readonly Action<ILogger, Guid, bool, Exception?> TaskEnqueued =
        LoggerMessage.Define<Guid, bool>(
            LogLevel.Information,
            new EventId(4200, nameof(TaskEnqueued)),
            "Durable task {TaskId} enqueue completed; created={Created}");

    private static readonly Action<ILogger, string, int, Exception?> TasksClaimed =
        LoggerMessage.Define<string, int>(
            LogLevel.Debug,
            new EventId(4201, nameof(TasksClaimed)),
            "Durable task worker {LeaseOwner} claimed {ClaimCount} tasks");

    private static readonly Action<ILogger, Guid, string, Exception?> TaskFinished =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(4202, nameof(TaskFinished)),
            "Durable task {TaskId} finish result is {ResultState}");

    private static readonly Action<ILogger, Guid, string, Exception?> CancellationRequested =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(4203, nameof(CancellationRequested)),
            "Durable task {TaskId} cancellation result is {CancellationStatus}");

    private static readonly Action<ILogger, Guid, bool, Exception?> OutboxEnqueued =
        LoggerMessage.Define<Guid, bool>(
            LogLevel.Debug,
            new EventId(4210, nameof(OutboxEnqueued)),
            "Outbox event {EventId} enqueue completed; created={Created}");

    private static readonly Action<ILogger, string, int, Exception?> OutboxClaimed =
        LoggerMessage.Define<string, int>(
            LogLevel.Debug,
            new EventId(4211, nameof(OutboxClaimed)),
            "Outbox publisher {LeaseOwner} claimed {ClaimCount} events");

    private static readonly Action<ILogger, Guid, string, Exception?> OutboxMutated =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(4212, nameof(OutboxMutated)),
            "Outbox event {EventId} mutation result is {ResultState}");

    private static readonly Action<ILogger, string, string, Exception?> HealthWritten =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(4220, nameof(HealthWritten)),
            "Health component {Component} write result is {WriteStatus}");

    public void Enqueued(DurableTaskEnqueueResult result) =>
        TaskEnqueued(logger, result.TaskId.Value, result.Status == DurableTaskEnqueueStatus.Created, null);

    public void Claimed(LeaseOwner owner, int count) => TasksClaimed(logger, owner.Value, count, null);

    public void Finished(DurableTaskId taskId, DurableTaskFinishResult result) =>
        TaskFinished(logger, taskId.Value, result.State?.ToString() ?? result.Status.ToString(), null);

    public void Cancellation(DurableTaskId taskId, DurableTaskCancellationResult result) =>
        CancellationRequested(logger, taskId.Value, result.Status.ToString(), null);

    public void Enqueued(OutboxEnqueueResult result) =>
        OutboxEnqueued(logger, result.EventId.Value, result.Status == OutboxEnqueueStatus.Created, null);

    public void ClaimedOutbox(LeaseOwner owner, int count) => OutboxClaimed(logger, owner.Value, count, null);

    public void Mutated(OutboxEventId eventId, OutboxMutationResult result) =>
        OutboxMutated(logger, eventId.Value, result.State?.ToString() ?? result.Status.ToString(), null);

    public void Health(HealthComponentName component, HealthStatusWriteResult result) =>
        HealthWritten(logger, component.Value, result.Status.ToString(), null);
}
