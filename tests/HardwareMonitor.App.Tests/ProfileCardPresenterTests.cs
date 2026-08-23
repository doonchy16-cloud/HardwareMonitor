using TheSpark.HardwareMonitor.App.Services;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Core.Status;

namespace TheSpark.HardwareMonitor.App.Tests;

public sealed class ProfileCardPresenterTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 23, 6, 45, 0, TimeSpan.Zero);

    private static HardwareProfile Profile() => new(
        Guid.NewGuid(),
        "User-created profile",
        Guid.NewGuid(),
        new HashSet<ProfileCapability> { ProfileCapability.PublishHardwareTelemetry },
        ViewerScope.None,
        new HashSet<Guid>(),
        new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)));

    private static SensorReading Metric(
        string id,
        string name,
        double? value,
        SensorAvailability availability = SensorAvailability.Available) =>
        new(id, name, SensorKind.Temperature, value, "°C", CapturedAt, availability);

    private static ProfileTelemetrySnapshot Telemetry(HardwareProfile profile, params SensorReading[] metrics) =>
        new(
            profile.ProfileId,
            profile.DeviceId!.Value,
            CapturedAt,
            CapturedAt,
            metrics,
            "Healthy",
            null);

    [Fact]
    public void Offline_card_hides_metric_grid_completely()
    {
        var profile = Profile();
        var telemetry = Telemetry(profile, Metric("gpu.temp", "GPU", 74));
        var status = new ProfileStatus(
            ConnectivityState.Offline,
            ActivityState.Idle,
            HealthState.Healthy,
            TimeSpan.FromSeconds(25));

        var card = ProfileCardPresenter.Present(profile, telemetry, status);

        Assert.False(card.ShowMetrics);
        Assert.Empty(card.Metrics);
        Assert.Equal("OFFLINE", card.StatusText);
        Assert.Contains("25", card.LastSeenText);
    }

    [Fact]
    public void Stale_card_keeps_last_known_available_metrics_and_marks_them_historical()
    {
        var profile = Profile();
        var telemetry = Telemetry(profile, Metric("cpu.temp", "CPU", 68));
        var status = new ProfileStatus(
            ConnectivityState.Stale,
            ActivityState.Idle,
            HealthState.Healthy,
            TimeSpan.FromSeconds(8));

        var card = ProfileCardPresenter.Present(profile, telemetry, status);

        Assert.True(card.ShowMetrics);
        Assert.True(card.IsHistorical);
        Assert.Single(card.Metrics);
        Assert.Equal(68, card.Metrics[0].Value);
        Assert.Contains("STALE", card.StatusText);
    }

    [Fact]
    public void Degraded_card_omits_unavailable_metrics_but_keeps_valid_metrics()
    {
        var profile = Profile();
        var telemetry = Telemetry(
            profile,
            Metric("cpu.temp", "CPU", 68),
            Metric("gpu.temp", "GPU", null, SensorAvailability.NotExposed));
        var status = new ProfileStatus(
            ConnectivityState.Online,
            ActivityState.Idle,
            HealthState.Degraded,
            TimeSpan.FromSeconds(1));

        var card = ProfileCardPresenter.Present(profile, telemetry, status);

        Assert.True(card.ShowMetrics);
        Assert.Single(card.Metrics);
        Assert.Equal("CPU", card.Metrics[0].Name);
        Assert.Contains("DEGRADED", card.StatusText);
    }

    [Fact]
    public void Online_training_card_shows_live_valid_metrics()
    {
        var profile = Profile();
        var telemetry = Telemetry(profile, Metric("gpu.temp", "GPU", 74));
        var status = new ProfileStatus(
            ConnectivityState.Online,
            ActivityState.Training,
            HealthState.Healthy,
            TimeSpan.FromSeconds(1));

        var card = ProfileCardPresenter.Present(profile, telemetry, status);

        Assert.True(card.ShowMetrics);
        Assert.False(card.IsHistorical);
        Assert.Single(card.Metrics);
        Assert.Equal(74, card.Metrics[0].Value);
        Assert.Contains("ONLINE", card.StatusText);
        Assert.Contains("TRAINING", card.StatusText);
    }
}
