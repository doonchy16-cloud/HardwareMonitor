using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Sensors;

public sealed record RawHardwareSample(
    string Id,
    string Name,
    HardwareKind Kind,
    IReadOnlyList<RawSensorSample> Sensors);
