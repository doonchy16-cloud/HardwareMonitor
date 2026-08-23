using System.Reflection;
using TheSpark.HardwareMonitor.Core;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileCoreContractTests
{
    private static readonly Assembly CoreAssembly = typeof(RollingSeries).Assembly;

    [Fact]
    public void Profile_core_contract_surface_exists()
    {
        var expectedTypes = new[]
        {
            "TheSpark.HardwareMonitor.Core.Profiles.ProfileContract",
            "TheSpark.HardwareMonitor.Core.Profiles.MonitoringProfile",
            "TheSpark.HardwareMonitor.Core.Profiles.ProfileRole",
            "TheSpark.HardwareMonitor.Core.Profiles.ViewerScopeMode",
            "TheSpark.HardwareMonitor.Core.Profiles.ViewerScope",
            "TheSpark.HardwareMonitor.Core.Profiles.DeviceBinding",
            "TheSpark.HardwareMonitor.Core.Profiles.FreshnessPolicy",
            "TheSpark.HardwareMonitor.Core.Profiles.ThermalPolicy",
            "TheSpark.HardwareMonitor.Core.Profiles.SensorVisibilityPolicy",
            "TheSpark.HardwareMonitor.Core.Profiles.ProfileRegistryDocument",
            "TheSpark.HardwareMonitor.Core.Profiles.ProfileJsonSerializer",
        };

        foreach (var typeName in expectedTypes)
        {
            Assert.NotNull(CoreAssembly.GetType(typeName));
        }
    }

    [Fact]
    public void Profile_contract_starts_at_schema_version_one()
    {
        var contractType = CoreAssembly.GetType("TheSpark.HardwareMonitor.Core.Profiles.ProfileContract");
        Assert.NotNull(contractType);

        var field = contractType.GetField("CurrentSchemaVersion", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(1, field.GetRawConstantValue());
    }
}
