using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileCoreBehaviorTests
{
    [Fact]
    public void Empty_registry_contains_no_seeded_profiles()
    {
        var registry = ProfileRegistryDocument.Empty;

        Assert.Equal(ProfileContract.CurrentSchemaVersion, registry.SchemaVersion);
        Assert.Empty(registry.Profiles);
    }

    [Fact]
    public void All_profiles_scope_is_dynamic_and_stores_no_profile_ids()
    {
        var scope = ViewerScope.AllProfiles();

        Assert.Equal(ViewerScopeMode.AllProfiles, scope.Mode);
        Assert.Empty(scope.ProfileIds);
    }

    [Fact]
    public void Selected_profiles_scope_requires_ids_and_deduplicates_them()
    {
        var profileId = Guid.NewGuid();

        var scope = ViewerScope.SelectedProfiles(profileId, profileId);

        Assert.Equal(ViewerScopeMode.SelectedProfiles, scope.Mode);
        Assert.Equal([profileId], scope.ProfileIds);
        Assert.Throws<ArgumentException>(() => ViewerScope.SelectedProfiles());
        Assert.Throws<ArgumentException>(() => ViewerScope.SelectedProfiles(Guid.Empty));
    }

    [Fact]
    public void Device_binding_rejects_blank_device_id()
    {
        Assert.Throws<ArgumentException>(() => new DeviceBinding("   "));
    }

    [Fact]
    public void Freshness_policy_requires_positive_ordered_thresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FreshnessPolicy(TimeSpan.Zero, TimeSpan.FromSeconds(10)));
        Assert.Throws<ArgumentException>(() => new FreshnessPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)));
        Assert.Throws<ArgumentException>(() => new FreshnessPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Thermal_policy_requires_finite_positive_ordered_thresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalPolicy(0, 90));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalPolicy(double.NaN, 90));
        Assert.Throws<ArgumentException>(() => new ThermalPolicy(90, 90));
        Assert.Throws<ArgumentException>(() => new ThermalPolicy(95, 90));
    }

    [Fact]
    public void Profile_requires_stable_identity_name_and_at_least_one_role()
    {
        var valid = CreateProfile();

        Assert.Throws<ArgumentException>(() => valid with { Id = Guid.Empty });
        Assert.Throws<ArgumentException>(() => valid with { DisplayName = " " });
        Assert.Throws<ArgumentException>(() => valid with { Roles = ProfileRole.None });
    }

    [Fact]
    public void Registry_rejects_duplicate_profile_ids()
    {
        var profile = CreateProfile();

        Assert.Throws<ArgumentException>(() => new ProfileRegistryDocument(
            ProfileContract.CurrentSchemaVersion,
            [profile, profile]));
    }

    [Fact]
    public void Profile_roles_can_be_combined_without_turning_profiles_into_devices()
    {
        var roles = ProfileRole.Viewer | ProfileRole.Publisher | ProfileRole.TrainingMonitor;
        var profile = CreateProfile(roles: roles, deviceBindings: []);

        Assert.True(profile.Roles.HasFlag(ProfileRole.Viewer));
        Assert.True(profile.Roles.HasFlag(ProfileRole.Publisher));
        Assert.True(profile.Roles.HasFlag(ProfileRole.TrainingMonitor));
        Assert.Empty(profile.DeviceBindings);
    }

    [Fact]
    public void Json_round_trip_preserves_profiles_policies_and_schema_version()
    {
        var profile = CreateProfile(
            roles: ProfileRole.Viewer | ProfileRole.Publisher,
            deviceBindings: [new DeviceBinding("device-main")]);
        var document = new ProfileRegistryDocument(ProfileContract.CurrentSchemaVersion, [profile]);

        var json = ProfileJsonSerializer.Serialize(document);
        var restored = ProfileJsonSerializer.Deserialize(json);

        Assert.Equal(ProfileContract.CurrentSchemaVersion, restored.SchemaVersion);
        var restoredProfile = Assert.Single(restored.Profiles);
        Assert.Equal(profile, restoredProfile);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("My Phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Main-PC", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Forgey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Json_deserializer_rejects_unsupported_schema_versions()
    {
        const string json = "{\"schemaVersion\":999,\"profiles\":[]}";

        Assert.Throws<NotSupportedException>(() => ProfileJsonSerializer.Deserialize(json));
    }

    private static MonitoringProfile CreateProfile(
        ProfileRole roles = ProfileRole.Viewer,
        IReadOnlyList<DeviceBinding>? deviceBindings = null)
    {
        return new MonitoringProfile(
            Guid.NewGuid(),
            "Test Profile",
            true,
            roles,
            deviceBindings ?? [],
            ViewerScope.AllProfiles(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)),
            new ThermalPolicy(80, 92),
            new SensorVisibilityPolicy(UnavailableSensorBehavior.ShowUnavailable));
    }
}
