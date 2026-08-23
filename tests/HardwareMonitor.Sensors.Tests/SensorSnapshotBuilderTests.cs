using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Sensors.Tests;

public sealed class SensorSnapshotBuilderTests
{
    [Fact]
    public void Builder_maps_backend_neutral_samples_into_device_snapshots()
    {
        var captured = new DateTimeOffset(2026, 8, 22, 7, 0, 0, TimeSpan.Zero);
        var raw = new RawHardwareSample(
            "cpu/0",
            "AMD Ryzen Test CPU",
            HardwareKind.Cpu,
            [
                new RawSensorSample("cpu/0/temp/0", "CPU Package", SensorKind.Temperature, 54.25, "°C"),
                new RawSensorSample("cpu/0/load/0", "CPU Total", SensorKind.Load, 27.5, "%")
            ]);

        var snapshot = SensorSnapshotBuilder.Build([raw], captured);

        var device = Assert.Single(snapshot.Devices);
        Assert.Equal(HardwareKind.Cpu, device.Kind);
        Assert.Equal("AMD Ryzen Test CPU", device.Name);
        Assert.Equal(2, device.Sensors.Count);
        Assert.All(device.Sensors, reading => Assert.Equal(captured, reading.CapturedAt));
        Assert.All(device.Sensors, reading => Assert.Equal(SensorAvailability.Available, reading.Availability));
    }

    [Fact]
    public void Null_backend_value_is_marked_not_exposed_instead_of_zero()
    {
        var raw = new RawHardwareSample(
            "gpu/0",
            "GPU",
            HardwareKind.Gpu,
            [new RawSensorSample("gpu/0/hotspot", "GPU Hotspot", SensorKind.Temperature, null, "°C")]);

        var snapshot = SensorSnapshotBuilder.Build([raw], DateTimeOffset.UtcNow);
        var reading = Assert.Single(Assert.Single(snapshot.Devices).Sensors);

        Assert.Null(reading.Value);
        Assert.Equal(SensorAvailability.NotExposed, reading.Availability);
    }

    [Fact]
    public void Builder_keeps_multiple_devices_separate()
    {
        var samples = new[]
        {
            new RawHardwareSample("gpu/0", "GPU A", HardwareKind.Gpu, []),
            new RawHardwareSample("gpu/1", "GPU B", HardwareKind.Gpu, []),
            new RawHardwareSample("storage/0", "NVMe", HardwareKind.Storage, [])
        };

        var snapshot = SensorSnapshotBuilder.Build(samples, DateTimeOffset.UtcNow);

        Assert.Equal(3, snapshot.Devices.Count);
        Assert.Equal(2, snapshot.Devices.Count(device => device.Kind == HardwareKind.Gpu));
    }
}
