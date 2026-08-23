using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TheSpark.HardwareMonitor.Core.Remote;

namespace TheSpark.HardwareMonitor.Agent;

public sealed class RemoteTelemetryPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly Func<CancellationToken, ValueTask<string>> _credentialProvider;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, TelemetryEnvelope> _pendingTelemetry = [];
    private readonly Dictionary<Guid, PresenceEnvelope> _pendingPresence = [];

    public RemoteTelemetryPublisher(
        HttpClient httpClient,
        Uri baseUri,
        Func<CancellationToken, ValueTask<string>> credentialProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(credentialProvider);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("Gateway base URI must be absolute HTTP(S).", nameof(baseUri));
        }

        _httpClient = httpClient;
        _baseUri = baseUri;
        _credentialProvider = credentialProvider;
    }

    public int PendingTelemetryCount
    {
        get
        {
            lock (_gate)
            {
                return _pendingTelemetry.Count;
            }
        }
    }

    public int PendingPresenceCount
    {
        get
        {
            lock (_gate)
            {
                return _pendingPresence.Count;
            }
        }
    }

    public void QueueTelemetry(TelemetryEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_gate)
        {
            _pendingTelemetry[envelope.ProfileId] = envelope;
        }
    }

    public void QueuePresence(PresenceEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_gate)
        {
            _pendingPresence[envelope.DeviceId] = envelope;
        }
    }

    public async Task<bool> FlushOnceAsync(CancellationToken cancellationToken)
    {
        KeyValuePair<Guid, TelemetryEnvelope>[] telemetry;
        KeyValuePair<Guid, PresenceEnvelope>[] presence;
        lock (_gate)
        {
            telemetry = _pendingTelemetry.ToArray();
            presence = _pendingPresence.ToArray();
        }

        var allSucceeded = true;

        foreach (var item in telemetry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryPostAsync("v2/hardware-monitor/telemetry", item.Value, cancellationToken).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    if (_pendingTelemetry.TryGetValue(item.Key, out var current) && ReferenceEquals(current, item.Value))
                    {
                        _pendingTelemetry.Remove(item.Key);
                    }
                }
            }
            else
            {
                allSucceeded = false;
            }
        }

        foreach (var item in presence)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryPostAsync("v2/hardware-monitor/presence", item.Value, cancellationToken).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    if (_pendingPresence.TryGetValue(item.Key, out var current) && ReferenceEquals(current, item.Value))
                    {
                        _pendingPresence.Remove(item.Key);
                    }
                }
            }
            else
            {
                allSucceeded = false;
            }
        }

        return allSucceeded;
    }

    private async Task<bool> TryPostAsync<T>(string relativePath, T envelope, CancellationToken cancellationToken)
    {
        try
        {
            var credential = await _credentialProvider(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(credential))
            {
                return false;
            }

            var json = JsonSerializer.Serialize(envelope, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, relativePath))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
