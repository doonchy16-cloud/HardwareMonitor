using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class ProfileTelemetrySnapshot
{
    public ProfileTelemetrySnapshot(
        Guid profileId,
        Guid deviceId,
        DateTimeOffset capturedAt,
        DateTimeOffset receivedAt,
        IReadOnlyList<SensorReading> metrics,
        string engineStatus,
        string? errorMessage)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must not be empty.", nameof(profileId));
        }

        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device ID must not be empty.", nameof(deviceId));
        }

        ArgumentNullException.ThrowIfNull(metrics);

        ProfileId = profileId;
        DeviceId = deviceId;
        CapturedAt = capturedAt;
        ReceivedAt = receivedAt;
        Metrics = metrics.ToArray();
        EngineStatus = engineStatus ?? string.Empty;
        ErrorMessage = errorMessage;
    }

    public Guid ProfileId { get; }
    public Guid DeviceId { get; }
    public DateTimeOffset CapturedAt { get; }
    public DateTimeOffset ReceivedAt { get; }
    public IReadOnlyList<SensorReading> Metrics { get; }
    public string EngineStatus { get; }
    public string? ErrorMessage { get; }
}
