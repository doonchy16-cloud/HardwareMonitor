using TheSpark.HardwareMonitor.Core.Devices;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Platforms;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class PlatformTelemetryContractTests
{
    [Fact]
    public void Android_can_be_healthy_without_gpu_temperature_capability()
    {
        var capabilities = new PlatformCapabilities(
            DevicePlatform.Android,
            new HashSet<PlatformTelemetryCapability>
            {
                PlatformTelemetryCapability.BatteryLevel,
                PlatformTelemetryCapability.BatteryTemperature,
                PlatformTelemetryCapability.MemoryUsage,
                PlatformTelemetryCapability.StorageUsage
            });

        Assert.True(capabilities.Supports(PlatformTelemetryCapability.BatteryLevel));
        Assert.False(capabilities.Supports(PlatformTelemetryCapability.GpuTemperature));
    }

    [Fact]
    public void Unsupported_metrics_are_absent_instead_of_fake_zero_rows()
    {
        var deviceId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.Parse("2026-08-23T06:20:00Z");
        var snapshot = new NormalizedTelemetrySnapshot(
            deviceId,
            DevicePlatform.Android,
            capturedAt,
            new[]
            {
                new NormalizedMetric(
                    "battery.level",
                    "Battery",
                    71,
                    null,
                    "%",
                    capturedAt,
                    SensorAvailability.Available)
            });

        Assert.Single(snapshot.Metrics);
        Assert.DoesNotContain(snapshot.Metrics, metric => metric.Key == "gpu.temperature");
    }

    [Fact]
    public void Normalized_metric_can_represent_text_state_without_inventing_numeric_value()
    {
        var capturedAt = DateTimeOffset.Parse("2026-08-23T06:20:00Z");
        var metric = new NormalizedMetric(
            "network.type",
            "Network",
            null,
            "Wi-Fi",
            string.Empty,
            capturedAt,
            SensorAvailability.Available);

        Assert.Null(metric.NumericValue);
        Assert.Equal("Wi-Fi", metric.TextValue);
    }

    [Fact]
    public void Platform_snapshot_requires_non_empty_device_identity()
    {
        Assert.Throws<ArgumentException>(() =>
            new NormalizedTelemetrySnapshot(
                Guid.Empty,
                DevicePlatform.Web,
                DateTimeOffset.UtcNow,
                Array.Empty<NormalizedMetric>()));
    }
}
