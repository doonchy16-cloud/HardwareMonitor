using System.Text.Json.Serialization;

namespace TheSpark.HardwareMonitor.Core.Remote;

public sealed record TelemetryMetricEnvelope
{
    public TelemetryMetricEnvelope(
        string key,
        string label,
        double? numericValue,
        string? textValue,
        string unit,
        string availability)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 120)
        {
            throw new ArgumentException("Metric key must be 1..120 characters.", nameof(key));
        }
        if (string.IsNullOrWhiteSpace(label) || label.Length > 160)
        {
            throw new ArgumentException("Metric label must be 1..160 characters.", nameof(label));
        }
        if (unit.Length > 24)
        {
            throw new ArgumentException("Metric unit must be at most 24 characters.", nameof(unit));
        }
        if (textValue is { Length: > 256 })
        {
            throw new ArgumentException("Metric text value must be at most 256 characters.", nameof(textValue));
        }
        if (availability is not ("Available" or "Stale" or "NotExposed" or "Error"))
        {
            throw new ArgumentException("Metric availability is invalid.", nameof(availability));
        }

        Key = key.Trim();
        Label = label.Trim();
        NumericValue = numericValue;
        TextValue = textValue;
        Unit = unit;
        Availability = availability;
    }

    [JsonPropertyName("key")]
    public string Key { get; }

    [JsonPropertyName("label")]
    public string Label { get; }

    [JsonPropertyName("numeric_value")]
    public double? NumericValue { get; }

    [JsonPropertyName("text_value")]
    public string? TextValue { get; }

    [JsonPropertyName("unit")]
    public string Unit { get; }

    [JsonPropertyName("availability")]
    public string Availability { get; }
}

public sealed record TelemetryEnvelope
{
    public TelemetryEnvelope(
        Guid deviceId,
        Guid profileId,
        DateTimeOffset capturedAt,
        string activity,
        string health,
        IReadOnlyList<TelemetryMetricEnvelope> metrics)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device ID must not be empty.", nameof(deviceId));
        }
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must not be empty.", nameof(profileId));
        }
        if (activity is not ("Unknown" or "Idle" or "Training"))
        {
            throw new ArgumentException("Activity is invalid.", nameof(activity));
        }
        if (health is not ("Healthy" or "Degraded" or "Error"))
        {
            throw new ArgumentException("Health is invalid.", nameof(health));
        }
        ArgumentNullException.ThrowIfNull(metrics);
        if (metrics.Count > 512)
        {
            throw new ArgumentException("Telemetry metrics must contain at most 512 items.", nameof(metrics));
        }

        DeviceId = deviceId;
        ProfileId = profileId;
        CapturedAt = capturedAt;
        Activity = activity;
        Health = health;
        Metrics = metrics.ToArray();
    }

    [JsonPropertyName("schema_version")]
    public string SchemaVersion => "1.0";

    [JsonPropertyName("device_id")]
    public Guid DeviceId { get; }

    [JsonPropertyName("profile_id")]
    public Guid ProfileId { get; }

    [JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; }

    [JsonPropertyName("activity")]
    public string Activity { get; }

    [JsonPropertyName("health")]
    public string Health { get; }

    [JsonPropertyName("metrics")]
    public IReadOnlyList<TelemetryMetricEnvelope> Metrics { get; }
}

public sealed record PresenceEnvelope
{
    public PresenceEnvelope(
        Guid deviceId,
        DateTimeOffset capturedAt,
        string platform,
        string agentVersion,
        string state)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device ID must not be empty.", nameof(deviceId));
        }
        if (string.IsNullOrWhiteSpace(platform) || platform.Length > 80)
        {
            throw new ArgumentException("Platform must be 1..80 characters.", nameof(platform));
        }
        if (string.IsNullOrWhiteSpace(agentVersion) || agentVersion.Length > 80)
        {
            throw new ArgumentException("Agent version must be 1..80 characters.", nameof(agentVersion));
        }
        if (state is not ("ONLINE" or "STALE" or "OFFLINE" or "DEGRADED"))
        {
            throw new ArgumentException("Presence state is invalid.", nameof(state));
        }

        DeviceId = deviceId;
        CapturedAt = capturedAt;
        Platform = platform.Trim();
        AgentVersion = agentVersion.Trim();
        State = state;
    }

    [JsonPropertyName("schema_version")]
    public string SchemaVersion => "1.0";

    [JsonPropertyName("device_id")]
    public Guid DeviceId { get; }

    [JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; }

    [JsonPropertyName("platform")]
    public string Platform { get; }

    [JsonPropertyName("agent_version")]
    public string AgentVersion { get; }

    [JsonPropertyName("state")]
    public string State { get; }
}

public sealed record ProfileRegistryItemEnvelope
{
    public ProfileRegistryItemEnvelope(Guid profileId, string name, bool enabled, long revision)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must not be empty.", nameof(profileId));
        }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
        {
            throw new ArgumentException("Profile name must be 1..120 characters.", nameof(name));
        }
        if (revision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        ProfileId = profileId;
        Name = name.Trim();
        Enabled = enabled;
        Revision = revision;
    }

    [JsonPropertyName("profile_id")]
    public Guid ProfileId { get; }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; }

    [JsonPropertyName("revision")]
    public long Revision { get; }
}

public sealed record ProfileRegistryEnvelope
{
    public ProfileRegistryEnvelope(long registryRevision, IReadOnlyList<ProfileRegistryItemEnvelope> profiles)
    {
        if (registryRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(registryRevision));
        }
        ArgumentNullException.ThrowIfNull(profiles);

        RegistryRevision = registryRevision;
        Profiles = profiles.ToArray();
    }

    [JsonPropertyName("schema_version")]
    public string SchemaVersion => "1.0";

    [JsonPropertyName("registry_revision")]
    public long RegistryRevision { get; }

    [JsonPropertyName("profiles")]
    public IReadOnlyList<ProfileRegistryItemEnvelope> Profiles { get; }
}
