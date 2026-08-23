namespace TheSpark.HardwareMonitor.Agent;

public sealed record AgentHealthSnapshot(
    AgentHealthState State,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSuccessfulReadAt,
    int ConsecutiveFailures,
    string? ErrorMessage = null);
