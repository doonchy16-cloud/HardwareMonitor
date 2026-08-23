using System.Reflection;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileRegistryFileStoreContractTests
{
    [Fact]
    public void Local_profile_store_contract_exists()
    {
        var assembly = typeof(ProfileContract).Assembly;
        var type = assembly.GetType("TheSpark.HardwareMonitor.Core.Profiles.ProfileRegistryFileStore");

        Assert.NotNull(type);
        Assert.NotNull(type.GetConstructor([typeof(string)]));
        Assert.NotNull(type.GetMethod("LoadAsync", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetMethod("SaveAsync", BindingFlags.Public | BindingFlags.Instance));
    }
}
