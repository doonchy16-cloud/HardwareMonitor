using System.Runtime.InteropServices;
using TheSpark.HardwareMonitor.Core.Models;
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

Console.WriteLine("HARDWARE_SMOKE_PASS");
return 0;
