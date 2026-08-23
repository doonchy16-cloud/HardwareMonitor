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
    string? ErrorMessage,
    FreshnessPolicy Freshness,
    IReadOnlyList<ProfileHardwareDeviceSnapshot> Devices);

public static class ProfileTelemetryRouter
{
    public static IReadOnlyList<ProfileTelemetrySnapshot> Route(
        string sourceDeviceId,
        HardwareSnapshot snapshot,
        ProfileRegistryDocument registry) => Array.Empty<ProfileTelemetrySnapshot>();
}
