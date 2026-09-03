using AssetLibrary.Modules.TaskHealth.Contracts;
using AssetLibrary.Modules.TaskHealth.Domain;

namespace AssetLibrary.Modules.TaskHealth.Application;

internal static class TaskHealthRequestValidator
{
    public static void Validate(DurableTaskEnqueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.TaskId.Value, nameof(request.TaskId));
        Require(request.IdempotencyKey.Value, nameof(request.IdempotencyKey));
        Require(request.TaskType.Value, nameof(request.TaskType));
        Require(request.Payload.Value, nameof(request.Payload));
        if (!Enum.IsDefined(request.Priority))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        ValidateAttempts(request.MaxAttempts, nameof(request.MaxAttempts));
    }

    public static void Validate(DurableTaskClaimRequest request, TaskHealthExecutionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.Worker.Value, nameof(request.Worker));
        ValidateLeaseDuration(request.LeaseDuration);
        ValidateClaimBatch(request.BatchSize, limits);
    }

    public static void Validate(DurableTaskHeartbeatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.TaskId.Value, nameof(request.TaskId));
        Validate(request.Identity);
        ValidateLeaseDuration(request.LeaseDuration);
    }

    public static void Validate(DurableTaskFinishRequest request)
    {
        DurableTaskStatePolicy.ValidateFinish(request);
        Require(request.TaskId.Value, nameof(request.TaskId));
        Validate(request.Identity);
        if (!Enum.IsDefined(request.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.FailureCode is { } failureCode)
        {
            Require(failureCode.Value, nameof(request.FailureCode));
        }
    }

    public static void Validate(OutboxEnqueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.EventId.Value, nameof(request.EventId));
        Require(request.SourceModule.Value, nameof(request.SourceModule));
        Require(request.EventType.Value, nameof(request.EventType));
        Require(request.Payload.Value, nameof(request.Payload));
        if (request.AggregateId == Guid.Empty)
        {
            throw new ArgumentException("An aggregate ID must be non-empty when present.", nameof(request));
        }

        if (request.SchemaVersion is <= 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Schema version is outside the supported range.");
        }

        ValidateAttempts(request.MaxPublishAttempts, nameof(request.MaxPublishAttempts));
        if (request.OccurredAt == default)
        {
            throw new ArgumentException("An outbox event requires an occurrence timestamp.", nameof(request));
        }
    }

    public static void Validate(OutboxClaimRequest request, TaskHealthExecutionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.Publisher.Value, nameof(request.Publisher));
        ValidateLeaseDuration(request.LeaseDuration);
        ValidateClaimBatch(request.BatchSize, limits);
    }

    public static void Validate(OutboxPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.EventId.Value, nameof(request.EventId));
        Validate(request.Identity);
    }

    public static void Validate(OutboxReleaseRequest request)
    {
        OutboxStatePolicy.ValidateRelease(request);
        Require(request.EventId.Value, nameof(request.EventId));
        Validate(request.Identity);
        Require(request.FailureCode.Value, nameof(request.FailureCode));
    }

    public static void Validate(HealthStatusUpdate update)
    {
        HealthStatusPolicy.Validate(update);
        if (!Enum.IsDefined(update.Scope.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(update));
        }

        Require(update.Component.Value, nameof(update.Component));
        if (update.Scope.Kind != HealthScopeKind.System
            && (update.Scope.ResourceId is null || update.Scope.ResourceId.Value == Guid.Empty))
        {
            throw new ArgumentException("A scoped health update requires a resource ID.", nameof(update));
        }

        if (update.ReasonCode is { } reasonCode)
        {
            Require(reasonCode.Value, nameof(update.ReasonCode));
        }
    }

    public static void ValidateReclaimBatch(int batchSize, TaskHealthExecutionLimits limits)
    {
        if (batchSize <= 0 || batchSize > limits.MaximumReclaimBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }
    }

    public static void Validate(DurableTaskId taskId) => Require(taskId.Value, nameof(taskId));

    private static void Validate(TaskLeaseIdentity identity)
    {
        Require(identity.Owner.Value, nameof(identity));
        Require(identity.Token.Value, nameof(identity));
        if (identity.Generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }
    }

    private static void Validate(OutboxLeaseIdentity identity)
    {
        Require(identity.Owner.Value, nameof(identity));
        Require(identity.Token.Value, nameof(identity));
        if (identity.Generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }
    }

    private static void ValidateLeaseDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1)
            || duration > TaskHealthExecutionLimits.AbsoluteMaximumLeaseDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "A lease must be between one second and one hour.");
        }
    }

    private static void ValidateClaimBatch(int batchSize, TaskHealthExecutionLimits limits)
    {
        if (batchSize <= 0 || batchSize > limits.MaximumClaimBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }
    }

    private static void ValidateAttempts(int attempts, string parameterName)
    {
        if (attempts is <= 0 or > TaskHealthExecutionLimits.AbsoluteMaximumAttempts)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void Require(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
        }
    }

    private static void Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }
    }
}
