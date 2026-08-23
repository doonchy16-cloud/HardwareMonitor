using System.Reflection;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileTelemetryRouterContractTests
{
    private static readonly Assembly CoreAssembly = typeof(ProfileContract).Assembly;
    private const string Namespace = "TheSpark.HardwareMonitor.Core.Profiles.Telemetry";

    [Fact]
    public void Phase4_profile_telemetry_contract_surface_exists()
    {
        var expectedTypes = new[]
        {
            $"{Namespace}.ProfileThermalState",
            $"{Namespace}.ProfileSensorReading",
            $"{Namespace}.ProfileHardwareDeviceSnapshot",
            $"{Namespace}.ProfileTelemetrySnapshot",
            $"{Namespace}.ProfileTelemetryRouter",
        };

        foreach (var typeName in expectedTypes)
        {
            Assert.NotNull(CoreAssembly.GetType(typeName));
        }
    }

    [Fact]
    public void Router_exposes_one_pure_route_entry_point()
    {
        var router = CoreAssembly.GetType($"{Namespace}.ProfileTelemetryRouter");
        Assert.NotNull(router);

        var methods = router!
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.DeclaringType == router)
            .ToArray();

        var route = Assert.Single(methods);
        Assert.Equal("Route", route.Name);
        Assert.Equal(3, route.GetParameters().Length);
    }
}