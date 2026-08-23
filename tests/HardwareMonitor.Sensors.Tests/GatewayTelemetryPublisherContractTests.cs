using System.Reflection;
using TheSpark.HardwareMonitor.Sensors.Agent;

namespace TheSpark.HardwareMonitor.Sensors.Tests;

public sealed class GatewayTelemetryPublisherContractTests
{
    [Fact]
    public void Agent_options_expose_optional_bridge_root_and_durable_sequence_state()
    {
        var optionsType = typeof(AgentRuntimeOptions);

        var bridgeRoot = optionsType.GetProperty("BridgeRoot", BindingFlags.Instance | BindingFlags.Public);
        var sequencePath = optionsType.GetProperty("TelemetrySequencePath", BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(bridgeRoot);
        Assert.Equal(typeof(string), bridgeRoot!.PropertyType);
        Assert.NotNull(sequencePath);
        Assert.Equal(typeof(string), sequencePath!.PropertyType);
    }

    [Fact]
    public void Sensors_agent_exposes_dedicated_bridge_gateway_publisher_boundary()
    {
        var assembly = typeof(AgentRuntimeOptions).Assembly;
        var publisherType = assembly.GetType("TheSpark.HardwareMonitor.Sensors.Agent.BridgeGatewayTelemetryPublisher");

        Assert.NotNull(publisherType);
        Assert.NotNull(publisherType!.GetMethod("StartAsync", BindingFlags.Instance | BindingFlags.Public));
        Assert.NotNull(publisherType.GetMethod("Queue", BindingFlags.Instance | BindingFlags.Public));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(publisherType));
    }

    [Fact]
    public void Publisher_status_surface_cannot_expose_transport_credentials()
    {
        var assembly = typeof(AgentRuntimeOptions).Assembly;
        var statusType = assembly.GetType("TheSpark.HardwareMonitor.Sensors.Agent.BridgeGatewayTelemetryPublisherStatus");

        Assert.NotNull(statusType);
        var propertyNames = statusType!
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Credential", StringComparison.OrdinalIgnoreCase));
    }
}
