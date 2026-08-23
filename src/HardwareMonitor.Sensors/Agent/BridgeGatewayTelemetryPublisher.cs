using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Sensors.Agent;

public sealed record BridgeGatewayTelemetryPublisherStatus(
    bool Enabled,
    DateTimeOffset? LastSuccessAt,
    string? LastErrorCode);

public sealed class BridgeGatewayTelemetryPublisher : IAsyncDisposable
{
    public BridgeGatewayTelemetryPublisherStatus Status { get; private set; } =
        new(false, null, null);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public void Queue(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
