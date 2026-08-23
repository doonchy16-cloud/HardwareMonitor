using TheSpark.HardwareMonitor.Core.Profiles.Telemetry;

namespace TheSpark.HardwareMonitor.Core.Profiles.Presence;

public enum ProfileConnectivityState
{
    Online,
    Stale,
    Offline,
}

public enum ProfileTelemetryPresentation
{
    Live,
    Historical,
    Hidden,
}

public sealed record ProfilePresenceSnapshot(
    Guid ProfileId,
    string SourceDeviceId,
    DateTimeOffset LastTelemetryAt,
    DateTimeOffset EvaluatedAt,
    TimeSpan TelemetryAge,
    ProfileConnectivityState Connectivity,
    ProfileTelemetryPresentation TelemetryPresentation,
    string SourceEngineStatus);

public static class ProfilePresenceEvaluator
{
    public static ProfilePresenceSnapshot Evaluate(
        ProfileTelemetrySnapshot telemetry,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        if (evaluatedAt < telemetry.CapturedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evaluatedAt),
                "Presence evaluation time cannot precede the telemetry capture time.");
        }

        var age = evaluatedAt - telemetry.CapturedAt;
        var (connectivity, presentation) = age > telemetry.Freshness.OfflineAfter
            ? (ProfileConnectivityState.Offline, ProfileTelemetryPresentation.Hidden)
            : age > telemetry.Freshness.StaleAfter
                ? (ProfileConnectivityState.Stale, ProfileTelemetryPresentation.Historical)
                : (ProfileConnectivityState.Online, ProfileTelemetryPresentation.Live);

        return new ProfilePresenceSnapshot(
            telemetry.ProfileId,
            telemetry.SourceDeviceId,
            telemetry.CapturedAt,
            evaluatedAt,
            age,
            connectivity,
            presentation,
            telemetry.EngineStatus);
    }
}
