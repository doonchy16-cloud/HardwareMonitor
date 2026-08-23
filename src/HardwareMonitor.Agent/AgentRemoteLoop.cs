using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Agent;

public sealed class AgentRemoteLoop
{
    private readonly Func<HardwareSnapshot?> _latestSnapshot;
    private readonly Func<CancellationToken, Task<bool>> _syncOnce;
    private readonly Func<HardwareSnapshot, CancellationToken, Task<AgentRuntimeCycleResult>> _processSnapshot;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _syncInterval;

    public AgentRemoteLoop(
        Func<HardwareSnapshot?> latestSnapshot,
        Func<CancellationToken, Task<bool>> syncOnce,
        Func<HardwareSnapshot, CancellationToken, Task<AgentRuntimeCycleResult>> processSnapshot,
        TimeSpan pollInterval,
        TimeSpan syncInterval)
    {
        ArgumentNullException.ThrowIfNull(latestSnapshot);
        ArgumentNullException.ThrowIfNull(syncOnce);
        ArgumentNullException.ThrowIfNull(processSnapshot);
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }
        if (syncInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(syncInterval));
        }

        _latestSnapshot = latestSnapshot;
        _syncOnce = syncOnce;
        _processSnapshot = processSnapshot;
        _pollInterval = pollInterval;
        _syncInterval = syncInterval;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        HardwareSnapshot? lastProcessed = null;
        var nextSyncAt = DateTimeOffset.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextSyncAt)
            {
                try
                {
                    await _syncOnce(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Remote configuration failure must never stop local monitoring.
                }
                nextSyncAt = now + _syncInterval;
            }

            var snapshot = _latestSnapshot();
            if (snapshot is not null && !ReferenceEquals(snapshot, lastProcessed))
            {
                try
                {
                    await _processSnapshot(snapshot, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Publishing/processing failure is contained; the next physical
                    // sensor frame remains eligible for processing.
                }
                finally
                {
                    lastProcessed = snapshot;
                }
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
}
