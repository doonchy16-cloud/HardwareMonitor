using System.Reflection;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
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
    public void Publisher_exposes_testable_one_shot_publish_boundary_without_credentials_in_api()
    {
        var assembly = typeof(AgentRuntimeOptions).Assembly;
        var publisherType = assembly.GetType("TheSpark.HardwareMonitor.Sensors.Agent.BridgeGatewayTelemetryPublisher");
        Assert.NotNull(publisherType);

        var constructor = publisherType!.GetConstructor(
            [typeof(string), typeof(string), typeof(ProfileRegistryFileStore), typeof(HttpMessageHandler)]);
        Assert.NotNull(constructor);

        var publish = publisherType.GetMethod(
            "PublishAsync",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(HardwareSnapshot), typeof(CancellationToken)],
            modifiers: null);
        Assert.NotNull(publish);
        Assert.Equal(typeof(Task<bool>), publish!.ReturnType);
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

        Assert.Contains("LastAcceptedSequence", propertyNames);
        Assert.Contains("LastProfileCount", propertyNames);
        Assert.Contains("LastSensorCount", propertyNames);
        Assert.DoesNotContain(propertyNames, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Credential", StringComparison.OrdinalIgnoreCase));
    }
}
