using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Sensors;

public sealed class HardwareMonitorService : IAsyncDisposable
{
    private readonly ISensorProvider _provider;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    public HardwareMonitorService(ISensorProvider provider, TimeSpan? pollInterval = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        PollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    public event Action<HardwareSnapshot>? SnapshotUpdated;

    public TimeSpan PollInterval { get; set; }

    public bool IsRunning => _pollTask is { IsCompleted: false };

    public Task StartAsync()
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _pollTask = PollLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var task = _pollTask;
        if (cts is null || task is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
            _cts = null;
            _pollTask = null;
        }
    }

    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await StartAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _provider.DisposeAsync().ConfigureAwait(false);
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = await _provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            SnapshotUpdated?.Invoke(snapshot);

            var delay = PollInterval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(250) : PollInterval;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
