using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Status;

public static class ProfileStatusEvaluator
{
    public static ProfileStatus Evaluate(
        DateTimeOffset now,
        DateTimeOffset? lastTelemetryAt,
        FreshnessPolicy policy,
        ActivityState activity,
        HealthState health)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!lastTelemetryAt.HasValue)
        {
            return new ProfileStatus(ConnectivityState.Offline, activity, health, null);
        }

        var age = now - lastTelemetryAt.Value;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var connectivity = age > policy.OfflineAfter
            ? ConnectivityState.Offline
            : age > policy.StaleAfter
                ? ConnectivityState.Stale
                : ConnectivityState.Online;

        return new ProfileStatus(connectivity, activity, health, age);
    }
}
