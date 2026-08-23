using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Sensors.Agent;

public enum AgentLifecycleState
{
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
}

public sealed record AgentHealthSnapshot(
    AgentLifecycleState State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastSnapshotAt,
    string? LastError,
    bool SensorEngineRunning,
    bool ProfileRegistryLoaded,
    int ProfileCount);

public sealed class BackgroundHardwareAgent : IAsyncDisposable
{
    public BackgroundHardwareAgent(HardwareMonitorService monitorService, ProfileRegistryFileStore profileStore)
    {
        ArgumentNullException.ThrowIfNull(monitorService);
        ArgumentNullException.ThrowIfNull(profileStore);
    }

    public AgentHealthSnapshot Health => throw new NotSupportedException();

    public HardwareSnapshot? LatestSnapshot => throw new NotSupportedException();

    public Task StartAsync() => throw new NotSupportedException();

    public Task StopAsync() => throw new NotSupportedException();

    public Task RestartAsync() => throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public static class AgentIpcProtocol
{
    public const int CurrentVersion = 1;
    public const string DefaultPipeName = "TheSpark.HardwareMonitor.Agent.v1";
}

public sealed record AgentIpcRequest(int Version, string Command);

public sealed record AgentIpcResponse(int Version, bool Success, AgentHealthSnapshot? Health, string? Error);

public sealed class AgentIpcServer : IAsyncDisposable
{
    public AgentIpcServer(BackgroundHardwareAgent agent, string pipeName)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
    }

    public Task StartAsync() => throw new NotSupportedException();

    public Task StopAsync() => throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class AgentIpcClient
{
    public AgentIpcClient(string pipeName, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public Task<AgentHealthSnapshot> GetHealthAsync() => throw new NotSupportedException();

    public Task RestartAsync() => throw new NotSupportedException();
}
