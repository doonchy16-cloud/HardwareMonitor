using TheSpark.HardwareMonitor.Core.Alerts;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Core.Status;
using Xunit;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class AlertEngineTests
{
    private static readonly ThermalThresholdPolicy Thresholds = new(70, 80, 90);

    [Fact]
    public void ThermalCrossingsEscalateWarmHotCriticalWithoutDuplicateSpam()
    {
        var engine = new AlertEngine();
        var profileId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-23T08:00:00Z");
        var online = Status(ConnectivityState.Online, HealthState.Healthy);

        var warm = Assert.Single(engine.Evaluate(profileId, online, [Temperature(75, now)], Thresholds, now));
        Assert.Equal(AlertKind.ThermalWarm, warm.Kind);
        Assert.Empty(engine.Evaluate(profileId, online, [Temperature(76, now.AddSeconds(1))], Thresholds, now.AddSeconds(1)));

        var hot = Assert.Single(engine.Evaluate(profileId, online, [Temperature(85, now.AddSeconds(2))], Thresholds, now.AddSeconds(2)));
        Assert.Equal(AlertKind.ThermalHot, hot.Kind);

        var critical = Assert.Single(engine.Evaluate(profileId, online, [Temperature(95, now.AddSeconds(3))], Thresholds, now.AddSeconds(3)));
        Assert.Equal(AlertKind.ThermalCritical, critical.Kind);
        Assert.Equal(95, critical.TemperatureCelsius);
    }

    [Fact]
    public void StaleAndOfflineAreDistinctAndDeduplicated()
    {
        var engine = new AlertEngine();
        var profileId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-23T08:00:00Z");

        var stale = Assert.Single(engine.Evaluate(
            profileId,
            Status(ConnectivityState.Stale, HealthState.Healthy),
            [],
            Thresholds,
            now));
        Assert.Equal(AlertKind.TelemetryStale, stale.Kind);
        Assert.Empty(engine.Evaluate(
            profileId,
            Status(ConnectivityState.Stale, HealthState.Healthy),
            [],
            Thresholds,
            now.AddSeconds(1)));

        var offline = Assert.Single(engine.Evaluate(
            profileId,
            Status(ConnectivityState.Offline, HealthState.Healthy),
            [],
            Thresholds,
            now.AddSeconds(2)));
        Assert.Equal(AlertKind.DeviceOffline, offline.Kind);
    }

    [Fact]
    public void DegradedHealthAlertsOnceWhileValidMetricsRemainUsable()
    {
        var engine = new AlertEngine();
        var profileId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-23T08:00:00Z");
        var degraded = Status(ConnectivityState.Online, HealthState.Degraded);

        var first = Assert.Single(engine.Evaluate(profileId, degraded, [Temperature(60, now)], Thresholds, now));
        Assert.Equal(AlertKind.SensorDegraded, first.Kind);
        Assert.Empty(engine.Evaluate(profileId, degraded, [Temperature(61, now.AddSeconds(1))], Thresholds, now.AddSeconds(1)));
    }

    [Fact]
    public void ReturningToHealthyNormalStateEmitsOneRecoveryEvent()
    {
        var engine = new AlertEngine();
        var profileId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-23T08:00:00Z");

        Assert.Single(engine.Evaluate(
            profileId,
            Status(ConnectivityState.Offline, HealthState.Healthy),
            [],
            Thresholds,
            now));

        var recovery = Assert.Single(engine.Evaluate(
            profileId,
            Status(ConnectivityState.Online, HealthState.Healthy),
            [Temperature(55, now.AddSeconds(1))],
            Thresholds,
            now.AddSeconds(1)));

        Assert.Equal(AlertKind.Recovered, recovery.Kind);
        Assert.Equal(AlertKind.DeviceOffline, recovery.RecoveredKind);
        Assert.Empty(engine.Evaluate(
            profileId,
            Status(ConnectivityState.Online, HealthState.Healthy),
            [Temperature(55, now.AddSeconds(2))],
            Thresholds,
            now.AddSeconds(2)));
    }

    [Fact]
    public void HighestAvailableTemperatureDrivesThermalSeverityAndUnavailableRowsAreIgnored()
    {
        var engine = new AlertEngine();
        var profileId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-23T08:00:00Z");
        var readings = new[]
        {
            Temperature(65, now, "cpu"),
            Temperature(92, now, "gpu"),
            new SensorReading("missing", "Missing", SensorKind.Temperature, 120, "°C", now, SensorAvailability.NotExposed)
        };

        var alert = Assert.Single(engine.Evaluate(
            profileId,
            Status(ConnectivityState.Online, HealthState.Healthy),
            readings,
            Thresholds,
            now));

        Assert.Equal(AlertKind.ThermalCritical, alert.Kind);
        Assert.Equal("gpu", alert.SensorId);
        Assert.Equal(92, alert.TemperatureCelsius);
    }

    private static ProfileStatus Status(ConnectivityState connectivity, HealthState health) =>
        new(connectivity, ActivityState.Training, health, TimeSpan.Zero);

    private static SensorReading Temperature(double value, DateTimeOffset capturedAt, string id = "gpu") =>
        new(id, id.ToUpperInvariant(), SensorKind.Temperature, value, "°C", capturedAt, SensorAvailability.Available);
}
