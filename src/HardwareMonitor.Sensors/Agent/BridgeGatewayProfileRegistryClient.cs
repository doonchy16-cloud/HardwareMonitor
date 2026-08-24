using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Sensors.Agent;

public sealed class BridgeGatewayProfileRegistryClient : IProfileRegistryAuthorityClient, IDisposable
{
    private const string RegistrySchemaVersion = "hardware-monitor.registry.v1";
    private const string RegistryPath = "/v2/host/hardware-monitor/registry";
    private readonly HttpClient _httpClient;
    private readonly Uri _gatewayBaseUri;
    private readonly string _hostToken;
    private bool _disposed;

    public BridgeGatewayProfileRegistryClient(string bridgeRoot, HttpMessageHandler? httpMessageHandler = null)
    {
        if (string.IsNullOrWhiteSpace(bridgeRoot))
            throw new ArgumentException("Bridge root cannot be blank.", nameof(bridgeRoot));

        var connection = LoadConnection(Path.GetFullPath(bridgeRoot.Trim()));
        _gatewayBaseUri = connection.GatewayBaseUri;
        _hostToken = connection.HostToken;
        _httpClient = httpMessageHandler is null
            ? new HttpClient()
            : new HttpClient(httpMessageHandler, disposeHandler: true);
        _httpClient.Timeout = TimeSpan.FromSeconds(3);
    }

    public async Task<ProfileRegistryDocument> PullAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var request = CreateRequest(HttpMethod.Get);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw Unavailable(response.StatusCode);
        return await ReadRegistryEnvelopeAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProfileRegistryDocument> PushAsync(
        ProfileRegistryDocument registry,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (registry.Revision != expectedRevision)
            throw new ArgumentException("Registry revision must equal expected revision for an optimistic commit.", nameof(registry));
        ThrowIfDisposed();

        using var registryJson = JsonDocument.Parse(ProfileJsonSerializer.Serialize(registry));
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schema_version"] = RegistrySchemaVersion,
            ["expected_revision"] = expectedRevision,
            ["registry"] = registryJson.RootElement.Clone(),
        });
        using var request = CreateRequest(HttpMethod.Post);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var json = JsonDocument.Parse(text);
            if (!json.RootElement.TryGetProperty("current_revision", out var revisionElement)
                || !revisionElement.TryGetInt64(out var remoteRevision)
                || remoteRevision < 0)
                throw new InvalidDataException("Gateway registry conflict response is invalid.");
            throw new ProfileRegistryRevisionConflictException(remoteRevision);
        }
        if (!response.IsSuccessStatusCode)
            throw Unavailable(response.StatusCode);
        return await ReadRegistryEnvelopeAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method)
    {
        var request = new HttpRequestMessage(method, new Uri(_gatewayBaseUri, RegistryPath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _hostToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProfileRegistryAuthorityUnavailableException("Gateway profile registry request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new ProfileRegistryAuthorityUnavailableException("Gateway profile registry request failed.", ex);
        }
    }

    private static async Task<ProfileRegistryDocument> ReadRegistryEnvelopeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        if (!root.TryGetProperty("schema_version", out var schema)
            || schema.GetString() != RegistrySchemaVersion
            || !root.TryGetProperty("registry", out var registry)
            || registry.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Gateway profile registry response schema is invalid.");
        return ProfileJsonSerializer.Deserialize(registry.GetRawText());
    }

    private static ProfileRegistryAuthorityUnavailableException Unavailable(HttpStatusCode statusCode) =>
        new($"Gateway profile registry request failed with HTTP {(int)statusCode}.");

    private static BridgeConnection LoadConnection(string bridgeRoot)
    {
        var config = Path.Combine(bridgeRoot, "config");
        using var transport = JsonDocument.Parse(File.ReadAllText(Path.Combine(config, "transport-v2.local.json")));
        var root = transport.RootElement;
        if (root.GetProperty("schema_version").GetString() != "2.0" || root.GetProperty("mode").GetString() != "direct")
            throw new InvalidDataException("Bridge transport is not a supported direct transport.");
        var gatewayUrl = root.GetProperty("gateway_url").GetString();
        if (!Uri.TryCreate(gatewayUrl, UriKind.Absolute, out var gateway)
            || (gateway.Scheme != Uri.UriSchemeHttps && gateway.Scheme != Uri.UriSchemeHttp))
            throw new InvalidDataException("Bridge gateway URL is invalid.");
        var token = root.GetProperty("host_token").GetString();
        if (string.IsNullOrWhiteSpace(token) || token.Length < 24)
            throw new InvalidDataException("Bridge host credential is unavailable.");

        using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(config, "identity.json")));
        var identityRoot = identity.RootElement;
        if (identityRoot.GetProperty("schema_version").GetString() != "1.1"
            || !Guid.TryParse(identityRoot.GetProperty("device_id").GetString(), out var deviceId)
            || deviceId == Guid.Empty)
            throw new InvalidDataException("Bridge device identity is invalid.");
        return new BridgeConnection(gateway, token);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record BridgeConnection(Uri GatewayBaseUri, string HostToken);
}
