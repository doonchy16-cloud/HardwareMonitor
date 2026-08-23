using System.Text.Json;
using System.Text.Json.Serialization;

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

public sealed record DeviceBinding
{
    [JsonConstructor]
    public DeviceBinding(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("Device ID cannot be blank.", nameof(deviceId));
        }

        DeviceId = deviceId.Trim();
    }

    public string DeviceId { get; }
}

public sealed record FreshnessPolicy
{
    [JsonConstructor]
    public FreshnessPolicy(TimeSpan staleAfter, TimeSpan offlineAfter)
    {
        if (staleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfter), "Stale threshold must be positive.");
        }

        if (offlineAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(offlineAfter), "Offline threshold must be positive.");
        }

        if (offlineAfter <= staleAfter)
        {
            throw new ArgumentException("Offline threshold must be greater than stale threshold.", nameof(offlineAfter));
        }

        StaleAfter = staleAfter;
        OfflineAfter = offlineAfter;
    }

    public TimeSpan StaleAfter { get; }

    public TimeSpan OfflineAfter { get; }
}

public sealed record ThermalPolicy
{
    [JsonConstructor]
    public ThermalPolicy(double warningCelsius, double criticalCelsius)
    {
        if (!double.IsFinite(warningCelsius) || warningCelsius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warningCelsius), "Warning temperature must be finite and positive.");
        }

        if (!double.IsFinite(criticalCelsius) || criticalCelsius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(criticalCelsius), "Critical temperature must be finite and positive.");
        }

        if (criticalCelsius <= warningCelsius)
        {
            throw new ArgumentException("Critical temperature must be greater than warning temperature.", nameof(criticalCelsius));
        }

        WarningCelsius = warningCelsius;
        CriticalCelsius = criticalCelsius;
    }

    public double WarningCelsius { get; }

    public double CriticalCelsius { get; }
}

public sealed record SensorVisibilityPolicy(UnavailableSensorBehavior UnavailableSensors);

public sealed record ViewerScope
{
    [JsonConstructor]
    public ViewerScope(ViewerScopeMode mode, IReadOnlyList<Guid> profileIds)
    {
        ArgumentNullException.ThrowIfNull(profileIds);

        var ids = profileIds.Distinct().ToArray();
        if (ids.Any(static id => id == Guid.Empty))
        {
            throw new ArgumentException("Viewer scope cannot contain an empty profile ID.", nameof(profileIds));
        }

        if (mode == ViewerScopeMode.AllProfiles)
        {
            if (ids.Length != 0)
            {
                throw new ArgumentException("All Profiles is dynamic and cannot persist profile IDs.", nameof(profileIds));
            }
        }
        else if (mode == ViewerScopeMode.SelectedProfiles)
        {
            if (ids.Length == 0)
            {
                throw new ArgumentException("Selected Profiles requires at least one profile ID.", nameof(profileIds));
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Mode = mode;
        ProfileIds = ids;
    }

    public ViewerScopeMode Mode { get; }

    public IReadOnlyList<Guid> ProfileIds { get; }

    public static ViewerScope AllProfiles() => new(ViewerScopeMode.AllProfiles, Array.Empty<Guid>());

    public static ViewerScope SelectedProfiles(params Guid[] profileIds)
    {
        ArgumentNullException.ThrowIfNull(profileIds);
        return new ViewerScope(ViewerScopeMode.SelectedProfiles, profileIds);
    }
}

public sealed record MonitoringProfile
{
    private const ProfileRole SupportedRoles = ProfileRole.Viewer | ProfileRole.Publisher | ProfileRole.TrainingMonitor;

    private Guid _id;
    private string _displayName = string.Empty;
    private ProfileRole _roles;
    private IReadOnlyList<DeviceBinding> _deviceBindings = Array.Empty<DeviceBinding>();
    private ViewerScope _viewerScope = Profiles.ViewerScope.AllProfiles();
    private FreshnessPolicy _freshness = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));
    private ThermalPolicy _thermal = new(80, 92);
    private SensorVisibilityPolicy _sensorVisibility = new(UnavailableSensorBehavior.Hide);

    [JsonConstructor]
    public MonitoringProfile(
        Guid id,
        string displayName,
        bool enabled,
        ProfileRole roles,
        IReadOnlyList<DeviceBinding> deviceBindings,
        ViewerScope viewerScope,
        FreshnessPolicy freshness,
        ThermalPolicy thermal,
        SensorVisibilityPolicy sensorVisibility)
    {
        Id = id;
        DisplayName = displayName;
        Enabled = enabled;
        Roles = roles;
        DeviceBindings = deviceBindings;
        ViewerScope = viewerScope;
        Freshness = freshness;
        Thermal = thermal;
        SensorVisibility = sensorVisibility;
    }

