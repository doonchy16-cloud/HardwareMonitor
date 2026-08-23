namespace TheSpark.HardwareMonitor.Core.Devices;

public sealed class DeviceRecord
{
    public DeviceRecord(
        Guid deviceId,
        string userAlias,
        DevicePlatform platform,
        string architecture,
        DateTimeOffset? lastHeartbeatAt,
        DateTimeOffset? lastTelemetryAt)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device ID must not be empty.", nameof(deviceId));
        }

        if (string.IsNullOrWhiteSpace(userAlias))
        {
            throw new ArgumentException("Device alias must not be empty.", nameof(userAlias));
        }

        if (string.IsNullOrWhiteSpace(architecture))
        {
            throw new ArgumentException("Architecture must not be empty.", nameof(architecture));
        }

        DeviceId = deviceId;
        UserAlias = userAlias.Trim();
        Platform = platform;
        Architecture = architecture.Trim();
        LastHeartbeatAt = lastHeartbeatAt;
        LastTelemetryAt = lastTelemetryAt;
    }

    public Guid DeviceId { get; }
    public string UserAlias { get; }
    public DevicePlatform Platform { get; }
    public string Architecture { get; }
    public DateTimeOffset? LastHeartbeatAt { get; }
    public DateTimeOffset? LastTelemetryAt { get; }
}
