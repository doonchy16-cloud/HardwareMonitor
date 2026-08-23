using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Sensors;

namespace TheSpark.HardwareMonitor.Agent;

public sealed class AgentHost : IAsyncDisposable
{
    private readonly ISensorProvider _provider;
    private readonly TimeSpan _pollInterval;
    private int _disposed;
    private int _running;
    private DateTimeOffset? _lastSuccessfulReadAt;
    private int _consecutiveFailures;

    public AgentHost(ISensorProvider provider, TimeSpan? pollInterval = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        var requested = pollInterval ?? TimeSpan.FromSeconds(1);
        _pollInterval = requested <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(250) : requested;
        Health = new AgentHealthSnapshot(AgentHealthState.Stopped, DateTimeOffset.UtcNow, null, 0);
    }

    public event Action<HardwareSnapshot>? SnapshotUpdated;
    public event Action<AgentHealthSnapshot>? HealthChanged;

    public HardwareSnapshot? LatestSnapshot { get; private set; }
    public AgentHealthSnapshot Health { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            throw new InvalidOperationException("Hardware Monitor agent is already running.");
        }

        PublishHealth(AgentHealthState.Starting, null);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var snapshot = await _provider.ReadAsync(cancellationToken).ConfigureAwait(false);
                    LatestSnapshot = snapshot;
                    _lastSuccessfulReadAt = snapshot.CapturedAt;
                    _consecutiveFailures = 0;

                    var state = IsHealthySnapshot(snapshot)
                        ? AgentHealthState.Healthy
                        : AgentHealthState.Degraded;
                    PublishHealth(state, snapshot.ErrorMessage);
                    SnapshotUpdated?.Invoke(snapshot);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _consecutiveFailures++;
                    PublishHealth(AgentHealthState.Error, $"{ex.GetType().Name}: {ex.Message}");
                }

                try
                {
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            PublishHealth(AgentHealthState.Stopped, null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _provider.DisposeAsync().ConfigureAwait(false);
    }

    private static bool IsHealthySnapshot(HardwareSnapshot snapshot) =>
        string.Equals(snapshot.EngineStatus, "Healthy", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(snapshot.ErrorMessage);

    private void PublishHealth(AgentHealthState state, string? errorMessage)
    {
        Health = new AgentHealthSnapshot(
            state,
            DateTimeOffset.UtcNow,
            _lastSuccessfulReadAt,
            _consecutiveFailures,
            errorMessage);
        HealthChanged?.Invoke(Health);
    }
}
