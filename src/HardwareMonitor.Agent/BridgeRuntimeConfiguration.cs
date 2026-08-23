using System.Text.Json;

namespace TheSpark.HardwareMonitor.Agent;

public enum BridgeRuntimeRole
{
    Host,
    Controller
}

public sealed class BridgeRuntimeConfiguration
{
    private const string TransportSchemaVersion = "2.0";
    private readonly string _transportPath;

    private BridgeRuntimeConfiguration(
        string bridgeRoot,
        string transportPath,
        Guid deviceId,
        Uri gatewayBaseUri,
        BridgeRuntimeRole role,
        string profilePath)
    {
        BridgeRoot = bridgeRoot;
        _transportPath = transportPath;
        DeviceId = deviceId;
        GatewayBaseUri = gatewayBaseUri;
        Role = role;
        ProfilePath = profilePath;
    }

    public string BridgeRoot { get; }
    public Guid DeviceId { get; }
    public Uri GatewayBaseUri { get; }
    public BridgeRuntimeRole Role { get; }
    public string ProfilePath { get; }

    public static BridgeRuntimeConfiguration Load(string bridgeRoot)
    {
        if (string.IsNullOrWhiteSpace(bridgeRoot))
        {
            throw new ArgumentException("Bridge root must not be empty.", nameof(bridgeRoot));
        }

        var root = Path.GetFullPath(bridgeRoot);
        var identityPath = Path.Combine(root, "config", "identity.json");
        var transportPath = Path.Combine(root, "config", "transport-v2.local.json");
        if (!File.Exists(identityPath))
        {
            throw new FileNotFoundException("Bridge identity file is missing.", identityPath);
        }
        if (!File.Exists(transportPath))
        {
            throw new FileNotFoundException("Bridge Transport V2 configuration is missing.", transportPath);
        }

        using var identityDocument = JsonDocument.Parse(File.ReadAllText(identityPath));
        if (!identityDocument.RootElement.TryGetProperty("device_id", out var deviceElement) ||
            !Guid.TryParse(deviceElement.GetString(), out var deviceId) ||
            deviceId == Guid.Empty)
        {
            throw new InvalidDataException("Bridge identity device_id is invalid.");
        }

        using var transportDocument = JsonDocument.Parse(File.ReadAllText(transportPath));
        var transport = transportDocument.RootElement;
        if (!transport.TryGetProperty("schema_version", out var schemaElement) ||
            !string.Equals(schemaElement.GetString(), TransportSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Bridge Transport V2 schema is unsupported.");
        }
        if (!transport.TryGetProperty("gateway_url", out var gatewayElement) ||
            !Uri.TryCreate(gatewayElement.GetString(), UriKind.Absolute, out var gatewayBaseUri) ||
            gatewayBaseUri.Scheme is not ("https" or "http"))
        {
            throw new InvalidDataException("Bridge gateway_url is invalid.");
        }

        var hostToken = ReadOptionalString(transport, "host_token");
        var controllerToken = ReadOptionalString(transport, "controller_token");
        var role = !string.IsNullOrWhiteSpace(hostToken)
            ? BridgeRuntimeRole.Host
            : !string.IsNullOrWhiteSpace(controllerToken)
                ? BridgeRuntimeRole.Controller
                : throw new InvalidDataException("Bridge transport contains no usable host or controller credential.");
        var selected = role == BridgeRuntimeRole.Host ? hostToken : controllerToken;
        if (selected!.Length < 24)
        {
            throw new InvalidDataException("Bridge transport credential is too short.");
        }

        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "The Spark",
            "Hardware Monitor",
            "profiles.json");

        return new BridgeRuntimeConfiguration(
            root,
            transportPath,
            deviceId,
            EnsureTrailingSlash(gatewayBaseUri),
            role,
            profilePath);
    }

    public ValueTask<string> ReadCredentialAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(File.ReadAllText(_transportPath));
        var propertyName = Role == BridgeRuntimeRole.Host ? "host_token" : "controller_token";
        var credential = ReadOptionalString(document.RootElement, propertyName);
        if (string.IsNullOrWhiteSpace(credential) || credential.Length < 24)
        {
            throw new InvalidDataException($"Bridge {propertyName} is missing or invalid.");
        }
        return ValueTask.FromResult(credential);
    }

    public override string ToString() =>
        $"BridgeRuntimeConfiguration {{ DeviceId = {DeviceId}, GatewayBaseUri = {GatewayBaseUri}, Role = {Role}, ProfilePath = {ProfilePath} }}";

    private static string? ReadOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/', StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
}
