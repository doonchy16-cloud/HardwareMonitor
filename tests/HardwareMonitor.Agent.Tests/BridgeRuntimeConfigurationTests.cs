using System.Text.Json;
using TheSpark.HardwareMonitor.Agent;
using Xunit;

namespace TheSpark.HardwareMonitor.Agent.Tests;

public sealed class BridgeRuntimeConfigurationTests
{
    [Fact]
    public async Task LoadsDeviceGatewayAndCredentialFromExistingBridgeLocalFilesWithoutPersistingSecret()
    {
        var root = Path.Combine(Path.GetTempPath(), "hm-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        var deviceId = Guid.NewGuid();
        const string token = "this-is-a-test-host-token-that-is-long-enough";
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "config", "identity.json"),
                JsonSerializer.Serialize(new { device_id = deviceId }),
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(root, "config", "transport-v2.local.json"),
                JsonSerializer.Serialize(new
                {
                    schema_version = "2.0",
                    gateway_url = "https://bridge.example/",
                    host_token = token
                }),
                TestContext.Current.CancellationToken);

            var config = BridgeRuntimeConfiguration.Load(root);

            Assert.Equal(deviceId, config.DeviceId);
            Assert.Equal(new Uri("https://bridge.example/"), config.GatewayBaseUri);
            Assert.EndsWith(Path.Combine("The Spark", "Hardware Monitor", "profiles.json"), config.ProfilePath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(token, await config.ReadCredentialAsync(TestContext.Current.CancellationToken));
            Assert.DoesNotContain(token, config.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ControllerTransportCredentialIsAcceptedForControllerMachineComposition()
    {
        var root = Path.Combine(Path.GetTempPath(), "hm-bridge-controller-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "config", "identity.json"),
                JsonSerializer.Serialize(new { device_id = Guid.NewGuid() }),
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(root, "config", "transport-v2.local.json"),
                JsonSerializer.Serialize(new
                {
                    schema_version = "2.0",
                    gateway_url = "https://bridge.example/",
                    controller_token = "controller-token-for-test-that-is-long-enough"
                }),
                TestContext.Current.CancellationToken);

            var config = BridgeRuntimeConfiguration.Load(root);

            Assert.Equal(BridgeRuntimeRole.Controller, config.Role);
            Assert.StartsWith("controller-token", await config.ReadCredentialAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
