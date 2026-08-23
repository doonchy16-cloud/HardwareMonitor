using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileVisibilityResolverTests
{
    private static HardwareProfile Profile(
        string name,
        ViewerScope scope = ViewerScope.None,
        IEnumerable<Guid>? visibleIds = null,
        bool enabled = true,
        bool canView = false)
    {
        var capabilities = canView
            ? new HashSet<ProfileCapability> { ProfileCapability.ViewProfiles }
            : new HashSet<ProfileCapability>();

        return new HardwareProfile(
            Guid.NewGuid(),
            name,
            null,
            capabilities,
            scope,
            new HashSet<Guid>(visibleIds ?? Array.Empty<Guid>()),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
            enabled: enabled);
    }

    [Fact]
    public void AllProfiles_dynamically_includes_profiles_added_after_viewer_creation()
    {
        var viewer = Profile("Viewer", ViewerScope.AllProfiles, canView: true);
        var first = Profile("First");
        var initial = new[] { viewer, first };

        var before = ProfileVisibilityResolver.ResolveVisibleProfiles(viewer, initial);
        Assert.Contains(first.ProfileId, before.Select(profile => profile.ProfileId));

        var addedLater = Profile("Added later");
        var after = ProfileVisibilityResolver.ResolveVisibleProfiles(viewer, new[] { viewer, first, addedLater });

        Assert.Contains(addedLater.ProfileId, after.Select(profile => profile.ProfileId));
    }

    [Fact]
    public void SelectedProfiles_returns_only_selected_enabled_profiles()
    {
        var selected = Profile("Selected");
        var unselected = Profile("Unselected");
        var disabledSelected = Profile("Disabled", enabled: false);
        var viewer = Profile(
            "Viewer",
            ViewerScope.SelectedProfiles,
            new[] { selected.ProfileId, disabledSelected.ProfileId },
            canView: true);

        var visible = ProfileVisibilityResolver.ResolveVisibleProfiles(
            viewer,
            new[] { viewer, selected, unselected, disabledSelected });

        Assert.Equal(new[] { selected.ProfileId }, visible.Select(profile => profile.ProfileId));
    }

    [Fact]
    public void AllProfiles_excludes_disabled_profiles()
    {
        var viewer = Profile("Viewer", ViewerScope.AllProfiles, canView: true);
        var enabled = Profile("Enabled");
        var disabled = Profile("Disabled", enabled: false);

        var visible = ProfileVisibilityResolver.ResolveVisibleProfiles(viewer, new[] { viewer, enabled, disabled });

        Assert.Contains(enabled.ProfileId, visible.Select(profile => profile.ProfileId));
        Assert.DoesNotContain(disabled.ProfileId, visible.Select(profile => profile.ProfileId));
    }

    [Fact]
    public void Viewer_without_ViewProfiles_capability_is_rejected()
    {
        var viewer = Profile("Not authorized", ViewerScope.AllProfiles, canView: false);

        Assert.Throws<UnauthorizedAccessException>(() =>
            ProfileVisibilityResolver.ResolveVisibleProfiles(viewer, new[] { viewer }));
    }
}
