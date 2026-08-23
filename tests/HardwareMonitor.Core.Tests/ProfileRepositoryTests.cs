using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileRepositoryTests
{
    private static HardwareProfile MakeProfile(Guid profileId, Guid? deviceId, string name) =>
        new(
            profileId,
            name,
            deviceId,
            new HashSet<ProfileCapability>
            {
                ProfileCapability.ViewProfiles,
                ProfileCapability.PublishHardwareTelemetry
            },
            ViewerScope.AllProfiles,
            new HashSet<Guid>(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
            revision: 3,
            sensorVisibilityPolicy: new SensorVisibilityPolicy(
                new HashSet<SensorKind> { SensorKind.Temperature, SensorKind.Load }));

    [Fact]
    public async Task Missing_cache_loads_as_valid_empty_registry()
    {
        var directory = Directory.CreateTempSubdirectory("hardware-monitor-profile-test-");
        try
        {
            var repository = new JsonProfileRepository(Path.Combine(directory.FullName, "profiles.json"));

            var result = await repository.LoadAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.Snapshot);
            Assert.Equal(0, result.Snapshot.Revision);
            Assert.Empty(result.Snapshot.Profiles);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Zero_profile_registry_round_trips()
    {
        var directory = Directory.CreateTempSubdirectory("hardware-monitor-profile-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "profiles.json");
            var repository = new JsonProfileRepository(path);
            await repository.SaveAsync(new ProfileRegistrySnapshot(7, Array.Empty<HardwareProfile>()));

            var loaded = await repository.LoadAsync();

            Assert.True(loaded.Success);
            Assert.Equal(7, loaded.Snapshot!.Revision);
            Assert.Empty(loaded.Snapshot.Profiles);
            Assert.Empty(Directory.EnumerateFiles(directory.FullName, "*.tmp"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Multiple_profiles_bound_to_same_device_round_trip_without_losing_policy()
    {
        var directory = Directory.CreateTempSubdirectory("hardware-monitor-profile-test-");
        try
        {
            var deviceId = Guid.NewGuid();
            var first = MakeProfile(Guid.NewGuid(), deviceId, "First user profile");
            var second = MakeProfile(Guid.NewGuid(), deviceId, "Second user profile");
            var repository = new JsonProfileRepository(Path.Combine(directory.FullName, "profiles.json"));

            await repository.SaveAsync(new ProfileRegistrySnapshot(12, new[] { first, second }));
            var loaded = await repository.LoadAsync();

            Assert.True(loaded.Success);
            Assert.Equal(12, loaded.Snapshot!.Revision);
            Assert.Equal(2, loaded.Snapshot.Profiles.Count);
            Assert.All(loaded.Snapshot.Profiles, profile => Assert.Equal(deviceId, profile.DeviceId));
            Assert.All(loaded.Snapshot.Profiles, profile => Assert.Equal(ViewerScope.AllProfiles, profile.ViewerScope));
            Assert.All(loaded.Snapshot.Profiles, profile => Assert.Contains(ProfileCapability.ViewProfiles, profile.Capabilities));
            Assert.All(loaded.Snapshot.Profiles, profile => Assert.Contains(SensorKind.Temperature, profile.SensorVisibilityPolicy.VisibleKinds));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Corrupt_cache_returns_explicit_error_instead_of_silent_empty_defaults()
    {
        var directory = Directory.CreateTempSubdirectory("hardware-monitor-profile-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "profiles.json");
            await File.WriteAllTextAsync(path, "{ definitely-not-json");
            var repository = new JsonProfileRepository(path);

            var loaded = await repository.LoadAsync();

            Assert.False(loaded.Success);
            Assert.Null(loaded.Snapshot);
            Assert.False(string.IsNullOrWhiteSpace(loaded.Error));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
