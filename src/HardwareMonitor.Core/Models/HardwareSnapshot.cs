namespace TheSpark.HardwareMonitor.Core.Models;

public sealed record HardwareSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<HardwareDeviceSnapshot> Devices,
    string EngineStatus,
    string? ErrorMessage = null)
{
    public static HardwareSnapshot Empty(DateTimeOffset capturedAt) =>
        new(capturedAt, Array.Empty<HardwareDeviceSnapshot>(), "Initializing");
}
