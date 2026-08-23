using System.Runtime.InteropServices;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Core.Profiles.Presence;
using TheSpark.HardwareMonitor.Core.Profiles.Telemetry;
using TheSpark.HardwareMonitor.Platform.Windows;
using TheSpark.HardwareMonitor.Sensors;

Console.WriteLine("HARDWARE_SMOKE_V1");
Console.WriteLine($"ARCH={RuntimeInformation.ProcessArchitecture}");

var inventoryProvider = new SystemInventoryProvider();
var inventory = await inventoryProvider.GetSnapshotAsync();
var inventoryHealthy = !string.IsNullOrWhiteSpace(inventory.OperatingSystem)
    && !string.IsNullOrWhiteSpace(inventory.Cpu)
    && inventory.TotalMemoryBytes > 0;

Console.WriteLine($"INVENTORY_HEALTHY={inventoryHealthy}");
Console.WriteLine($"GPU_COUNT={inventory.Gpus.Count}");
Console.WriteLine($"STORAGE_COUNT={inventory.StorageDevices.Count}");

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
await using var provider = new LibreHardwareMonitorProvider();
var snapshot = await provider.ReadAsync(timeout.Token);
var sensorCount = snapshot.Devices.Sum(device => device.Sensors.Count);
var temperatureSensors = snapshot.Devices
    .SelectMany(device => device.Sensors)
    .Count(sensor => sensor.Kind == SensorKind.Temperature && sensor.Availability == SensorAvailability.Available);
var cpuDevices = snapshot.Devices.Count(device => device.Kind == HardwareKind.Cpu);
var gpuDevices = snapshot.Devices.Count(device => device.Kind == HardwareKind.Gpu);

Console.WriteLine($"ENGINE_STATUS={snapshot.EngineStatus}");
Console.WriteLine($"DEVICE_COUNT={snapshot.Devices.Count}");
Console.WriteLine($"CPU_DEVICE_COUNT={cpuDevices}");
Console.WriteLine($"GPU_DEVICE_COUNT={gpuDevices}");
Console.WriteLine($"SENSOR_COUNT={sensorCount}");
Console.WriteLine($"TEMPERATURE_SENSOR_COUNT={temperatureSensors}");

if (!inventoryHealthy)
{
    Console.Error.WriteLine("HARDWARE_SMOKE_FAIL: Windows inventory did not return required baseline hardware data.");
    return 10;
}

if (!string.Equals(snapshot.EngineStatus, "Healthy", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"HARDWARE_SMOKE_FAIL: sensor engine status is {snapshot.EngineStatus}.");
    return 20;
}

if (snapshot.Devices.Count == 0 || sensorCount == 0)
{
    Console.Error.WriteLine("HARDWARE_SMOKE_FAIL: the sensor provider returned no devices or sensors.");
    return 30;
}

if (temperatureSensors == 0)
{
    Console.Error.WriteLine("HARDWARE_SMOKE_FAIL: no live temperature sensor is available on this runner.");
    return 40;
}

const string gateDeviceId = "hardware-monitor-real-gate";
var gateProfile = new MonitoringProfile(
    Guid.NewGuid(),
    "Real Hardware Gate",
    true,
    ProfileRole.Publisher | ProfileRole.TrainingMonitor,
    [new DeviceBinding(gateDeviceId)],
    ViewerScope.AllProfiles(),
    new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
    new ThermalPolicy(80, 92),
    new SensorVisibilityPolicy(UnavailableSensorBehavior.ShowUnavailable));
var gateRegistry = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [gateProfile]);
var routedProfiles = ProfileTelemetryRouter.Route(gateDeviceId, snapshot, gateRegistry);

if (routedProfiles.Count != 1)
{
    Console.Error.WriteLine($"PROFILE_ROUTING_FAIL: expected one routed profile, got {routedProfiles.Count}.");
    return 50;
}

var routed = routedProfiles[0];
var routedSensorCount = routed.Devices.Sum(device => device.Sensors.Count);
var routedTemperatureSensors = routed.Devices
    .SelectMany(device => device.Sensors)
    .Count(sensor => sensor.Kind == SensorKind.Temperature && sensor.Availability == SensorAvailability.Available);

Console.WriteLine($"ROUTED_PROFILE_COUNT={routedProfiles.Count}");
Console.WriteLine($"ROUTED_SENSOR_COUNT={routedSensorCount}");
Console.WriteLine($"ROUTED_TEMPERATURE_SENSOR_COUNT={routedTemperatureSensors}");

if (routed.ProfileId != gateProfile.Id
    || !string.Equals(routed.SourceDeviceId, gateDeviceId, StringComparison.Ordinal)
    || routed.CapturedAt != snapshot.CapturedAt
    || routedSensorCount != sensorCount
    || routedTemperatureSensors != temperatureSensors)
{
    Console.Error.WriteLine("PROFILE_ROUTING_FAIL: routed telemetry did not preserve the real snapshot/profile contract.");
    return 60;
}

Console.WriteLine("PROFILE_ROUTING_PASS");

var freshPresence = ProfilePresenceEvaluator.Evaluate(routed, routed.CapturedAt);
var stalePresence = ProfilePresenceEvaluator.Evaluate(
    routed,
    routed.CapturedAt + gateProfile.Freshness.StaleAfter + TimeSpan.FromTicks(1));
var offlinePresence = ProfilePresenceEvaluator.Evaluate(
    routed,
    routed.CapturedAt + gateProfile.Freshness.OfflineAfter + TimeSpan.FromTicks(1));

Console.WriteLine($"PROFILE_PRESENCE_FRESH={freshPresence.Connectivity}/{freshPresence.TelemetryPresentation}");
Console.WriteLine($"PROFILE_PRESENCE_STALE={stalePresence.Connectivity}/{stalePresence.TelemetryPresentation}");
Console.WriteLine($"PROFILE_PRESENCE_OFFLINE={offlinePresence.Connectivity}/{offlinePresence.TelemetryPresentation}");

if (freshPresence.Connectivity != ProfileConnectivityState.Online
    || freshPresence.TelemetryPresentation != ProfileTelemetryPresentation.Live
    || stalePresence.Connectivity != ProfileConnectivityState.Stale
    || stalePresence.TelemetryPresentation != ProfileTelemetryPresentation.Historical
    || offlinePresence.Connectivity != ProfileConnectivityState.Offline
    || offlinePresence.TelemetryPresentation != ProfileTelemetryPresentation.Hidden
    || freshPresence.ProfileId != routed.ProfileId
    || !string.Equals(freshPresence.SourceDeviceId, routed.SourceDeviceId, StringComparison.Ordinal))
{
    Console.Error.WriteLine("PROFILE_PRESENCE_FAIL: real routed telemetry did not produce the required status-first freshness states.");
    return 70;
}

Console.WriteLine("PROFILE_PRESENCE_PASS");
Console.WriteLine("HARDWARE_SMOKE_PASS");
return 0;