    public Guid Id
    {
        get => _id;
        init
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("Profile ID cannot be empty.", nameof(value));
            }

            _id = value;
        }
    }

    public string DisplayName
    {
        get => _displayName;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Profile display name cannot be blank.", nameof(value));
            }

            _displayName = value.Trim();
        }
    }

    public bool Enabled { get; init; }

    public ProfileRole Roles
    {
        get => _roles;
        init
        {
            if (value == ProfileRole.None)
            {
                throw new ArgumentException("Profile must have at least one role.", nameof(value));
            }

            if ((value & ~SupportedRoles) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Profile contains an unsupported role.");
            }

            _roles = value;
        }
    }

    public IReadOnlyList<DeviceBinding> DeviceBindings
    {
        get => _deviceBindings;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Any(static binding => binding is null))
            {
                throw new ArgumentException("Device bindings cannot contain null entries.", nameof(value));
            }

            _deviceBindings = value.ToArray();
        }
    }

    public ViewerScope ViewerScope
    {
        get => _viewerScope;
        init => _viewerScope = value ?? throw new ArgumentNullException(nameof(value));
    }

    public FreshnessPolicy Freshness
    {
        get => _freshness;
        init => _freshness = value ?? throw new ArgumentNullException(nameof(value));
    }

    public ThermalPolicy Thermal
    {
        get => _thermal;
        init => _thermal = value ?? throw new ArgumentNullException(nameof(value));
    }

    public SensorVisibilityPolicy SensorVisibility
    {
        get => _sensorVisibility;
        init => _sensorVisibility = value ?? throw new ArgumentNullException(nameof(value));
    }

    public bool Equals(MonitoringProfile? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return Id == other.Id
            && DisplayName == other.DisplayName
            && Enabled == other.Enabled
            && Roles == other.Roles
            && DeviceBindings.SequenceEqual(other.DeviceBindings)
            && ViewerScope.Mode == other.ViewerScope.Mode
            && ViewerScope.ProfileIds.SequenceEqual(other.ViewerScope.ProfileIds)
            && Freshness == other.Freshness
            && Thermal == other.Thermal
            && SensorVisibility == other.SensorVisibility;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(DisplayName);
        hash.Add(Enabled);
        hash.Add(Roles);
        foreach (var binding in DeviceBindings)
        {
            hash.Add(binding);
        }

        hash.Add(ViewerScope.Mode);
        foreach (var profileId in ViewerScope.ProfileIds)
        {
            hash.Add(profileId);
        }

        hash.Add(Freshness);
        hash.Add(Thermal);
        hash.Add(SensorVisibility);
        return hash.ToHashCode();
    }
}

public sealed record ProfileRegistryDocument
{
    [JsonConstructor]
    public ProfileRegistryDocument(int schemaVersion, IReadOnlyList<MonitoringProfile> profiles)
    {
        if (schemaVersion != ProfileContract.CurrentSchemaVersion)
        {
            throw new NotSupportedException($"Profile schema version {schemaVersion} is not supported.");
        }

        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Any(static profile => profile is null))
        {
            throw new ArgumentException("Profile registry cannot contain null profiles.", nameof(profiles));
        }

        var materialized = profiles.ToArray();
        if (materialized.GroupBy(static profile => profile.Id).Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Profile registry cannot contain duplicate profile IDs.", nameof(profiles));
        }

        SchemaVersion = schemaVersion;
        Profiles = materialized;
    }

    public int SchemaVersion { get; }

    public IReadOnlyList<MonitoringProfile> Profiles { get; }

    public static ProfileRegistryDocument Empty => new(ProfileContract.CurrentSchemaVersion, Array.Empty<MonitoringProfile>());
}

public static class ProfileJsonSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ProfileRegistryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public static ProfileRegistryDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Profile JSON cannot be blank.", nameof(json));
        }

        using (var parsed = JsonDocument.Parse(json))
        {
            if (!parsed.RootElement.TryGetProperty("schemaVersion", out var schemaElement)
                || !schemaElement.TryGetInt32(out var schemaVersion))
            {
                throw new JsonException("Profile JSON is missing a numeric schemaVersion.");
            }

            if (schemaVersion != ProfileContract.CurrentSchemaVersion)
            {
                throw new NotSupportedException($"Profile schema version {schemaVersion} is not supported.");
            }
        }

        return JsonSerializer.Deserialize<ProfileRegistryDocument>(json, Options)
            ?? throw new JsonException("Profile JSON did not contain a registry document.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
