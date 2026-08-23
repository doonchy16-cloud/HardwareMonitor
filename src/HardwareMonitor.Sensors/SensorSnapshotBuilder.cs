using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Sensors;

public static class SensorSnapshotBuilder
{
    public static HardwareSnapshot Build(IEnumerable<RawHardwareSample> rawDevices, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(rawDevices);

        var devices = rawDevices
            .Select(device => new HardwareDeviceSnapshot(
                device.Id,
                device.Name,
                device.Kind,
                device.Sensors.Select(sensor =>
                {
                    var value = sensor.Value is { } rawValue && double.IsFinite(rawValue)
                        ? rawValue
                        : (double?)null;
                    return new SensorReading(
                        sensor.Id,
                        sensor.Name,
                        sensor.Kind,
                        value,
                        sensor.Unit,
                        capturedAt,
                        value.HasValue ? SensorAvailability.Available : SensorAvailability.NotExposed);
                }).ToArray()))
            .ToArray();

        return new HardwareSnapshot(capturedAt, devices, "Healthy");
    }
}
