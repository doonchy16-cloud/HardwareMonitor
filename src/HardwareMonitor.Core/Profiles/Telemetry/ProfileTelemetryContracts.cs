using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Core.Profiles.Telemetry;

public enum ProfileThermalState
{
    Unknown,
    Normal,
    Warning,
    Critical,
}

public sealed record ProfileSensorReading(
    string Id,
    string Name,
    SensorKind Kind,
    double? Value,
    string Unit,
    DateTimeOffset CapturedAt,
    SensorAvailability Availability,
    ProfileThermalState ThermalState);

public sealed record ProfileHardwareDeviceSnapshot(
    string Id,
    string Name,
    HardwareKind Kind,
    IReadOnlyList<ProfileSensorReading> Sensors);

public sealed record ProfileTelemetrySnapshot(
    Guid ProfileId,
    string DisplayName,
    ProfileRole Roles,
    string SourceDeviceId,
    DateTimeOffset CapturedAt,
    string EngineStatus,
    FreshnessPolicy Freshness,
    IReadOnlyList<ProfileHardwareDeviceSnapshot> Devices);

public static class ProfileTelemetryRouter
{
    private const ProfileRole TelemetryRoles = ProfileRole.Publisher | ProfileRole.TrainingMonitor;

    public static IReadOnlyList<ProfileTelemetrySnapshot> Route(
        string sourceDeviceId,
        HardwareSnapshot snapshot,
        ProfileRegistryDocument registry)
    {
        if (string.IsNullOrWhiteSpace(sourceDeviceId))
        {
            throw new ArgumentException("Source device ID cannot be blank.", nameof(sourceDeviceId));
        }

        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registry);

        var normalizedDeviceId = sourceDeviceId.Trim();
        var routed = new List<ProfileTelemetrySnapshot>();

        foreach (var profile in registry.Profiles)
        {
            if (!ShouldRoute(profile, normalizedDeviceId))
            {
                continue;
            }

            routed.Add(new ProfileTelemetrySnapshot(
                profile.Id,
                profile.DisplayName,
                profile.Roles,
                normalizedDeviceId,
                snapshot.CapturedAt,
                snapshot.EngineStatus,
                profile.Freshness,
                RouteDevices(snapshot.Devices, profile)));
        }

        return routed.ToArray();
    }

    private static bool ShouldRoute(MonitoringProfile profile, string sourceDeviceId) =>
        profile.Enabled
        && (profile.Roles & TelemetryRoles) != 0
        && profile.DeviceBindings.Any(binding =>
            string.Equals(binding.DeviceId, sourceDeviceId, StringComparison.Ordinal));

    private static IReadOnlyList<ProfileHardwareDeviceSnapshot> RouteDevices(
        IReadOnlyList<HardwareDeviceSnapshot> devices,
        MonitoringProfile profile)
    {
        var routed = new ProfileHardwareDeviceSnapshot[devices.Count];
        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            var sensors = RouteSensors(device.Sensors, profile);
            routed[index] = new ProfileHardwareDeviceSnapshot(
                device.Id,
                device.Name,
                device.Kind,
                sensors);
        }

        return routed;
    }

    private static IReadOnlyList<ProfileSensorReading> RouteSensors(
        IReadOnlyList<SensorReading> sensors,
        MonitoringProfile profile)
    {
        var includeUnavailable = profile.SensorVisibility.UnavailableSensors == UnavailableSensorBehavior.ShowUnavailable;
        var routed = new List<ProfileSensorReading>(sensors.Count);

        foreach (var sensor in sensors)
        {
            if (!includeUnavailable && sensor.Availability != SensorAvailability.Available)
            {
                continue;
            }

            routed.Add(new ProfileSensorReading(
                sensor.Id,
                sensor.Name,
                sensor.Kind,
                sensor.Value,
                sensor.Unit,
                sensor.CapturedAt,
                sensor.Availability,
                ClassifyThermal(sensor, profile.Thermal)));
        }

        return routed.ToArray();
    }

    private static ProfileThermalState ClassifyThermal(SensorReading sensor, ThermalPolicy policy)
    {
        if (sensor.Kind != SensorKind.Temperature
            || sensor.Availability != SensorAvailability.Available
            || !sensor.Value.HasValue
            || !double.IsFinite(sensor.Value.Value))
        {
            return ProfileThermalState.Unknown;
        }

        return sensor.Value.Value >= policy.CriticalCelsius
            ? ProfileThermalState.Critical
            : sensor.Value.Value >= policy.WarningCelsius
                ? ProfileThermalState.Warning
                : ProfileThermalState.Normal;
    }
}
