using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Core.Status;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileStatusEvaluatorTests
{
    private static readonly FreshnessPolicy Policy =
        new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));

    [Fact]
    public void Fresh_telemetry_is_online()
    {
        var now = DateTimeOffset.Parse("2026-08-23T06:00:00Z");

        var result = ProfileStatusEvaluator.Evaluate(
            now,
            now.AddSeconds(-3),
            Policy,
            ActivityState.Idle,
            HealthState.Healthy);

        Assert.Equal(ConnectivityState.Online, result.Connectivity);
        Assert.Equal(ActivityState.Idle, result.Activity);
        Assert.Equal(HealthState.Healthy, result.Health);
    }

    [Fact]
    public void Telemetry_older_than_stale_threshold_is_stale()
    {
        var now = DateTimeOffset.Parse("2026-08-23T06:00:00Z");

        var result = ProfileStatusEvaluator.Evaluate(
            now,
            now.AddSeconds(-11),
            Policy,
            ActivityState.Training,
            HealthState.Healthy);

        Assert.Equal(ConnectivityState.Stale, result.Connectivity);
        Assert.Equal(ActivityState.Training, result.Activity);
    }

    [Fact]
    public void Telemetry_older_than_offline_threshold_is_offline()
    {
        var now = DateTimeOffset.Parse("2026-08-23T06:00:00Z");

        var result = ProfileStatusEvaluator.Evaluate(
            now,
            now.AddSeconds(-61),
            Policy,
            ActivityState.Unknown,
            HealthState.Healthy);

        Assert.Equal(ConnectivityState.Offline, result.Connectivity);
    }

    [Fact]
    public void Missing_telemetry_is_offline()
    {
        var result = ProfileStatusEvaluator.Evaluate(
            DateTimeOffset.UtcNow,
            null,
            Policy,
            ActivityState.Unknown,
            HealthState.Healthy);

        Assert.Equal(ConnectivityState.Offline, result.Connectivity);
    }

    [Fact]
    public void Health_degradation_is_preserved_independently_of_connectivity()
    {
        var now = DateTimeOffset.Parse("2026-08-23T06:00:00Z");

        var result = ProfileStatusEvaluator.Evaluate(
            now,
            now.AddSeconds(-1),
            Policy,
            ActivityState.Training,
            HealthState.Degraded);

        Assert.Equal(ConnectivityState.Online, result.Connectivity);
        Assert.Equal(ActivityState.Training, result.Activity);
        Assert.Equal(HealthState.Degraded, result.Health);
    }

    [Fact]
    public void Fresh_telemetry_after_offline_period_returns_online()
    {
        var now = DateTimeOffset.Parse("2026-08-23T06:00:00Z");

        var recovered = ProfileStatusEvaluator.Evaluate(
            now,
            now,
            Policy,
            ActivityState.Training,
            HealthState.Healthy);

        Assert.Equal(ConnectivityState.Online, recovered.Connectivity);
    }
}
