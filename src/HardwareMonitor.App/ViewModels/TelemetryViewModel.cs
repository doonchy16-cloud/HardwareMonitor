using System.Collections.ObjectModel;
using TheSpark.HardwareMonitor.Core;
using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.App.ViewModels;

public sealed class TelemetryViewModel : ObservableObject
{
    private readonly Dictionary<string, SensorRowViewModel> _sensorRows = new(StringComparer.Ordinal);
    private string _engineStatus = "Initializing";
    private string _cpuName = "Detecting CPU…";
    private string _gpuName = "Detecting GPU…";
    private string _cpuTemperature = "—";
    private string _gpuTemperature = "—";
    private string _cpuLoad = "—";
    private string _gpuLoad = "—";
    private string _memoryLoad = "—";
    private string _storageTemperature = "—";
    private string _thermalStatus = "Scanning";
    private string _freshness = "Waiting for sensors";
    private int _deviceCount;
    private int _sensorCount;
    private DateTimeOffset? _lastRefresh;
    private double _cpuLoadValue;
    private double _gpuLoadValue;
    private double _memoryLoadValue;
    private string _temperatureUnit = "Celsius";

    public TelemetryViewModel()
    {
        CpuHistory = new RollingSeries(300);
        GpuHistory = new RollingSeries(300);
    }

    public string EngineStatus { get => _engineStatus; private set => SetProperty(ref _engineStatus, value); }
    public string CpuName { get => _cpuName; private set => SetProperty(ref _cpuName, value); }
    public string GpuName { get => _gpuName; private set => SetProperty(ref _gpuName, value); }
    public string CpuTemperature { get => _cpuTemperature; private set => SetProperty(ref _cpuTemperature, value); }
    public string GpuTemperature { get => _gpuTemperature; private set => SetProperty(ref _gpuTemperature, value); }
    public string CpuLoad { get => _cpuLoad; private set => SetProperty(ref _cpuLoad, value); }
    public string GpuLoad { get => _gpuLoad; private set => SetProperty(ref _gpuLoad, value); }
    public string MemoryLoad { get => _memoryLoad; private set => SetProperty(ref _memoryLoad, value); }
    public string StorageTemperature { get => _storageTemperature; private set => SetProperty(ref _storageTemperature, value); }
    public string ThermalStatus { get => _thermalStatus; private set => SetProperty(ref _thermalStatus, value); }
    public string Freshness { get => _freshness; private set => SetProperty(ref _freshness, value); }
    public int DeviceCount { get => _deviceCount; private set => SetProperty(ref _deviceCount, value); }
    public int SensorCount { get => _sensorCount; private set => SetProperty(ref _sensorCount, value); }
    public DateTimeOffset? LastRefresh { get => _lastRefresh; private set => SetProperty(ref _lastRefresh, value); }
    public double CpuLoadValue { get => _cpuLoadValue; private set => SetProperty(ref _cpuLoadValue, value); }
    public double GpuLoadValue { get => _gpuLoadValue; private set => SetProperty(ref _gpuLoadValue, value); }
    public double MemoryLoadValue { get => _memoryLoadValue; private set => SetProperty(ref _memoryLoadValue, value); }
    public string TemperatureUnit { get => _temperatureUnit; set => SetProperty(ref _temperatureUnit, value); }

    public RollingSeries CpuHistory { get; }
    public RollingSeries GpuHistory { get; }
    public ObservableCollection<SensorRowViewModel> CpuSensors { get; } = [];
    public ObservableCollection<SensorRowViewModel> GpuSensors { get; } = [];
    public ObservableCollection<SensorRowViewModel> MemorySensors { get; } = [];
    public ObservableCollection<SensorRowViewModel> StorageSensors { get; } = [];
    public ObservableCollection<SensorRowViewModel> MotherboardSensors { get; } = [];
    public ObservableCollection<SensorRowViewModel> FanSensors { get; } = [];

    public void Apply(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var now = DateTimeOffset.UtcNow;
        EngineStatus = snapshot.EngineStatus;
        LastRefresh = snapshot.CapturedAt;
        DeviceCount = snapshot.Devices.Count;
        SensorCount = snapshot.Devices.Sum(device => device.Sensors.Count);
        Freshness = now - snapshot.CapturedAt > TimeSpan.FromSeconds(3)
            ? $"Stale · {(now - snapshot.CapturedAt).TotalSeconds:0}s"
            : "LIVE";

        foreach (var collection in SensorCollections())
        {
            collection.Clear();
        }

        var maxThermal = ThermalState.Unknown;
        foreach (var device in snapshot.Devices)
        {
            var target = CollectionFor(device.Kind);
            foreach (var sensor in device.Sensors)
            {
                if (!_sensorRows.TryGetValue(sensor.Id, out var row))
                {
                    row = new SensorRowViewModel(sensor.Id, device.Name, sensor.Name, sensor.Kind, sensor.Unit);
                    _sensorRows.Add(sensor.Id, row);
                }

                row.Update(sensor, now, TemperatureUnit);
                target?.Add(row);

                if (sensor.Kind == SensorKind.Temperature)
                {
                    var state = TemperatureClassifier.Classify(device.Kind, sensor.Value);
                    if (state > maxThermal)
                    {
                        maxThermal = state;
                    }
                }
            }
        }

        ThermalStatus = maxThermal == ThermalState.Unknown ? "No temperature data" : maxThermal.ToString();
        UpdateHeroMetrics(snapshot);
    }

