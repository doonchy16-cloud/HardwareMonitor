namespace TheSpark.HardwareMonitor.Core.Profiles;

public static class ProfileVisibilityResolver
{
    public static IReadOnlyList<HardwareProfile> ResolveVisibleProfiles(
        HardwareProfile viewer,
        IReadOnlyCollection<HardwareProfile> allProfiles)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(allProfiles);

        if (!viewer.Capabilities.Contains(ProfileCapability.ViewProfiles))
        {
            throw new UnauthorizedAccessException("Viewer profile lacks ViewProfiles capability.");
        }

        return viewer.ViewerScope switch
        {
            ViewerScope.AllProfiles => allProfiles
                .Where(profile => profile.Enabled)
                .ToArray(),
            ViewerScope.SelectedProfiles => allProfiles
                .Where(profile => profile.Enabled && viewer.VisibleProfileIds.Contains(profile.ProfileId))
                .ToArray(),
            _ => Array.Empty<HardwareProfile>()
        };
    }
}
