namespace TheSpark.HardwareMonitor.Platform.Windows;

public sealed record SystemInventorySnapshot(
    string ComputerName,
    string OperatingSystem,
    string Manufacturer,
    string Model,
    string Bios,
    string Motherboard,
    string Cpu,
    int PhysicalCores,
    int LogicalProcessors,
    ulong TotalMemoryBytes,
    IReadOnlyList<string> Gpus,
    IReadOnlyList<StorageDeviceInfo> StorageDevices,
    DateTimeOffset CapturedAt);

public sealed record StorageDeviceInfo(string Model, string InterfaceType, ulong SizeBytes);
