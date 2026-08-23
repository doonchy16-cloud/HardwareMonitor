using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileTelemetryRouterTests
{
    private static readonly DateTimeOffset CapturedAt =
        DateTimeOffset.Parse("2026-08-23T06:30:00Z");

    private static HardwareSnapshot SnapshotWithGpuSensors() =>
        new(
            CapturedAt,
            new[]
            {
                new HardwareDeviceSnapshot(
                    "gpu-0",
                    "GPU",
                    HardwareKind.Gpu,
                    new[]
                    {
                        new SensorReading(
                            "gpu-temp",
                            "GPU Core",
                            SensorKind.Temperature,
                            74,
                            "°C",
                            CapturedAt,
                            SensorAvailability.Available),
                        new SensorReading(
                            "gpu-hotspot",
                            "GPU Hotspot",
                            SensorKind.Temperature,
                            null,
                            "°C",
                            CapturedAt,
                            SensorAvailability.NotExposed),
                        new SensorReading(
                            "gpu-load",
                            "GPU Load",
                            SensorKind.Load,
                            99,
                            "%",
                            CapturedAt,
                            SensorAvailability.Available)
                    })
            },
            "Healthy");

    private static HardwareProfile Profile(
        Guid profileId,
        Guid? deviceId,
        SensorVisibilityPolicy? visibility = null) =>
        new(
            profileId,
            $"Profile-{profileId:N}",
            deviceId,
            new HashSet<ProfileCapability> { ProfileCapability.PublishHardwareTelemetry },
            ViewerScope.None,
            new HashSet<Guid>(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
            sensorVisibilityPolicy: visibility);

    [Fact]
    public void One_device_snapshot_routes_to_every_enabled_profile_bound_to_that_device()
    {
        var deviceId = Guid.NewGuid();
        var first = Profile(Guid.NewGuid(), deviceId);
        var second = Profile(Guid.NewGuid(), deviceId);
        var otherDevice = Profile(Guid.NewGuid(), Guid.NewGuid());

        var routed = ProfileTelemetryRouter.Route(
            deviceId,
            SnapshotWithGpuSensors(),
            new[] { first, second, otherDevice },
            CapturedAt.AddMilliseconds(100));

        Assert.Equal(2, routed.Count);
        Assert.Contains(routed, item => item.ProfileId == first.ProfileId);
        Assert.Contains(routed, item => item.ProfileId == second.ProfileId);
        Assert.DoesNotContain(routed, item => item.ProfileId == otherDevice.ProfileId);
    }

    [Fact]
    public void Unbound_viewer_profile_does_not_receive_physical_hardware_telemetry()
    {
        var deviceId = Guid.NewGuid();
        var viewer = new HardwareProfile(
            Guid.NewGuid(),
            "Viewer",
            null,
            new HashSet<ProfileCapability> { ProfileCapability.ViewProfiles },
            ViewerScope.AllProfiles,
            new HashSet<Guid>(),
            new FreshnessPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60)));

        var routed = ProfileTelemetryRouter.Route(
            deviceId,
            SnapshotWithGpuSensors(),
            new[] { viewer },
            CapturedAt);

        Assert.Empty(routed);
    }

    [Fact]
    public void Unavailable_or_not_exposed_sensor_rows_are_omitted_from_profile_metrics()
    {
        var deviceId = Guid.NewGuid();
        var profile = Profile(Guid.NewGuid(), deviceId);

        var routed = Assert.Single(ProfileTelemetryRouter.Route(
            deviceId,
            SnapshotWithGpuSensors(),
            new[] { profile },
            CapturedAt));

        Assert.Contains(routed.Metrics, metric => metric.Id == "gpu-temp" && metric.Value == 74);
        Assert.Contains(routed.Metrics, metric => metric.Id == "gpu-load" && metric.Value == 99);
        Assert.DoesNotContain(routed.Metrics, metric => metric.Id == "gpu-hotspot");
    }

    [Fact]
    public void Per_profile_sensor_visibility_can_produce_different_views_of_same_device()
    {
        var deviceId = Guid.NewGuid();
        var temperaturesOnly = Profile(
            Guid.NewGuid(),
            deviceId,
            new SensorVisibilityPolicy(new HashSet<SensorKind> { SensorKind.Temperature }));
        var loadsOnly = Profile(
            Guid.NewGuid(),
            deviceId,
            new SensorVisibilityPolicy(new HashSet<SensorKind> { SensorKind.Load }));

        var routed = ProfileTelemetryRouter.Route(
            deviceId,
            SnapshotWithGpuSensors(),
            new[] { temperaturesOnly, loadsOnly },
            CapturedAt);

        var tempView = Assert.Single(routed, item => item.ProfileId == temperaturesOnly.ProfileId);
        var loadView = Assert.Single(routed, item => item.ProfileId == loadsOnly.ProfileId);

        Assert.All(tempView.Metrics, metric => Assert.Equal(SensorKind.Temperature, metric.Kind));
        Assert.All(loadView.Metrics, metric => Assert.Equal(SensorKind.Load, metric.Kind));
    }

    [Fact]
    public void Disabled_profile_is_not_routed()
    {
        var deviceId = Guid.NewGuid();
        var disabled = new HardwareProfile(
            Guid.NewGuid(),
            "Disabled",
            deviceId,
            new HashSet<ProfileCapability> { ProfileCapability.PublishHardwareTelemetry },
            ViewerScope.None,
            new HashSet<Guid>(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
            enabled: false);

        var routed = ProfileTelemetryRouter.Route(
            deviceId,
            SnapshotWithGpuSensors(),
            new[] { disabled },
            CapturedAt);

        Assert.Empty(routed);
    }
}
