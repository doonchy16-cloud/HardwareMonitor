using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class SensorReadingTests
{
    [Fact]
    public void Reading_is_stale_after_configured_age()
    {
        var captured = new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);
        var reading = new SensorReading("cpu.temp", "CPU Package", SensorKind.Temperature, 52.5, "°C", captured, SensorAvailability.Available);

        Assert.False(reading.IsStale(captured.AddSeconds(2), TimeSpan.FromSeconds(3)));
        Assert.True(reading.IsStale(captured.AddSeconds(4), TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void Unavailable_reading_is_never_reported_live()
    {
        var captured = DateTimeOffset.UtcNow;
        var reading = new SensorReading("gpu.hotspot", "GPU Hotspot", SensorKind.Temperature, null, "°C", captured, SensorAvailability.NotExposed);

        Assert.False(reading.IsLive(captured, TimeSpan.FromSeconds(3)));
    }
}
