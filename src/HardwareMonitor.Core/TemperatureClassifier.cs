using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Core;

public static class TemperatureClassifier
{
    public static ThermalState Classify(HardwareKind hardwareKind, double? celsius)
    {
        if (!celsius.HasValue || double.IsNaN(celsius.Value) || double.IsInfinity(celsius.Value))
        {
            return ThermalState.Unknown;
        }

        var (warm, hot, critical) = hardwareKind switch
        {
            HardwareKind.Cpu => (70d, 85d, 95d),
            HardwareKind.Gpu => (70d, 82d, 92d),
            HardwareKind.Storage => (55d, 65d, 75d),
            HardwareKind.Memory => (60d, 75d, 90d),
            HardwareKind.Motherboard => (60d, 75d, 90d),
            _ => (65d, 80d, 95d)
        };

        return celsius.Value >= critical ? ThermalState.Critical
            : celsius.Value >= hot ? ThermalState.Hot
            : celsius.Value >= warm ? ThermalState.Warm
            : ThermalState.Normal;
    }
}
