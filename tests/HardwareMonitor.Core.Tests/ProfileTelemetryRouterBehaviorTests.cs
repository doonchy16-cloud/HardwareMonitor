using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Core.Profiles.Telemetry;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileTelemetryRouterBehaviorTests
{
    [Fact]
    public void Route_emits_only_enabled_matching_telemetry_profiles_and_fans_out_one_device()
    {
        const string deviceId = "device-alpha";
        var snapshot = CreateSnapshot();
        var publisher = CreateProfile("Publisher A", ProfileRole.Publisher, true, [new DeviceBinding(deviceId)]);
        var training = CreateProfile("Training B", ProfileRole.TrainingMonitor, true, [new DeviceBinding(deviceId)]);
        var viewerOnly = CreateProfile("Viewer", ProfileRole.Viewer, true, [new DeviceBinding(deviceId)]);
        var disabled = CreateProfile("Disabled", ProfileRole.Publisher, false, [new DeviceBinding(deviceId)]);
        var unbound = CreateProfile("Unbound", ProfileRole.Publisher, true, []);
        var otherDevice = CreateProfile("Other", ProfileRole.Publisher, true, [new DeviceBinding("device-beta")]);
        var registry = Registry(publisher, training, viewerOnly, disabled, unbound, otherDevice);

        var routed = ProfileTelemetryRouter.Route(deviceId, snapshot, registry);

        Assert.Equal(2, routed.Count);
        Assert.Equal([publisher.Id, training.Id], routed.Select(item => item.ProfileId).ToArray());
        Assert.All(routed, item => Assert.Equal(deviceId, item.SourceDeviceId));
    }

    [Fact]
    public void Hide_unavailable_sensors_removes_them_without_inventing_values()
    {
        const string deviceId = "device-alpha";
        var snapshot = CreateSnapshot(
            new SensorReading("temp", "CPU Package", SensorKind.Temperature, 84, "°C", CapturedAt, SensorAvailability.Available),
            new SensorReading("load", "CPU Total", SensorKind.Load, null, "%", CapturedAt, SensorAvailability.Error));
        var profile = CreateProfile(
            "Hide unavailable",
            ProfileRole.Publisher,
            true,
            [new DeviceBinding(deviceId)],
            unavailable: UnavailableSensorBehavior.Hide);

        var routed = Assert.Single(ProfileTelemetryRouter.Route(deviceId, snapshot, Registry(profile)));
        var device = Assert.Single(routed.Devices);
        var sensor = Assert.Single(device.Sensors);

        Assert.Equal("temp", sensor.Id);
        Assert.Equal(84, sensor.Value);
    }

    [Fact]
    public void Show_unavailable_sensors_preserves_null_and_availability()
    {
        const string deviceId = "device-alpha";
        var snapshot = CreateSnapshot(
            new SensorReading("missing", "Unavailable Temperature", SensorKind.Temperature, null, "°C", CapturedAt, SensorAvailability.NotExposed));
        var profile = CreateProfile(
            "Show unavailable",
            ProfileRole.Publisher,
            true,
            [new DeviceBinding(deviceId)],
            unavailable: UnavailableSensorBehavior.ShowUnavailable);

        var routed = Assert.Single(ProfileTelemetryRouter.Route(deviceId, snapshot, Registry(profile)));
        var sensor = Assert.Single(Assert.Single(routed.Devices).Sensors);

        Assert.Null(sensor.Value);
        Assert.Equal(SensorAvailability.NotExposed, sensor.Availability);
        Assert.Equal(ProfileThermalState.Unknown, sensor.ThermalState);
    }

    [Fact]
    public void Same_temperature_is_classified_against_each_profiles_own_thresholds()
    {
        const string deviceId = "device-alpha";
        var snapshot = CreateSnapshot(
            new SensorReading("gpu-temp", "GPU Core", SensorKind.Temperature, 85, "°C", CapturedAt, SensorAvailability.Available));
        var warningProfile = CreateProfile(
            "Warning policy",
            ProfileRole.Publisher,
            true,
            [new DeviceBinding(deviceId)],
            warning: 80,
            critical: 90);
        var criticalProfile = CreateProfile(
            "Critical policy",
            ProfileRole.Publisher,
            true,
            [new DeviceBinding(deviceId)],
            warning: 70,
            critical: 80);

        var routed = ProfileTelemetryRouter.Route(deviceId, snapshot, Registry(warningProfile, criticalProfile));

        Assert.Equal(ProfileThermalState.Warning, Assert.Single(routed[0].Devices[0].Sensors).ThermalState);
        Assert.Equal(ProfileThermalState.Critical, Assert.Single(routed[1].Devices[0].Sensors).ThermalState);
    }

    [Fact]
    public void Routed_snapshot_preserves_bounded_source_and_profile_metadata_for_later_presence_stage()
    {
        const string deviceId = "device-alpha";
        var freshness = new FreshnessPolicy(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(31));
        var profile = CreateProfile(
            "Metadata profile",
            ProfileRole.Publisher | ProfileRole.TrainingMonitor,
            true,
            [new DeviceBinding(deviceId)],
            freshness: freshness);
        var snapshot = new HardwareSnapshot(CapturedAt, [], "Degraded", "InvalidOperationException: DO_NOT_LEAK_THIS_PATH");

        var routed = Assert.Single(ProfileTelemetryRouter.Route(deviceId, snapshot, Registry(profile)));

        Assert.Equal(profile.Id, routed.ProfileId);
        Assert.Equal(profile.DisplayName, routed.DisplayName);
        Assert.Equal(profile.Roles, routed.Roles);
        Assert.Equal(deviceId, routed.SourceDeviceId);
        Assert.Equal(CapturedAt, routed.CapturedAt);
        Assert.Equal("Degraded", routed.EngineStatus);
        Assert.Same(freshness, routed.Freshness);
    }

    [Fact]
    public void Route_rejects_blank_source_device_identity()
    {
        Assert.Throws<ArgumentException>(() => ProfileTelemetryRouter.Route(
            "   ",
            CreateSnapshot(),
            ProfileRegistryDocument.Empty));
    }

    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private static HardwareSnapshot CreateSnapshot(params SensorReading[] sensors) => new(
        CapturedAt,
        [new HardwareDeviceSnapshot("cpu-0", "CPU", HardwareKind.Cpu, sensors)],
        "Healthy");

    private static ProfileRegistryDocument Registry(params MonitoringProfile[] profiles) =>
        new(ProfileContract.CurrentSchemaVersion, profiles);

    private static MonitoringProfile CreateProfile(
        string name,
        ProfileRole roles,
        bool enabled,
        IReadOnlyList<DeviceBinding> bindings,
        UnavailableSensorBehavior unavailable = UnavailableSensorBehavior.ShowUnavailable,
        double warning = 80,
        double critical = 92,
        FreshnessPolicy? freshness = null) => new(
            Guid.NewGuid(),
            name,
            enabled,
            roles,
            bindings,
            ViewerScope.AllProfiles(),
            freshness ?? new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
            new ThermalPolicy(warning, critical),
            new SensorVisibilityPolicy(unavailable));
}
