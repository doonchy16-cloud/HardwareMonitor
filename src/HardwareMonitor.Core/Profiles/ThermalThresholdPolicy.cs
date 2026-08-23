namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed record ThermalThresholdPolicy
{
    public ThermalThresholdPolicy(double warmCelsius, double hotCelsius, double criticalCelsius)
    {
        if (double.IsNaN(warmCelsius) || double.IsInfinity(warmCelsius))
        {
            throw new ArgumentOutOfRangeException(nameof(warmCelsius));
        }

        if (hotCelsius <= warmCelsius || criticalCelsius <= hotCelsius)
        {
            throw new ArgumentOutOfRangeException(nameof(criticalCelsius), "Thermal thresholds must increase from warm to hot to critical.");
        }

        WarmCelsius = warmCelsius;
        HotCelsius = hotCelsius;
        CriticalCelsius = criticalCelsius;
    }

    public double WarmCelsius { get; }
    public double HotCelsius { get; }
    public double CriticalCelsius { get; }
}
