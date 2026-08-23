namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class RollingSeriesTests
{
    [Fact]
    public void Series_discards_oldest_values_when_capacity_is_exceeded()
    {
        var series = new RollingSeries(3);

        series.Add(10);
        series.Add(20);
        series.Add(30);
        series.Add(40);

        Assert.Equal([20d, 30d, 40d], series.Values);
    }

    [Fact]
    public void Series_reports_average_min_and_max()
    {
        var series = new RollingSeries(4);
        series.Add(10);
        series.Add(20);
        series.Add(30);

        Assert.Equal(20, series.Average);
        Assert.Equal(10, series.Minimum);
        Assert.Equal(30, series.Maximum);
    }

    [Fact]
    public void Invalid_capacity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RollingSeries(0));
    }
}
