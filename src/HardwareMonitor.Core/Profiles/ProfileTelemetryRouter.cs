using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Core.Profiles;

public static class ProfileTelemetryRouter
{
    public static IReadOnlyList<ProfileTelemetrySnapshot> Route(
        Guid sourceDeviceId,
        HardwareSnapshot snapshot,
        IReadOnlyCollection<HardwareProfile> profiles,
        DateTimeOffset receivedAt)
    {
        if (sourceDeviceId == Guid.Empty)
        {
            throw new ArgumentException("Source device ID must not be empty.", nameof(sourceDeviceId));
        }

        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(profiles);

        var results = new List<ProfileTelemetrySnapshot>();

        foreach (var profile in profiles)
        {
            if (!profile.Enabled || profile.DeviceId != sourceDeviceId)
            {
                continue;
            }

            if (!profile.Capabilities.Contains(ProfileCapability.PublishHardwareTelemetry))
            {
                continue;
            }

            var metrics = snapshot.Devices
                .SelectMany(device => device.Sensors)
                .Where(sensor =>
                    sensor.Availability == SensorAvailability.Available &&
                    sensor.Value.HasValue &&
                    profile.SensorVisibilityPolicy.IsVisible(sensor))
                .ToArray();

            results.Add(new ProfileTelemetrySnapshot(
                profile.ProfileId,
                sourceDeviceId,
                snapshot.CapturedAt,
                receivedAt,
                metrics,
                snapshot.EngineStatus,
                snapshot.ErrorMessage));
        }

        return results;
    }
}