    private void UpdateHeroMetrics(HardwareSnapshot snapshot)
    {
        var cpu = snapshot.Devices.FirstOrDefault(device => device.Kind == HardwareKind.Cpu);
        if (cpu is not null)
        {
            CpuName = cpu.Name;
            var temp = Best(cpu, SensorKind.Temperature, "Package");
            CpuTemperature = Format(temp, TemperatureUnit);
            if (temp?.Value is double celsius)
            {
                CpuHistory.Add(celsius);
                OnPropertyChanged(nameof(CpuHistory));
            }

            var load = Best(cpu, SensorKind.Load, "Total");
            CpuLoadValue = ClampPercent(load?.Value);
            CpuLoad = load?.Value is double cpuValue ? $"{cpuValue:0.#}%" : "—";
        }

        var gpu = snapshot.Devices.FirstOrDefault(device => device.Kind == HardwareKind.Gpu);
        if (gpu is not null)
        {
            GpuName = gpu.Name;
            var temp = Best(gpu, SensorKind.Temperature, "Core") ?? Best(gpu, SensorKind.Temperature, null);
            GpuTemperature = Format(temp, TemperatureUnit);
            if (temp?.Value is double celsius)
            {
                GpuHistory.Add(celsius);
                OnPropertyChanged(nameof(GpuHistory));
            }

            var load = Best(gpu, SensorKind.Load, "Core") ?? Best(gpu, SensorKind.Load, null);
            GpuLoadValue = ClampPercent(load?.Value);
            GpuLoad = load?.Value is double gpuValue ? $"{gpuValue:0.#}%" : "—";
        }

        var memory = snapshot.Devices.FirstOrDefault(device => device.Kind == HardwareKind.Memory);
        var memoryLoad = memory is null ? null : Best(memory, SensorKind.Load, "Memory") ?? Best(memory, SensorKind.Load, null);
        MemoryLoadValue = ClampPercent(memoryLoad?.Value);
        MemoryLoad = memoryLoad?.Value is double memoryValue ? $"{memoryValue:0.#}%" : "—";

        var storage = snapshot.Devices.FirstOrDefault(device => device.Kind == HardwareKind.Storage);
        StorageTemperature = storage is null ? "—" : Format(Best(storage, SensorKind.Temperature, null), TemperatureUnit);
    }

    private static SensorReading? Best(HardwareDeviceSnapshot device, SensorKind kind, string? preferredName) =>
        device.Sensors.FirstOrDefault(sensor => sensor.Kind == kind && preferredName is not null && sensor.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
        ?? device.Sensors.FirstOrDefault(sensor => sensor.Kind == kind);

    private static string Format(SensorReading? reading, string temperatureUnit)
    {
        if (reading?.Value is not double value)
        {
            return "Not exposed";
        }

        if (reading.Kind == SensorKind.Temperature && temperatureUnit.Equals("Fahrenheit", StringComparison.OrdinalIgnoreCase))
        {
            return $"{value * 9d / 5d + 32d:0.#} °F";
        }

        return $"{value:0.#} {reading.Unit}".Trim();
    }

    private static double ClampPercent(double? value) => value.HasValue ? Math.Clamp(value.Value, 0, 100) : 0;

    private ObservableCollection<SensorRowViewModel>? CollectionFor(HardwareKind kind) => kind switch
    {
        HardwareKind.Cpu => CpuSensors,
        HardwareKind.Gpu => GpuSensors,
        HardwareKind.Memory => MemorySensors,
        HardwareKind.Storage => StorageSensors,
        HardwareKind.Motherboard => MotherboardSensors,
        HardwareKind.Fan => FanSensors,
        _ => null
    };

    private IEnumerable<ObservableCollection<SensorRowViewModel>> SensorCollections()
    {
        yield return CpuSensors;
        yield return GpuSensors;
        yield return MemorySensors;
        yield return StorageSensors;
        yield return MotherboardSensors;
        yield return FanSensors;
    }
}
