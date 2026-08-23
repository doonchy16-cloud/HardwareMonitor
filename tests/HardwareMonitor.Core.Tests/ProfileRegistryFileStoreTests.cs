using System.Text.Json;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileRegistryFileStoreTests
{
    [Fact]
    public void Store_rejects_blank_path()
    {
        Assert.Throws<ArgumentException>(() => new ProfileRegistryFileStore("   "));
    }

    [Fact]
    public async Task Missing_file_loads_as_empty_registry()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new ProfileRegistryFileStore(Path.Combine(root, "profiles.json"));

            var loaded = await store.LoadAsync();

            Assert.Equal(ProfileContract.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.Empty(loaded.Profiles);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_creates_parent_directory_and_round_trips_registry()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "nested", "profiles.json");
            var store = new ProfileRegistryFileStore(path);
            var expected = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [CreateProfile("Main-PC")]);

            await store.SaveAsync(expected);
            var loaded = await store.LoadAsync();

            Assert.True(File.Exists(path));
            Assert.Equal(expected, loaded);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_replaces_existing_registry_atomically()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "profiles.json");
            var store = new ProfileRegistryFileStore(path);
            await store.SaveAsync(new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [CreateProfile("Old")]));
            var replacement = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [CreateProfile("New")]);

            await store.SaveAsync(replacement);
            var loaded = await store.LoadAsync();

            Assert.Equal(replacement, loaded);
            Assert.Single(loaded.Profiles);
            Assert.Equal("New", loaded.Profiles[0].DisplayName);
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Corrupt_profile_file_is_not_silently_treated_as_empty()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "profiles.json");
            await File.WriteAllTextAsync(path, "{ definitely-not-valid-json");
            var store = new ProfileRegistryFileStore(path);

            await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static MonitoringProfile CreateProfile(string name) => new(
        Guid.NewGuid(),
        name,
        true,
        ProfileRole.Viewer | ProfileRole.Publisher,
        [new DeviceBinding("device-main")],
        ViewerScope.AllProfiles(),
        new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
        new ThermalPolicy(80, 92),
        new SensorVisibilityPolicy(UnavailableSensorBehavior.ShowUnavailable));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
