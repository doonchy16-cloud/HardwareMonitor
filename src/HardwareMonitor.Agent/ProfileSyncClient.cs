using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Agent;

public sealed class ProfileSyncClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly IProfileRepository _repository;
    private readonly Func<CancellationToken, ValueTask<string>> _credentialProvider;

    public ProfileSyncClient(
        HttpClient httpClient,
        Uri baseUri,
        IProfileRepository repository,
        Func<CancellationToken, ValueTask<string>> credentialProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(credentialProvider);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("Gateway base URI must be absolute HTTP(S).", nameof(baseUri));
        }

        _httpClient = httpClient;
        _baseUri = baseUri;
        _repository = repository;
        _credentialProvider = credentialProvider;
    }

    public async Task<bool> SyncOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var credential = await _credentialProvider(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(credential))
            {
                return false;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(_baseUri, "v2/hardware-monitor/host/profiles"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var remote = await JsonSerializer.DeserializeAsync<HostProfileRegistryDto>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (remote is null)
            {
                return false;
            }

            var snapshot = ValidateAndConvert(remote);
            await _repository.SaveAsync(snapshot).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static ProfileRegistrySnapshot ValidateAndConvert(HostProfileRegistryDto remote)
    {
        if (remote.RegistryRevision < 0 || remote.Profiles is null || remote.Profiles.Count > 1024)
        {
            throw new InvalidDataException("Remote profile registry is invalid.");
        }

        var profiles = new List<HardwareProfile>(remote.Profiles.Count);
        var seen = new HashSet<Guid>();
        foreach (var item in remote.Profiles)
        {
            if (!string.Equals(item.SchemaVersion, "1.0", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unsupported remote profile schema.");
            }
            if (!Guid.TryParse(item.ProfileId, out var profileId) || profileId == Guid.Empty || !seen.Add(profileId))
            {
                throw new InvalidDataException("Remote profile ID is invalid or duplicated.");
            }
            if (!Guid.TryParse(item.DeviceId, out var deviceId) || deviceId == Guid.Empty)
            {
                throw new InvalidDataException("Host-scoped remote profile must have a valid device ID.");
            }
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 120 || item.Revision < 1)
            {
                throw new InvalidDataException("Remote profile metadata is invalid.");
            }
            if (item.Capabilities is null || item.Capabilities.Count > 64 ||
                item.VisibleProfileIds is null || item.VisibleProfileIds.Count > 1024 ||
                item.Freshness is null)
            {
                throw new InvalidDataException("Remote profile shape is invalid.");
            }

            var capabilities = new HashSet<ProfileCapability>();
            foreach (var value in item.Capabilities)
            {
                if (!Enum.TryParse<ProfileCapability>(value, ignoreCase: false, out var capability) ||
                    !Enum.IsDefined(capability) || !capabilities.Add(capability))
                {
                    throw new InvalidDataException("Remote profile capability is invalid or duplicated.");
                }
            }

            if (!Enum.TryParse<ViewerScope>(item.ViewerScope, ignoreCase: false, out var viewerScope) ||
                !Enum.IsDefined(viewerScope))
            {
                throw new InvalidDataException("Remote viewer scope is invalid.");
            }

            var visibleProfileIds = new HashSet<Guid>();
            foreach (var value in item.VisibleProfileIds)
            {
                if (!Guid.TryParse(value, out var visibleId) || visibleId == Guid.Empty || !visibleProfileIds.Add(visibleId))
                {
                    throw new InvalidDataException("Remote visible profile ID is invalid or duplicated.");
                }
            }

            var freshness = new FreshnessPolicy(
                TimeSpan.FromSeconds(item.Freshness.StaleAfterSeconds),
                TimeSpan.FromSeconds(item.Freshness.OfflineAfterSeconds));
            var thermal = item.Thermal is null
                ? ThermalThresholdPolicy.Default
                : new ThermalThresholdPolicy(
                    item.Thermal.WarmCelsius,
                    item.Thermal.HotCelsius,
                    item.Thermal.CriticalCelsius);

            profiles.Add(new HardwareProfile(
                profileId,
                item.Name,
                deviceId,
                capabilities,
                viewerScope,
                visibleProfileIds,
                freshness,
                item.Enabled,
                item.Revision,
                thermalThresholdPolicy: thermal));
        }

        return new ProfileRegistrySnapshot(remote.RegistryRevision, profiles);
    }

    private sealed class HostProfileRegistryDto
    {
        [JsonPropertyName("registry_revision")]
        public long RegistryRevision { get; set; }

        [JsonPropertyName("profiles")]
        public List<HostProfileDto>? Profiles { get; set; }
    }

    private sealed class HostProfileDto
    {
        [JsonPropertyName("schema_version")]
        public string? SchemaVersion { get; set; }

        [JsonPropertyName("profile_id")]
        public string? ProfileId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("device_id")]
        public string? DeviceId { get; set; }

        [JsonPropertyName("capabilities")]
        public List<string>? Capabilities { get; set; }

        [JsonPropertyName("viewer_scope")]
        public string? ViewerScope { get; set; }

        [JsonPropertyName("visible_profile_ids")]
        public List<string>? VisibleProfileIds { get; set; }

        [JsonPropertyName("freshness")]
        public FreshnessDto? Freshness { get; set; }

        [JsonPropertyName("thermal")]
        public ThermalDto? Thermal { get; set; }

        [JsonPropertyName("revision")]
        public long Revision { get; set; }
    }

    private sealed class FreshnessDto
    {
        [JsonPropertyName("stale_after_seconds")]
        public double StaleAfterSeconds { get; set; }

        [JsonPropertyName("offline_after_seconds")]
        public double OfflineAfterSeconds { get; set; }
    }

    private sealed class ThermalDto
    {
        [JsonPropertyName("warm_celsius")]
        public double WarmCelsius { get; set; }

        [JsonPropertyName("hot_celsius")]
        public double HotCelsius { get; set; }

        [JsonPropertyName("critical_celsius")]
        public double CriticalCelsius { get; set; }
    }
}
