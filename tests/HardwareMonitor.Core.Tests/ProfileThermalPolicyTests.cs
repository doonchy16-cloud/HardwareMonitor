using TheSpark.HardwareMonitor.Core.Profiles;
using Xunit;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileThermalPolicyTests
{
    [Fact]
    public async Task Custom_thermal_thresholds_round_trip_through_profile_cache()
    {
        var directory = Directory.CreateTempSubdirectory("hardware-monitor-thermal-profile-test-");
        try
        {
            var profile = new HardwareProfile(
                Guid.NewGuid(),
                "User configured thermal policy",
                Guid.NewGuid(),
                new HashSet<ProfileCapability> { ProfileCapability.PublishHardwareTelemetry },
                ViewerScope.None,
                new HashSet<Guid>(),
                new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
                revision: 4,
                thermalThresholdPolicy: new ThermalThresholdPolicy(74, 84, 91));
            var repository = new JsonProfileRepository(Path.Combine(directory.FullName, "profiles.json"));

            await repository.SaveAsync(new ProfileRegistrySnapshot(8, [profile]));
            var loaded = await repository.LoadAsync();

            Assert.True(loaded.Success);
            var saved = Assert.Single(loaded.Snapshot!.Profiles);
            Assert.Equal(74, saved.ThermalThresholdPolicy.WarmCelsius);
            Assert.Equal(84, saved.ThermalThresholdPolicy.HotCelsius);
            Assert.Equal(91, saved.ThermalThresholdPolicy.CriticalCelsius);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Profile_without_explicit_thermal_policy_uses_safe_existing_gpu_defaults()
    {
        var profile = new HardwareProfile(
            Guid.NewGuid(),
            "Defaults",
            null,
            new HashSet<ProfileCapability>(),
            ViewerScope.None,
            new HashSet<Guid>(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)));

        Assert.Equal(70, profile.ThermalThresholdPolicy.WarmCelsius);
        Assert.Equal(82, profile.ThermalThresholdPolicy.HotCelsius);
        Assert.Equal(92, profile.ThermalThresholdPolicy.CriticalCelsius);
    }
}
