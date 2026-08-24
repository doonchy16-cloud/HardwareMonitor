using System.Text.Json;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileRegistryRevisionAndRecoveryTests
{
    [Fact]
    public void Registry_defaults_to_revision_zero()
    {
        var registry = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [CreateProfile("Main")]);

        Assert.Equal(0, registry.Revision);
    }

    [Fact]
    public void Registry_rejects_negative_revision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, -1, [CreateProfile("Main")]));
    }

    [Fact]
    public void Registry_revision_round_trips_through_json()
    {
        var expected = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, 17, [CreateProfile("Main")]);

        var loaded = ProfileJsonSerializer.Deserialize(ProfileJsonSerializer.Serialize(expected));

        Assert.Equal(17, loaded.Revision);
        Assert.Equal(Assert.Single(expected.Profiles), Assert.Single(loaded.Profiles));
    }

    [Fact]
    public void Local_editor_preserves_authoritative_revision()
    {
        var existing = CreateProfile("Old");
        var registry = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, 8, [existing]);
        var replacement = CreateProfile("New", existing.Id);

        var updated = ProfileRegistryEditor.Upsert(registry, replacement);
        var removed = ProfileRegistryEditor.Remove(updated, replacement.Id);

        Assert.Equal(8, updated.Revision);
        Assert.Equal(8, removed.Revision);
    }

    [Fact]
    public async Task Store_recovers_corrupt_primary_from_valid_last_known_good_backup()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "profiles.json");
            var store = new ProfileRegistryFileStore(path);
            var expected = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, 9, [CreateProfile("Recovered")]);
            await store.SaveAsync(expected, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(path + ".bak"));

            await File.WriteAllTextAsync(path, "{ broken", TestContext.Current.CancellationToken);

            var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(9, loaded.Revision);
            Assert.Equal("Recovered", Assert.Single(loaded.Profiles).DisplayName);
            var restoredText = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            var restored = ProfileJsonSerializer.Deserialize(restoredText);
            Assert.Equal(9, restored.Revision);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Store_still_throws_when_primary_and_backup_are_both_corrupt()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "profiles.json");
            await File.WriteAllTextAsync(path, "{ broken-primary", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(path + ".bak", "{ broken-backup", TestContext.Current.CancellationToken);
            var store = new ProfileRegistryFileStore(path);

            await Assert.ThrowsAnyAsync<JsonException>(() => store.LoadAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static MonitoringProfile CreateProfile(string name, Guid? id = null) => new(
        id ?? Guid.NewGuid(),
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
