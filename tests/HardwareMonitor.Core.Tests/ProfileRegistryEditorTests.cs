using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileRegistryEditorTests
{
    [Fact]
    public void Upsert_appends_new_profile_without_mutating_input()
    {
        var first = CreateProfile("First");
        var added = CreateProfile("Added");
        var original = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [first]);

        var updated = ProfileRegistryEditor.Upsert(original, added);

        Assert.Single(original.Profiles);
        Assert.Equal([first, added], updated.Profiles);
    }

    [Fact]
    public void Upsert_replaces_matching_id_in_place_without_duplicates()
    {
        var first = CreateProfile("Old");
        var second = CreateProfile("Second");
        var replacement = CreateProfile("New", id: first.Id);
        var original = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [first, second]);

        var updated = ProfileRegistryEditor.Upsert(original, replacement);

        Assert.Equal(2, updated.Profiles.Count);
        Assert.Equal(replacement, updated.Profiles[0]);
        Assert.Equal(second, updated.Profiles[1]);
        Assert.Single(updated.Profiles.Where(profile => profile.Id == first.Id));
    }

    [Fact]
    public void Upsert_rejects_selected_scope_with_unknown_profile_reference()
    {
        var existing = CreateProfile("Existing");
        var viewer = CreateProfile(
            "Viewer",
            viewerScope: ViewerScope.SelectedProfiles(Guid.NewGuid()));
        var registry = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [existing]);

        Assert.Throws<InvalidOperationException>(() => ProfileRegistryEditor.Upsert(registry, viewer));
    }

    [Fact]
    public void Remove_deletes_unreferenced_profile_without_mutating_input()
    {
        var first = CreateProfile("First");
        var second = CreateProfile("Second");
        var original = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [first, second]);

        var updated = ProfileRegistryEditor.Remove(original, first.Id);

        Assert.Equal(2, original.Profiles.Count);
        Assert.Equal([second], updated.Profiles);
    }

    [Fact]
    public void Remove_rejects_unknown_profile_id()
    {
        var registry = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [CreateProfile("Known")]);

        Assert.Throws<KeyNotFoundException>(() => ProfileRegistryEditor.Remove(registry, Guid.NewGuid()));
    }

    [Fact]
    public void Remove_rejects_profile_referenced_by_another_selected_viewer()
    {
        var target = CreateProfile("Target");
        var viewer = CreateProfile(
            "Viewer",
            viewerScope: ViewerScope.SelectedProfiles(target.Id));
        var registry = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [target, viewer]);

        Assert.Throws<InvalidOperationException>(() => ProfileRegistryEditor.Remove(registry, target.Id));
    }

    private static MonitoringProfile CreateProfile(
        string name,
        Guid? id = null,
        ViewerScope? viewerScope = null) => new(
        id ?? Guid.NewGuid(),
        name,
        true,
        ProfileRole.Viewer,
        [],
        viewerScope ?? ViewerScope.AllProfiles(),
        new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
        new ThermalPolicy(80, 92),
        new SensorVisibilityPolicy(UnavailableSensorBehavior.ShowUnavailable));
}
