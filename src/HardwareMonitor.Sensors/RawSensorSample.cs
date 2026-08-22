using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Sensors;

public sealed record RawSensorSample(
    string Id,
    string Name,
    SensorKind Kind,
    double? Value,
    string Unit);
