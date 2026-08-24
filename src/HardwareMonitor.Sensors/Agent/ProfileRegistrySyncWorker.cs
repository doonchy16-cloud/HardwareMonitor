using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Sensors.Agent;

public sealed record ProfileRegistrySyncWorkerStatus(
    bool Running,
    ProfileRegistrySyncStatus Status,
    long LocalRevision,
    long? RemoteRevision,
    DateTimeOffset? LastSuccessfulSyncAt,
    string? ErrorCode);

public sealed class ProfileRegistrySyncWorker : IAsyncDisposable
{
    private readonly ProfileRegistrySyncCoordinator _coordinator;
    private readonly TimeSpan _interval;
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cancellation;
    private Task? _workerTask;
    private bool _disposed;

    public ProfileRegistrySyncWorker(ProfileRegistrySyncCoordinator coordinator, TimeSpan interval)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        if (interval <= TimeSpan.Zero || interval > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }
        _interval = interval;
    }

    public ProfileRegistrySyncWorkerStatus Status { get; private set; } =
        new(false, ProfileRegistrySyncStatus.Stale, 0, null, null, null);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        lock (_lifecycleLock)
        {
            if (_workerTask is { IsCompleted: false }) return Task.CompletedTask;
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            Status = Status with { Running = true };
            _workerTask = Task.Run(() => WorkerAsync(_cancellation.Token), CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    public async Task<ProfileRegistrySyncResult> SynchronizeOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var result = await _coordinator.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        Status = new ProfileRegistrySyncWorkerStatus(
            Status.Running,
            result.Status,
            result.Registry.Revision,
            result.RemoteRevision,
            result.LastSuccessfulSyncAt,
            result.ErrorCode);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Task? worker; CancellationTokenSource? cancellation;
        lock (_lifecycleLock)
        {
            worker = _workerTask; cancellation = _cancellation;
            _workerTask = null; _cancellation = null;
        }
        cancellation?.Cancel();
        if (worker is not null)
        {
            try { await worker.ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true) { }
        }
        cancellation?.Dispose();
        Status = Status with { Running = false };
    }

    private async Task WorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await SynchronizeOnceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Status = Status with { Status = ProfileRegistrySyncStatus.Stale, ErrorCode = ex.GetType().Name };
                }
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Status = Status with { Running = false };
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
