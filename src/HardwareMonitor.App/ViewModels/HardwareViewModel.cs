using System.Collections.ObjectModel;
using TheSpark.HardwareMonitor.Platform.Windows;

namespace TheSpark.HardwareMonitor.App.ViewModels;

public sealed class HardwareViewModel : ObservableObject
{
    private string _computerName = "Detecting…";
    private string _operatingSystem = "Detecting…";
    private string _systemModel = "Detecting…";
    private string _bios = "Detecting…";
    private string _motherboard = "Detecting…";
    private string _cpu = "Detecting…";
    private string _coreSummary = "Detecting…";
    private string _memory = "Detecting…";

    public string ComputerName { get => _computerName; private set => SetProperty(ref _computerName, value); }
    public string OperatingSystem { get => _operatingSystem; private set => SetProperty(ref _operatingSystem, value); }
    public string SystemModel { get => _systemModel; private set => SetProperty(ref _systemModel, value); }
    public string Bios { get => _bios; private set => SetProperty(ref _bios, value); }
    public string Motherboard { get => _motherboard; private set => SetProperty(ref _motherboard, value); }
    public string Cpu { get => _cpu; private set => SetProperty(ref _cpu, value); }
    public string CoreSummary { get => _coreSummary; private set => SetProperty(ref _coreSummary, value); }
    public string Memory { get => _memory; private set => SetProperty(ref _memory, value); }
    public ObservableCollection<string> Gpus { get; } = [];
    public ObservableCollection<string> StorageDevices { get; } = [];

    public void Apply(SystemInventorySnapshot snapshot)
    {
        ComputerName = snapshot.ComputerName;
        OperatingSystem = snapshot.OperatingSystem;
        SystemModel = $"{snapshot.Manufacturer} {snapshot.Model}".Trim();
        Bios = snapshot.Bios;
        Motherboard = snapshot.Motherboard;
        Cpu = snapshot.Cpu;
        CoreSummary = $"{snapshot.PhysicalCores} cores / {snapshot.LogicalProcessors} threads";
        Memory = snapshot.TotalMemoryBytes == 0 ? "Unknown" : $"{snapshot.TotalMemoryBytes / 1024d / 1024d / 1024d:0.#} GB";

        Gpus.Clear();
        foreach (var gpu in snapshot.Gpus.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            Gpus.Add(gpu);
        }

        StorageDevices.Clear();
        foreach (var drive in snapshot.StorageDevices)
        {
            var capacity = drive.SizeBytes == 0 ? "Unknown capacity" : $"{drive.SizeBytes / 1000d / 1000d / 1000d:0.#} GB";
            StorageDevices.Add($"{drive.Model} · {drive.InterfaceType} · {capacity}");
        }
    }
}
