using TheSpark.HardwareMonitor.App.ViewModels;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Core.Status;

namespace TheSpark.HardwareMonitor.App.Services;

public static class ProfileCardPresenter
{
    public static ProfileCardViewModel Present(
        HardwareProfile profile,
        ProfileTelemetrySnapshot? telemetry,
        ProfileStatus status)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(status);

        var isOffline = status.Connectivity == ConnectivityState.Offline;
        var isHistorical = status.Connectivity == ConnectivityState.Stale;

        var metrics = isOffline || telemetry is null
            ? Array.Empty<ProfileMetricRowViewModel>()
            : telemetry.Metrics
                .Where(reading =>
                    reading.Availability == SensorAvailability.Available &&
                    reading.Value.HasValue)
                .Select(reading => new ProfileMetricRowViewModel(
                    reading.Id,
                    reading.Name,
                    reading.Kind,
                    reading.Value!.Value,
                    reading.Unit))
                .ToArray();

        return new ProfileCardViewModel(
            profile.ProfileId,
            profile.Name,
            status.Connectivity,
            status.Activity,
            status.Health,
            FormatStatus(status),
            FormatLastSeen(status.TelemetryAge),
            !isOffline && metrics.Length > 0,
            isHistorical,
            metrics);
    }

    private static string FormatStatus(ProfileStatus status)
    {
        var connectivity = status.Connectivity.ToString().ToUpperInvariant();
        if (status.Connectivity == ConnectivityState.Offline)
        {
            return connectivity;
        }

        var parts = new List<string> { connectivity };
        if (status.Activity == ActivityState.Training)
        {
            parts.Add("TRAINING");
        }

        if (status.Health == HealthState.Degraded)
        {
            parts.Add("DEGRADED");
        }
        else if (status.Health == HealthState.Error)
        {
            parts.Add("ERROR");
        }

        return string.Join(" · ", parts);
    }

    private static string FormatLastSeen(TimeSpan? telemetryAge)
    {
        if (!telemetryAge.HasValue)
        {
            return "Never seen";
        }

        var age = telemetryAge.Value < TimeSpan.Zero ? TimeSpan.Zero : telemetryAge.Value;
        if (age < TimeSpan.FromMinutes(1))
        {
            return $"Last seen {(int)Math.Floor(age.TotalSeconds)}s ago";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"Last seen {(int)Math.Floor(age.TotalMinutes)}m ago";
        }

        return $"Last seen {(int)Math.Floor(age.TotalHours)}h ago";
    }
}
