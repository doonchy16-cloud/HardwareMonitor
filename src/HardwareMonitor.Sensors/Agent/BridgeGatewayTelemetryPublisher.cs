using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Core.Profiles.Telemetry;

namespace TheSpark.HardwareMonitor.Sensors.Agent;

public sealed record BridgeGatewayTelemetryPublisherStatus(
    bool Enabled,
    DateTimeOffset? LastSuccessAt,
    string? LastErrorCode,
    long? LastAcceptedSequence,
    int LastProfileCount,
    int LastSensorCount);

public sealed class BridgeGatewayTelemetryPublisher : IAsyncDisposable
{
    private const string TelemetrySchemaVersion = "hardware-monitor.telemetry.v1";
    private const string SequenceSchemaVersion = "1.0";
    private const string TelemetryPath = "/v2/host/hardware-monitor/telemetry";

    private readonly string _telemetrySequencePath;
    private readonly ProfileRegistryFileStore _profileStore;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly Channel<HardwareSnapshot> _queue = Channel.CreateBounded<HardwareSnapshot>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly object _lifecycleLock = new();
    private readonly Uri? _gatewayBaseUri;
    private readonly string? _hostToken;
    private readonly string? _deviceId;
    private CancellationTokenSource? _workerCancellation;
    private Task? _workerTask;
    private bool _disposed;

    public BridgeGatewayTelemetryPublisher(
        string bridgeRoot,
        string telemetrySequencePath,
        ProfileRegistryFileStore profileStore,
        HttpMessageHandler httpMessageHandler)
    {
        if (string.IsNullOrWhiteSpace(bridgeRoot))
        {
            throw new ArgumentException("Bridge root cannot be blank.", nameof(bridgeRoot));
        }

        if (string.IsNullOrWhiteSpace(telemetrySequencePath))
        {
            throw new ArgumentException("Telemetry sequence path cannot be blank.", nameof(telemetrySequencePath));
        }

        _telemetrySequencePath = Path.GetFullPath(telemetrySequencePath.Trim());
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        ArgumentNullException.ThrowIfNull(httpMessageHandler);
        _httpClient = new HttpClient(httpMessageHandler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(3),
        };

        try
        {
            var connection = LoadBridgeConnection(Path.GetFullPath(bridgeRoot.Trim()));
            _gatewayBaseUri = connection.GatewayBaseUri;
            _hostToken = connection.HostToken;
            _deviceId = connection.DeviceId;
            Status = new BridgeGatewayTelemetryPublisherStatus(true, null, null, null, 0, 0);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Status = new BridgeGatewayTelemetryPublisherStatus(false, null, ex.GetType().Name, null, 0, 0);
        }
    }

