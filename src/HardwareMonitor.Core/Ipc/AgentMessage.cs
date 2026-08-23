using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Core.Ipc;

public static class AgentProtocol
{
    public const string Version = "1.0";
    public const string GetStatus = "get-status";
    public const string Status = "status";
    public const string Error = "error";
    public const int MaxMessageBytes = 64 * 1024;
}

public sealed record AgentStatusPayload(
    string HealthState,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSuccessfulReadAt,
    int ConsecutiveFailures,
    string? ErrorMessage,
    HardwareSnapshot? LatestSnapshot);

public sealed record AgentMessage(
    string ProtocolVersion,
    string Type,
    string RequestId,
    AgentStatusPayload? Status = null,
    string? Error = null);
