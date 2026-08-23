namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed record ThermalThresholdPolicy
{
    public static ThermalThresholdPolicy Default { get; } = new(70, 82, 92);

    public ThermalThresholdPolicy(double warmCelsius, double hotCelsius, double criticalCelsius)
    {
        if (!double.IsFinite(warmCelsius) || !double.IsFinite(hotCelsius) || !double.IsFinite(criticalCelsius))
        {
            throw new ArgumentOutOfRangeException(nameof(warmCelsius), "Thermal thresholds must be finite numbers.");
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