    public BridgeGatewayTelemetryPublisherStatus Status { get; private set; } =
        new(false, null, null, null, 0, 0);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        lock (_lifecycleLock)
        {
            if (_workerTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _workerCancellation?.Dispose();
            _workerCancellation = new CancellationTokenSource();
            _workerTask = Task.Run(() => WorkerAsync(_workerCancellation.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public void Queue(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ThrowIfDisposed();
        if (!Status.Enabled)
        {
            return;
        }

        _queue.Writer.TryWrite(snapshot);
    }

    public async Task<bool> PublishAsync(HardwareSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (!Status.Enabled || _gatewayBaseUri is null || _hostToken is null || _deviceId is null)
        {
            return false;
        }

        await _publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var registry = await _profileStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var profiles = ProfileTelemetryRouter.Route(_deviceId, snapshot, registry);
            if (profiles.Count == 0)
            {
                return false;
            }

            var sequence = await ReserveNextSequenceAsync(cancellationToken).ConfigureAwait(false);
            var body = BuildPayload(_deviceId, sequence, profiles);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_gatewayBaseUri, TelemetryPath));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _hostToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Status = Status with { LastErrorCode = $"HttpStatus{(int)response.StatusCode}" };
                    return false;
                }

                var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var responseJson = JsonDocument.Parse(responseText);
                if (!responseJson.RootElement.TryGetProperty("sequence", out var acceptedSequenceElement)
                    || !acceptedSequenceElement.TryGetInt64(out var acceptedSequence)
                    || acceptedSequence != sequence)
                {
                    throw new InvalidDataException("Gateway telemetry response did not acknowledge the reserved sequence.");
                }

                var sensorCount = profiles.Sum(static profile =>
                    profile.Devices.Sum(static device => device.Sensors.Count));
                Status = new BridgeGatewayTelemetryPublisherStatus(
                    true,
                    DateTimeOffset.UtcNow,
                    null,
                    acceptedSequence,
                    profiles.Count,
                    sensorCount);
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
            {
                Status = Status with { LastErrorCode = ex.GetType().Name };
                return false;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
        {
            Status = Status with { LastErrorCode = ex.GetType().Name };
            return false;
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.Writer.TryComplete();

        Task? worker;
        CancellationTokenSource? cancellation;
        lock (_lifecycleLock)
        {
            worker = _workerTask;
            cancellation = _workerCancellation;
            _workerTask = null;
            _workerCancellation = null;
        }

        if (cancellation is not null)
        {
            cancellation.Cancel();
        }

        if (worker is not null)
        {
            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
        _publishLock.Dispose();
        _httpClient.Dispose();
    }

    private async Task WorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var snapshot in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await PublishAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<long> ReserveNextSequenceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long current = 0;
        if (File.Exists(_telemetrySequencePath))
        {
            var existing = await File.ReadAllTextAsync(_telemetrySequencePath, cancellationToken).ConfigureAwait(false);
            using var json = JsonDocument.Parse(existing);
            if (!json.RootElement.TryGetProperty("schema_version", out var schemaVersion)
                || schemaVersion.GetString() != SequenceSchemaVersion
                || !json.RootElement.TryGetProperty("last_sequence", out var lastSequence)
                || !lastSequence.TryGetInt64(out current)
                || current < 0)
            {
                throw new InvalidDataException("Telemetry sequence state is invalid.");
            }
        }

        if (current == long.MaxValue)
        {
            throw new InvalidOperationException("Telemetry sequence is exhausted.");
        }

        var next = checked(current + 1);
        var directory = Path.GetDirectoryName(_telemetrySequencePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempDirectory = string.IsNullOrWhiteSpace(directory)
            ? Directory.GetCurrentDirectory()
            : directory;
        var tempPath = Path.Combine(
            tempDirectory,
            $".{Path.GetFileName(_telemetrySequencePath)}.{Guid.NewGuid():N}.tmp");
        var jsonText = JsonSerializer.Serialize(new
        {
            schema_version = SequenceSchemaVersion,
            last_sequence = next,
        });

        try
        {
            await File.WriteAllTextAsync(tempPath, jsonText, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _telemetrySequencePath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return next;
    }

    private static Dictionary<string, object?> BuildPayload(
        string deviceId,
        long sequence,
        IReadOnlyList<ProfileTelemetrySnapshot> profiles)
    {
        return new Dictionary<string, object?>
        {
            ["schema_version"] = TelemetrySchemaVersion,
            ["device_id"] = deviceId,
            ["sequence"] = sequence,
            ["sent_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["profiles"] = profiles.Select(BuildProfilePayload).ToArray(),
        };
    }

    private static Dictionary<string, object?> BuildProfilePayload(ProfileTelemetrySnapshot profile)
    {
        return new Dictionary<string, object?>
        {
            ["profile_id"] = profile.ProfileId.ToString(),
            ["source_device_id"] = profile.SourceDeviceId,
            ["captured_at"] = profile.CapturedAt.ToString("O"),
            ["engine_status"] = profile.EngineStatus,
            ["freshness"] = new Dictionary<string, object?>
            {
                ["stale_after_ms"] = checked((long)profile.Freshness.StaleAfter.TotalMilliseconds),
                ["offline_after_ms"] = checked((long)profile.Freshness.OfflineAfter.TotalMilliseconds),
            },
            ["devices"] = profile.Devices.Select(BuildDevicePayload).ToArray(),
        };
    }

    private static Dictionary<string, object?> BuildDevicePayload(ProfileHardwareDeviceSnapshot device)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = device.Id,
            ["name"] = device.Name,
            ["kind"] = device.Kind.ToString(),
            ["sensors"] = device.Sensors.Select(BuildSensorPayload).ToArray(),
        };
    }

    private static Dictionary<string, object?> BuildSensorPayload(ProfileSensorReading sensor)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = sensor.Id,
            ["name"] = sensor.Name,
            ["kind"] = sensor.Kind.ToString(),
            ["value"] = sensor.Value,
            ["unit"] = sensor.Unit,
            ["captured_at"] = sensor.CapturedAt.ToString("O"),
            ["availability"] = sensor.Availability.ToString(),
            ["thermal_state"] = sensor.ThermalState.ToString(),
        };
    }

    private static BridgeConnection LoadBridgeConnection(string bridgeRoot)
    {
        var configDirectory = Path.Combine(bridgeRoot, "config");
        var transportPath = Path.Combine(configDirectory, "transport-v2.local.json");
        var identityPath = Path.Combine(configDirectory, "identity.json");

        using var transport = JsonDocument.Parse(File.ReadAllText(transportPath));
        var transportRoot = transport.RootElement;
        if (transportRoot.GetProperty("schema_version").GetString() != "2.0")
        {
            throw new InvalidDataException("Bridge transport schema is unsupported.");
        }

        if (transportRoot.GetProperty("mode").GetString() != "direct")
        {
            throw new InvalidDataException("Bridge transport must be in direct mode.");
        }

        var gatewayUrl = transportRoot.GetProperty("gateway_url").GetString();
        if (!Uri.TryCreate(gatewayUrl, UriKind.Absolute, out var gatewayBaseUri)
            || (gatewayBaseUri.Scheme != Uri.UriSchemeHttps && gatewayBaseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidDataException("Bridge gateway URL is invalid.");
        }

        var hostToken = transportRoot.GetProperty("host_token").GetString();
        if (string.IsNullOrWhiteSpace(hostToken) || hostToken.Length < 24)
        {
            throw new InvalidDataException("Bridge host credential is unavailable.");
        }

        using var identity = JsonDocument.Parse(File.ReadAllText(identityPath));
        var identityRoot = identity.RootElement;
        if (identityRoot.GetProperty("schema_version").GetString() != "1.1")
        {
            throw new InvalidDataException("Bridge identity schema is unsupported.");
        }

        var deviceIdText = identityRoot.GetProperty("device_id").GetString();
        if (!Guid.TryParse(deviceIdText, out var deviceId) || deviceId == Guid.Empty)
        {
            throw new InvalidDataException("Bridge device identity is invalid.");
        }

        return new BridgeConnection(gatewayBaseUri, hostToken, deviceId.ToString());
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record BridgeConnection(Uri GatewayBaseUri, string HostToken, string DeviceId);
}
