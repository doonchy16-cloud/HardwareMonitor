using LibreHardwareMonitor.Hardware;
using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Sensors;

public sealed class LibreHardwareMonitorProvider : ISensorProvider
{
    private readonly Computer _computer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _opened;
    private bool _disposed;

    public LibreHardwareMonitorProvider()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsNetworkEnabled = true,
            IsStorageEnabled = true,
            IsPsuEnabled = true,
            IsBatteryEnabled = true,
            IsPowerMonitorEnabled = true
        };
    }

    public async Task<HardwareSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            EnsureOpen();

            foreach (var hardware in _computer.Hardware)
            {
                UpdateHardwareTree(hardware);
            }

            var capturedAt = DateTimeOffset.UtcNow;
            var samples = new List<RawHardwareSample>();
            foreach (var hardware in _computer.Hardware)
            {
                CollectHardwareTree(hardware, samples);
            }

            return SensorSnapshotBuilder.Build(samples, capturedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HardwareSnapshot(
                DateTimeOffset.UtcNow,
                Array.Empty<HardwareDeviceSnapshot>(),
                "Degraded",
                $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_opened)
            {
                _computer.Close();
                _opened = false;
            }

            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private void EnsureOpen()
    {
        if (_opened)
        {
            return;
        }

        _computer.Open();
        _opened = true;
    }

    private static void UpdateHardwareTree(IHardware hardware)
    {
        hardware.Update();
        foreach (var child in hardware.SubHardware)
        {
            UpdateHardwareTree(child);
        }
    }

    private static void CollectHardwareTree(IHardware hardware, ICollection<RawHardwareSample> destination)
    {
        var sensors = hardware.Sensors.Select(MapSensor).ToArray();
        destination.Add(new RawHardwareSample(
            hardware.Identifier.ToString(),
            hardware.Name,
            MapHardwareKind(hardware.HardwareType),
            sensors));

        foreach (var child in hardware.SubHardware)
        {
            CollectHardwareTree(child, destination);
        }
    }

    private static RawSensorSample MapSensor(ISensor sensor) => new(
        sensor.Identifier.ToString(),
        sensor.Name,
        MapSensorKind(sensor.SensorType),
        sensor.Value,
        UnitFor(sensor.SensorType));

    private static HardwareKind MapHardwareKind(HardwareType type) => type switch
    {
        HardwareType.Cpu => HardwareKind.Cpu,
        HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia => HardwareKind.Gpu,
        HardwareType.Memory => HardwareKind.Memory,
        HardwareType.Storage => HardwareKind.Storage,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => HardwareKind.Motherboard,
        HardwareType.Cooler => HardwareKind.Fan,
        HardwareType.Network => HardwareKind.Network,
        HardwareType.Battery => HardwareKind.Battery,
        HardwareType.Psu or HardwareType.PowerMonitor => HardwareKind.PowerSupply,
        _ => HardwareKind.Unknown
    };

    private static SensorKind MapSensorKind(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Clock or SensorType.Frequency => SensorKind.Clock,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Power or SensorType.Energy => SensorKind.Power,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Current => SensorKind.Current,
        SensorType.Data or SensorType.SmallData => SensorKind.Data,
        SensorType.Throughput or SensorType.Flow => SensorKind.Throughput,
        SensorType.Control => SensorKind.Control,
        SensorType.Level or SensorType.Humidity => SensorKind.Level,
        _ => SensorKind.Unknown
    };

    private static string UnitFor(SensorType type) => type switch
    {
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Power => "W",
        SensorType.Clock => "MHz",
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Frequency => "Hz",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Control or SensorType.Level or SensorType.Humidity => "%",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        SensorType.TimeSpan => "s",
        SensorType.Timing => "ns",
        SensorType.Energy => "mWh",
        SensorType.Noise => "dBA",
        SensorType.Conductivity => "µS/cm",
        _ => string.Empty
    };
}
