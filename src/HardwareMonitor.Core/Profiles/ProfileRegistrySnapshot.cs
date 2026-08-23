namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class ProfileRegistrySnapshot
{
    public ProfileRegistrySnapshot(long revision, IReadOnlyList<HardwareProfile> profiles)
    {
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        ArgumentNullException.ThrowIfNull(profiles);
        Revision = revision;
        Profiles = profiles.ToArray();
    }

    public long Revision { get; }
    public IReadOnlyList<HardwareProfile> Profiles { get; }

    public static ProfileRegistrySnapshot Empty { get; } =
        new(0, Array.Empty<HardwareProfile>());
}
