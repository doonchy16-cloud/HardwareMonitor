using System.Reflection;
using TheSpark.HardwareMonitor.Core;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileCoreContractTests
{
    private static readonly Assembly CoreAssembly = typeof(RollingSeries).Assembly;
    private const string Namespace = "TheSpark.HardwareMonitor.Core.Profiles";

    [Fact]
    public void Profile_core_contract_surface_exists()
    {
        var expectedTypes = new[]
        {
            $"{Namespace}.ProfileContract",
            $"{Namespace}.MonitoringProfile",
            $"{Namespace}.ProfileRole",
            $"{Namespace}.ViewerScopeMode",
            $"{Namespace}.ViewerScope",
            $"{Namespace}.DeviceBinding",
            $"{Namespace}.FreshnessPolicy",
            $"{Namespace}.ThermalPolicy",
            $"{Namespace}.UnavailableSensorBehavior",
            $"{Namespace}.SensorVisibilityPolicy",
            $"{Namespace}.ProfileRegistryDocument",
            $"{Namespace}.ProfileJsonSerializer",
        };

        foreach (var typeName in expectedTypes)
        {
            Assert.NotNull(CoreAssembly.GetType(typeName));
        }
    }

    [Fact]
    public void Profile_contract_starts_at_schema_version_one()
    {
        var contractType = RequiredType("ProfileContract");
        var field = contractType.GetField("CurrentSchemaVersion", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(1, field.GetRawConstantValue());
    }

    [Fact]
    public void Profile_core_exposes_the_locked_configuration_contracts()
    {
        AssertConstructor("DeviceBinding", typeof(string));
        AssertConstructor("FreshnessPolicy", typeof(TimeSpan), typeof(TimeSpan));
        AssertConstructor("ThermalPolicy", typeof(double), typeof(double));

        var viewerScope = RequiredType("ViewerScope");
        Assert.NotNull(viewerScope.GetMethod("AllProfiles", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes));
        Assert.NotNull(viewerScope.GetMethod("SelectedProfiles", BindingFlags.Public | BindingFlags.Static));

        var profile = RequiredType("MonitoringProfile");
        Assert.NotNull(profile.GetProperty("Id"));
        Assert.NotNull(profile.GetProperty("DisplayName"));
        Assert.NotNull(profile.GetProperty("Enabled"));
        Assert.NotNull(profile.GetProperty("Roles"));
        Assert.NotNull(profile.GetProperty("DeviceBindings"));
        Assert.NotNull(profile.GetProperty("ViewerScope"));
        Assert.NotNull(profile.GetProperty("Freshness"));
        Assert.NotNull(profile.GetProperty("Thermal"));
        Assert.NotNull(profile.GetProperty("SensorVisibility"));

        var registry = RequiredType("ProfileRegistryDocument");
        Assert.NotNull(registry.GetProperty("SchemaVersion"));
        Assert.NotNull(registry.GetProperty("Profiles"));
        Assert.NotNull(registry.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static));

        var serializer = RequiredType("ProfileJsonSerializer");
        Assert.NotNull(serializer.GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(serializer.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Static));
    }

    private static Type RequiredType(string name)
    {
        var type = CoreAssembly.GetType($"{Namespace}.{name}");
        Assert.NotNull(type);
        return type;
    }

    private static void AssertConstructor(string typeName, params Type[] parameterTypes)
    {
        var type = RequiredType(typeName);
        Assert.NotNull(type.GetConstructor(parameterTypes));
    }
}
