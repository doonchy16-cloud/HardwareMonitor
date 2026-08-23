namespace TheSpark.HardwareMonitor.Core.Status;

public sealed record ProfileStatus(
    ConnectivityState Connectivity,
    ActivityState Activity,
    HealthState Health,
    TimeSpan? TelemetryAge);
