namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class HardwareProfile
{
    public HardwareProfile(
        Guid profileId,
        string name,
        Guid? deviceId,
        IReadOnlySet<ProfileCapability> capabilities,
        ViewerScope viewerScope,
        IReadOnlySet<Guid> visibleProfileIds,
        FreshnessPolicy freshnessPolicy,
        bool enabled = true,
        long revision = 0)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must not be empty.", nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Profile name must not be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(visibleProfileIds);
        ArgumentNullException.ThrowIfNull(freshnessPolicy);

        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        ProfileId = profileId;
        Name = name.Trim();
        DeviceId = deviceId;
        Capabilities = new HashSet<ProfileCapability>(capabilities);
        ViewerScope = viewerScope;
        VisibleProfileIds = new HashSet<Guid>(visibleProfileIds);
        FreshnessPolicy = freshnessPolicy;
        Enabled = enabled;
        Revision = revision;
    }

    public Guid ProfileId { get; }
    public string Name { get; }
    public Guid? DeviceId { get; }
    public IReadOnlySet<ProfileCapability> Capabilities { get; }
    public ViewerScope ViewerScope { get; }
    public IReadOnlySet<Guid> VisibleProfileIds { get; }
    public FreshnessPolicy FreshnessPolicy { get; }
    public bool Enabled { get; }
    public long Revision { get; }
}
