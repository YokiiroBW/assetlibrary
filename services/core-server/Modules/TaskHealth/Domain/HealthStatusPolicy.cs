using AssetLibrary.Modules.TaskHealth.Contracts;

namespace AssetLibrary.Modules.TaskHealth.Domain;

public static class HealthStatusPolicy
{
    public static void Validate(HealthStatusUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!Enum.IsDefined(update.State))
        {
            throw new ArgumentOutOfRangeException(nameof(update));
        }

        var requiresReason = update.State != HealthState.Normal;
        if (requiresReason != update.ReasonCode.HasValue)
        {
            throw new ArgumentException(
                "Non-normal health requires a reason code; normal health must clear it.",
                nameof(update));
        }

        if (update.ObservedAt == default)
        {
            throw new ArgumentException("Health observations require a timestamp.", nameof(update));
        }
    }
}
