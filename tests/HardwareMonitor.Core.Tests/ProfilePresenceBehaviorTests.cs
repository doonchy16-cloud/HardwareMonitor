using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Core.Profiles.Presence;
using TheSpark.HardwareMonitor.Core.Profiles.Telemetry;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfilePresenceBehaviorTests
{
    [Fact]
    public void Fresh_telemetry_is_online_and_live_at_the_stale_boundary()
    {
        var telemetry = CreateTelemetry(staleSeconds: 5, offlineSeconds: 20);

        var presence = ProfilePresenceEvaluator.Evaluate(
            telemetry,
            CapturedAt.AddSeconds(5));

        Assert.Equal(ProfileConnectivityState.Online, presence.Connectivity);
        Assert.Equal(ProfileTelemetryPresentation.Live, presence.TelemetryPresentation);
        Assert.Equal(TimeSpan.FromSeconds(5), presence.TelemetryAge);
    }

    [Fact]
    public void Telemetry_becomes_stale_only_after_the_stale_threshold_and_stays_historical_at_offline_boundary()
    {
        var telemetry = CreateTelemetry(staleSeconds: 5, offlineSeconds: 20);

        var justStale = ProfilePresenceEvaluator.Evaluate(
            telemetry,
            CapturedAt.AddSeconds(5).AddTicks(1));
        var offlineBoundary = ProfilePresenceEvaluator.Evaluate(
            telemetry,
            CapturedAt.AddSeconds(20));

        Assert.Equal(ProfileConnectivityState.Stale, justStale.Connectivity);
        Assert.Equal(ProfileTelemetryPresentation.Historical, justStale.TelemetryPresentation);
        Assert.Equal(ProfileConnectivityState.Stale, offlineBoundary.Connectivity);
        Assert.Equal(ProfileTelemetryPresentation.Historical, offlineBoundary.TelemetryPresentation);
    }

    [Fact]
    public void Telemetry_becomes_offline_only_after_the_offline_threshold_and_hides_metric_rendering()
    {
        var telemetry = CreateTelemetry(staleSeconds: 5, offlineSeconds: 20);

        var presence = ProfilePresenceEvaluator.Evaluate(
            telemetry,
            CapturedAt.AddSeconds(20).AddTicks(1));

        Assert.Equal(ProfileConnectivityState.Offline, presence.Connectivity);
        Assert.Equal(ProfileTelemetryPresentation.Hidden, presence.TelemetryPresentation);
    }

    [Fact]
    public void Freshness_is_evaluated_per_profile_not_from_a_global_default()
    {
        var fast = CreateTelemetry(staleSeconds: 5, offlineSeconds: 20);
        var relaxed = CreateTelemetry(staleSeconds: 10, offlineSeconds: 30);
        var evaluatedAt = CapturedAt.AddSeconds(7);

        var fastPresence = ProfilePresenceEvaluator.Evaluate(fast, evaluatedAt);
        var relaxedPresence = ProfilePresenceEvaluator.Evaluate(relaxed, evaluatedAt);

        Assert.Equal(ProfileConnectivityState.Stale, fastPresence.Connectivity);
        Assert.Equal(ProfileConnectivityState.Online, relaxedPresence.Connectivity);
    }

    [Fact]
    public void Presence_is_independent_of_sensor_availability_and_preserves_bounded_source_status()
    {
        var telemetry = CreateTelemetry(
            staleSeconds: 5,
            offlineSeconds: 20,
            engineStatus: "Degraded",
            devices: []);

        var presence = ProfilePresenceEvaluator.Evaluate(telemetry, CapturedAt);

        Assert.Equal(ProfileConnectivityState.Online, presence.Connectivity);
        Assert.Equal(ProfileTelemetryPresentation.Live, presence.TelemetryPresentation);
        Assert.Equal("Degraded", presence.SourceEngineStatus);
    }

    [Fact]
    public void Presence_preserves_profile_source_and_time_metadata()
    {
        var telemetry = CreateTelemetry(staleSeconds: 5, offlineSeconds: 20);
        var evaluatedAt = CapturedAt.AddSeconds(3);

        var presence = ProfilePresenceEvaluator.Evaluate(telemetry, evaluatedAt);

        Assert.Equal(telemetry.ProfileId, presence.ProfileId);
        Assert.Equal(telemetry.SourceDeviceId, presence.SourceDeviceId);
        Assert.Equal(CapturedAt, presence.LastTelemetryAt);
        Assert.Equal(evaluatedAt, presence.EvaluatedAt);
        Assert.Equal(TimeSpan.FromSeconds(3), presence.TelemetryAge);
    }

    [Fact]
    public void Evaluation_rejects_a_time_before_the_telemetry_frame_instead_of_silently_treating_it_as_fresh()
    {
        var telemetry = CreateTelemetry(staleSeconds: 5, offlineSeconds: 20);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProfilePresenceEvaluator.Evaluate(telemetry, CapturedAt.AddTicks(-1)));
    }

    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private static ProfileTelemetrySnapshot CreateTelemetry(
        double staleSeconds,
        double offlineSeconds,
        string engineStatus = "Healthy",
        IReadOnlyList<ProfileHardwareDeviceSnapshot>? devices = null) => new(
            Guid.NewGuid(),
            "Test Profile",
            ProfileRole.Publisher,
            "device-alpha",
            CapturedAt,
            engineStatus,
            new FreshnessPolicy(TimeSpan.FromSeconds(staleSeconds), TimeSpan.FromSeconds(offlineSeconds)),
            devices ?? []);
}
