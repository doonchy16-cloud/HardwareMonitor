namespace TheSpark.HardwareMonitor.Core.Profiles;

public static class ProfileRegistryEditor
{
    public static ProfileRegistryDocument Upsert(ProfileRegistryDocument registry, MonitoringProfile profile)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(profile);

        var profiles = registry.Profiles.ToList();
        var index = profiles.FindIndex(existing => existing.Id == profile.Id);
        if (index >= 0)
        {
            profiles[index] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        ValidateSelectedProfileReferences(profiles);
        return new ProfileRegistryDocument(registry.SchemaVersion, profiles);
    }

    public static ProfileRegistryDocument Remove(ProfileRegistryDocument registry, Guid profileId)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var profiles = registry.Profiles.ToList();
        var index = profiles.FindIndex(profile => profile.Id == profileId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Profile {profileId} does not exist.");
        }

        var referencedBy = profiles.FirstOrDefault(profile =>
            profile.Id != profileId
            && profile.ViewerScope.Mode == ViewerScopeMode.SelectedProfiles
            && profile.ViewerScope.ProfileIds.Contains(profileId));
        if (referencedBy is not null)
        {
            throw new InvalidOperationException(
                $"Profile '{profiles[index].DisplayName}' cannot be removed because '{referencedBy.DisplayName}' references it.");
        }

        profiles.RemoveAt(index);
        return new ProfileRegistryDocument(registry.SchemaVersion, profiles);
    }

    private static void ValidateSelectedProfileReferences(IReadOnlyList<MonitoringProfile> profiles)
    {
        var ids = profiles.Select(profile => profile.Id).ToHashSet();
        foreach (var profile in profiles)
        {
            if (profile.ViewerScope.Mode != ViewerScopeMode.SelectedProfiles)
            {
                continue;
            }

            var missingId = profile.ViewerScope.ProfileIds.FirstOrDefault(id => !ids.Contains(id));
            if (missingId != Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"Profile '{profile.DisplayName}' references unknown profile {missingId}.");
            }
        }
    }
}
