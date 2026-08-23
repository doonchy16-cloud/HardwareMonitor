using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;

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
    public BridgeGatewayTelemetryPublisher(
        string bridgeRoot,
        string telemetrySequencePath,
        ProfileRegistryFileStore profileStore,
        HttpMessageHandler httpMessageHandler)
    {
        _ = bridgeRoot;
        _ = telemetrySequencePath;
        _ = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _ = httpMessageHandler ?? throw new ArgumentNullException(nameof(httpMessageHandler));
    }

    public BridgeGatewayTelemetryPublisherStatus Status { get; private set; } =
        new(false, null, null, null, 0, 0);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public void Queue(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
    }

    public Task<bool> PublishAsync(HardwareSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
