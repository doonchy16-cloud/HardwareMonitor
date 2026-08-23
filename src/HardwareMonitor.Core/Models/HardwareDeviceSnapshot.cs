namespace TheSpark.HardwareMonitor.Core.Models;

public sealed record HardwareDeviceSnapshot(
    string Id,
    string Name,
    HardwareKind Kind,
    IReadOnlyList<SensorReading> Sensors);
