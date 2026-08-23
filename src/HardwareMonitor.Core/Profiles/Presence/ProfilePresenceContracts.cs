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
        DateTimeOffset evaluatedAt) => throw new NotImplementedException();
}
