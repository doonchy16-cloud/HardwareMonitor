namespace TheSpark.HardwareMonitor.Core.Devices;

public sealed record DeviceRecord(
    Guid DeviceId,
    string UserAlias,
    DevicePlatform Platform,
    string Architecture,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastTelemetryAt)
{
    public DeviceRecord : this(
        DeviceId,
        string.IsNullOrWhiteSpace(UserAlias) ? throw new ArgumentException("Device alias must not be empty.", nameof(UserAlias)) : UserAlias.Trim(),
        Platform,
        string.IsNullOrWhiteSpace(Architecture) ? throw new ArgumentException("Architecture must not be empty.", nameof(Architecture)) : Architecture.Trim(),
        LastHeartbeatAt,
        LastTelemetryAt)
    {
        if (DeviceId == Guid.Empty)
        {
            throw new ArgumentException("Device ID must not be empty.", nameof(DeviceId));
        }
    }
}
