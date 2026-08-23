namespace TheSpark.HardwareMonitor.Core.Profiles;

public static class ProfileContract
{
    public const int CurrentSchemaVersion = 1;
}

[Flags]
public enum ProfileRole
{
    None = 0,
    Viewer = 1 << 0,
    Publisher = 1 << 1,
    TrainingMonitor = 1 << 2,
}

public enum ViewerScopeMode
{
    SelectedProfiles = 0,
    AllProfiles = 1,
}

public enum UnavailableSensorBehavior
{
    Hide = 0,
    ShowUnavailable = 1,
}

public sealed record DeviceBinding(string DeviceId);

public sealed record FreshnessPolicy(TimeSpan StaleAfter, TimeSpan OfflineAfter);

public sealed record ThermalPolicy(double WarningCelsius, double CriticalCelsius);

public sealed record SensorVisibilityPolicy(UnavailableSensorBehavior UnavailableSensors);

public sealed record ViewerScope(ViewerScopeMode Mode, IReadOnlyList<Guid> ProfileIds)
{
    public static ViewerScope AllProfiles() => throw new NotImplementedException();

    public static ViewerScope SelectedProfiles(params Guid[] profileIds) => throw new NotImplementedException();
}

public sealed record MonitoringProfile(
    Guid Id,
    string DisplayName,
    bool Enabled,
    ProfileRole Roles,
    IReadOnlyList<DeviceBinding> DeviceBindings,
    ViewerScope ViewerScope,
    FreshnessPolicy Freshness,
    ThermalPolicy Thermal,
    SensorVisibilityPolicy SensorVisibility);

public sealed record ProfileRegistryDocument(int SchemaVersion, IReadOnlyList<MonitoringProfile> Profiles)
{
    public static ProfileRegistryDocument Empty => throw new NotImplementedException();
}

public static class ProfileJsonSerializer
{
    public static string Serialize(ProfileRegistryDocument document) => throw new NotImplementedException();

    public static ProfileRegistryDocument Deserialize(string json) => throw new NotImplementedException();
}
