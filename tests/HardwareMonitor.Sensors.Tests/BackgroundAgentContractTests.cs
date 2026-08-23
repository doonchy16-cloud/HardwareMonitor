using System.Reflection;
using TheSpark.HardwareMonitor.Core;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Sensors;

namespace TheSpark.HardwareMonitor.Sensors.Tests;

public sealed class BackgroundAgentContractTests
{
    private static readonly Assembly SensorsAssembly = typeof(HardwareMonitorService).Assembly;
    private const string Namespace = "TheSpark.HardwareMonitor.Sensors.Agent";

    [Fact]
    public void Background_agent_contract_surface_exists()
    {
        var expectedTypes = new[]
        {
            $"{Namespace}.AgentLifecycleState",
            $"{Namespace}.AgentHealthSnapshot",
            $"{Namespace}.BackgroundHardwareAgent",
            $"{Namespace}.AgentIpcProtocol",
            $"{Namespace}.AgentIpcRequest",
            $"{Namespace}.AgentIpcResponse",
            $"{Namespace}.AgentIpcServer",
            $"{Namespace}.AgentIpcClient",
        };

        foreach (var typeName in expectedTypes)
        {
            Assert.NotNull(SensorsAssembly.GetType(typeName));
        }
    }

    [Fact]
    public void Background_agent_exposes_lifecycle_health_and_restart_contracts()
    {
        var state = RequiredType("AgentLifecycleState");
        foreach (var name in new[] { "Starting", "Running", "Stopping", "Stopped", "Faulted" })
        {
            Assert.True(Enum.IsDefined(state, name));
        }

        var health = RequiredType("AgentHealthSnapshot");
        Assert.NotNull(health.GetProperty("State"));
        Assert.NotNull(health.GetProperty("StartedAt"));
        Assert.NotNull(health.GetProperty("LastSnapshotAt"));
        Assert.NotNull(health.GetProperty("LastError"));
        Assert.NotNull(health.GetProperty("SensorEngineRunning"));
        Assert.NotNull(health.GetProperty("ProfileRegistryLoaded"));
        Assert.NotNull(health.GetProperty("ProfileCount"));

        var agent = RequiredType("BackgroundHardwareAgent");
        Assert.NotNull(agent.GetProperty("Health"));
        Assert.NotNull(agent.GetProperty("LatestSnapshot"));
        Assert.NotNull(agent.GetMethod("StartAsync"));
        Assert.NotNull(agent.GetMethod("StopAsync"));
        Assert.NotNull(agent.GetMethod("RestartAsync"));

        var protocol = RequiredType("AgentIpcProtocol");
        Assert.NotNull(protocol.GetField("CurrentVersion", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(protocol.GetField("DefaultPipeName", BindingFlags.Public | BindingFlags.Static));

        var server = RequiredType("AgentIpcServer");
        Assert.NotNull(server.GetMethod("StartAsync"));
        Assert.NotNull(server.GetMethod("StopAsync"));

        var client = RequiredType("AgentIpcClient");
        Assert.NotNull(client.GetMethod("GetHealthAsync"));
        Assert.NotNull(client.GetMethod("RestartAsync"));
    }

    [Fact]
    public void Background_agent_contract_is_constructible_and_observes_sensor_faults()
    {
        var agent = RequiredType("BackgroundHardwareAgent");
        Assert.NotNull(agent.GetConstructor([typeof(HardwareMonitorService), typeof(ProfileRegistryFileStore)]));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(agent));

        var server = RequiredType("AgentIpcServer");
        Assert.NotNull(server.GetConstructor([agent, typeof(string)]));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(server));

        var client = RequiredType("AgentIpcClient");
        Assert.NotNull(client.GetConstructor([typeof(string), typeof(TimeSpan)]));

        Assert.NotNull(typeof(HardwareMonitorService).GetEvent("Faulted", BindingFlags.Instance | BindingFlags.Public));
    }

    private static Type RequiredType(string name)
    {
        var type = SensorsAssembly.GetType($"{Namespace}.{name}");
        Assert.NotNull(type);
        return type;
    }
}
