using TheSpark.HardwareMonitor.Core.Devices;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfileDomainTests
{
    [Fact]
    public void Profile_can_combine_viewer_publisher_and_training_capabilities()
    {
        var profile = new HardwareProfile(
            Guid.NewGuid(),
            "User-created profile",
            Guid.NewGuid(),
            new HashSet<ProfileCapability>
            {
                ProfileCapability.ViewProfiles,
                ProfileCapability.PublishHardwareTelemetry,
                ProfileCapability.TrainingMode
            },
            ViewerScope.AllProfiles,
            new HashSet<Guid>(),
            new FreshnessPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)));

        Assert.Contains(ProfileCapability.ViewProfiles, profile.Capabilities);
        Assert.Contains(ProfileCapability.PublishHardwareTelemetry, profile.Capabilities);
        Assert.Contains(ProfileCapability.TrainingMode, profile.Capabilities);
        Assert.Equal(ViewerScope.AllProfiles, profile.ViewerScope);
    }

    [Fact]
    public void Multiple_profiles_can_bind_to_the_same_physical_device()
    {
        var deviceId = Guid.NewGuid();
        var freshness = new FreshnessPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));

        var training = new HardwareProfile(
            Guid.NewGuid(), "Training", deviceId,
            new HashSet<ProfileCapability> { ProfileCapability.PublishHardwareTelemetry, ProfileCapability.TrainingMode },
            ViewerScope.None, new HashSet<Guid>(), freshness);

        var general = new HardwareProfile(
            Guid.NewGuid(), "General", deviceId,
            new HashSet<ProfileCapability> { ProfileCapability.PublishHardwareTelemetry },
            ViewerScope.None, new HashSet<Guid>(), freshness);

        Assert.NotEqual(training.ProfileId, general.ProfileId);
        Assert.Equal(deviceId, training.DeviceId);
        Assert.Equal(deviceId, general.DeviceId);
    }

    [Fact]
    public void AllProfiles_scope_does_not_require_selected_profile_ids()
    {
        var profile = new HardwareProfile(
            Guid.NewGuid(), "Viewer", null,
            new HashSet<ProfileCapability> { ProfileCapability.ViewProfiles },
            ViewerScope.AllProfiles,
            new HashSet<Guid>(),
            new FreshnessPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60)));

        Assert.Empty(profile.VisibleProfileIds);
        Assert.Equal(ViewerScope.AllProfiles, profile.ViewerScope);
    }

    [Fact]
    public void Freshness_policy_rejects_offline_threshold_not_later_than_stale_threshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FreshnessPolicy(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Device_record_is_platform_neutral()
    {
        var android = new DeviceRecord(
            Guid.NewGuid(), "User alias", DevicePlatform.Android, "arm64", null, null);

        Assert.Equal(DevicePlatform.Android, android.Platform);
        Assert.Equal("arm64", android.Architecture);
    }
}
