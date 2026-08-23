using System.Reflection;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileRegistryEditorContractTests
{
    [Fact]
    public void Registry_editor_contract_exists()
    {
        var assembly = typeof(ProfileContract).Assembly;
        var type = assembly.GetType("TheSpark.HardwareMonitor.Core.Profiles.ProfileRegistryEditor");

        Assert.NotNull(type);
        Assert.NotNull(type.GetMethod("Upsert", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("Remove", BindingFlags.Public | BindingFlags.Static));
    }
}
