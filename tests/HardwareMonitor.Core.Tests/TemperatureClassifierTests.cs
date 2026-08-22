using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class TemperatureClassifierTests
{
    [Theory]
    [InlineData(40, ThermalState.Normal)]
    [InlineData(75, ThermalState.Warm)]
    [InlineData(87, ThermalState.Hot)]
    [InlineData(96, ThermalState.Critical)]
    public void Cpu_thresholds_are_classified(double celsius, ThermalState expected)
    {
        Assert.Equal(expected, TemperatureClassifier.Classify(HardwareKind.Cpu, celsius));
    }

    [Theory]
    [InlineData(50, ThermalState.Normal)]
    [InlineData(61, ThermalState.Warm)]
    [InlineData(71, ThermalState.Hot)]
    [InlineData(81, ThermalState.Critical)]
    public void Storage_uses_lower_thresholds(double celsius, ThermalState expected)
    {
        Assert.Equal(expected, TemperatureClassifier.Classify(HardwareKind.Storage, celsius));
    }

    [Fact]
    public void Missing_temperature_is_unknown()
    {
        Assert.Equal(ThermalState.Unknown, TemperatureClassifier.Classify(HardwareKind.Cpu, null));
    }
}
