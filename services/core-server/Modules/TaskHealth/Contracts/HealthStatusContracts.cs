namespace AssetLibrary.Modules.TaskHealth.Contracts;

public enum HealthScopeKind
{
    System = 1,
    Library = 2,
    Asset = 3,
}

public enum HealthState
{
    Normal = 0,
    Degraded = 1,
    Warning = 2,
    Offline = 3,
    Maintenance = 4,
    Initializing = 5,
}

public readonly record struct HealthScope
{
    public HealthScope(HealthScopeKind kind, Guid? resourceId)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == HealthScopeKind.System
                ? resourceId is not null
                : resourceId is null || resourceId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "System health has no resource ID; library and asset health require a non-empty ID.",
                nameof(resourceId));
        }

        Kind = kind;
        ResourceId = resourceId;
    }

    public HealthScopeKind Kind { get; }

    public Guid? ResourceId { get; }

    public static HealthScope System => new(HealthScopeKind.System, null);
}

public sealed record HealthStatusUpdate(
    HealthScope Scope,
    HealthComponentName Component,
    HealthState State,
    HealthReasonCode? ReasonCode,
    DateTimeOffset ObservedAt);

public enum HealthStatusWriteStatus
{
    Applied = 0,
    Stale = 1,
}

public sealed record HealthStatusWriteResult(HealthStatusWriteStatus Status);

public sealed record HealthStatusSnapshot(
    HealthScope Scope,
    HealthComponentName Component,
    HealthState State,
    HealthReasonCode? ReasonCode,
    DateTimeOffset ObservedAt,
    DateTimeOffset UpdatedAt);
