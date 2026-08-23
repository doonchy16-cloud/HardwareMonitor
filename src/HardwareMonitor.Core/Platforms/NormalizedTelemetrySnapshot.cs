using TheSpark.HardwareMonitor.Core.Devices;

namespace TheSpark.HardwareMonitor.Core.Platforms;

public sealed class NormalizedTelemetrySnapshot
{
    public NormalizedTelemetrySnapshot(
        Guid deviceId,
        DevicePlatform platform,
        DateTimeOffset capturedAt,
        IReadOnlyList<NormalizedMetric> metrics)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device ID must not be empty.", nameof(deviceId));
        }

        ArgumentNullException.ThrowIfNull(metrics);

        DeviceId = deviceId;
        Platform = platform;
        CapturedAt = capturedAt;
        Metrics = metrics.ToArray();
    }

    public Guid DeviceId { get; }
    public DevicePlatform Platform { get; }
    public DateTimeOffset CapturedAt { get; }
    public IReadOnlyList<NormalizedMetric> Metrics { get; }
}
