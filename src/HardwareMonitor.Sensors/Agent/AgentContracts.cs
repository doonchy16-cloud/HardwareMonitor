using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Sensors.Agent;

public enum AgentLifecycleState
{
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
}

public sealed record AgentHealthSnapshot(
    AgentLifecycleState State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastSnapshotAt,
    string? LastError,
    bool SensorEngineRunning,
    bool ProfileRegistryLoaded,
    int ProfileCount);

public sealed class BackgroundHardwareAgent : IAsyncDisposable
{
    private readonly HardwareMonitorService _monitorService;
    private readonly ProfileRegistryFileStore _profileStore;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _stateGate = new();
    private AgentHealthSnapshot _health = new(
        AgentLifecycleState.Stopped,
        StartedAt: null,
        LastSnapshotAt: null,
        LastError: null,
        SensorEngineRunning: false,
        ProfileRegistryLoaded: false,
        ProfileCount: 0);
    private HardwareSnapshot? _latestSnapshot;
    private bool _disposed;

    public BackgroundHardwareAgent(HardwareMonitorService monitorService, ProfileRegistryFileStore profileStore)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _monitorService.SnapshotUpdated += MonitorService_SnapshotUpdated;
        _monitorService.Faulted += MonitorService_Faulted;
    }

    public AgentHealthSnapshot Health
    {
        get
        {
            lock (_stateGate)
            {
                return _health;
            }
        }
    }

    public HardwareSnapshot? LatestSnapshot
    {
        get
        {
            lock (_stateGate)
            {
                return _latestSnapshot;
            }
        }
    }

    public async Task StartAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var state = Health.State;
            if (state is AgentLifecycleState.Starting or AgentLifecycleState.Running)
            {
                return;
            }

            lock (_stateGate)
            {
                _latestSnapshot = null;
                _health = new AgentHealthSnapshot(
                    AgentLifecycleState.Starting,
                    StartedAt: null,
                    LastSnapshotAt: null,
                    LastError: null,
                    SensorEngineRunning: false,
                    ProfileRegistryLoaded: false,
                    ProfileCount: 0);
            }

            ProfileRegistryDocument registry;
            try
            {
                registry = await _profileStore.LoadAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SetFaulted(ex);
                return;
            }

            lock (_stateGate)
            {
                _health = _health with
                {
                    State = AgentLifecycleState.Running,
                    StartedAt = DateTimeOffset.UtcNow,
                    SensorEngineRunning = true,
                    ProfileRegistryLoaded = true,
                    ProfileCount = registry.Profiles.Count,
                };
            }

            try
            {
                await _monitorService.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SetFaulted(ex);
                return;
            }

            lock (_stateGate)
            {
                if (_health.State != AgentLifecycleState.Faulted)
                {
                    _health = _health with { SensorEngineRunning = _monitorService.IsRunning };
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_health.State == AgentLifecycleState.Stopped)
            {
                return;
            }

            lock (_stateGate)
            {
                _health = _health with
                {
                    State = AgentLifecycleState.Stopping,
                    SensorEngineRunning = _monitorService.IsRunning,
                };
            }

            try
            {
                await _monitorService.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SetFaulted(ex);
                return;
            }

            lock (_stateGate)
            {
                _health = _health with
                {
                    State = AgentLifecycleState.Stopped,
                    SensorEngineRunning = false,
                };
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task RestartAsync()
    {
        ThrowIfDisposed();
        await StopAsync().ConfigureAwait(false);
        await StartAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _monitorService.SnapshotUpdated -= MonitorService_SnapshotUpdated;
        _monitorService.Faulted -= MonitorService_Faulted;
        await _monitorService.DisposeAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private void MonitorService_SnapshotUpdated(HardwareSnapshot snapshot)
    {
        lock (_stateGate)
        {
            _latestSnapshot = snapshot;
            _health = _health with
            {
                LastSnapshotAt = snapshot.CapturedAt,
                SensorEngineRunning = true,
            };
        }
    }

    private void MonitorService_Faulted(Exception exception) => SetFaulted(exception);

    private void SetFaulted(Exception exception)
    {
        lock (_stateGate)
        {
            _health = _health with
            {
                State = AgentLifecycleState.Faulted,
                LastError = exception.GetType().Name,
                SensorEngineRunning = false,
            };
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BackgroundHardwareAgent));
        }
    }
}

public static class AgentIpcProtocol
{
    public const int CurrentVersion = 1;
    public const string DefaultPipeName = "TheSpark.HardwareMonitor.Agent.v1";
}

public sealed record AgentIpcRequest(int Version, string Command);

public sealed record AgentIpcResponse(int Version, bool Success, AgentHealthSnapshot? Health, string? Error);

public sealed class AgentIpcServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly BackgroundHardwareAgent _agent;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public AgentIpcServer(BackgroundHardwareAgent agent, string pipeName)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName.Trim();
    }

    public Task StartAsync()
    {
        if (_serverTask is { IsCompleted: false })
        {
            return Task.CompletedTask;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _serverTask = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var task = _serverTask;
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
            _serverTask = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            return;
        }

        AgentIpcResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<AgentIpcRequest>(line, JsonOptions);
            response = request is null
                ? Failure("InvalidRequest")
                : await ExecuteAsync(request).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            response = Failure("InvalidRequest");
        }

        var json = JsonSerializer.Serialize(response, JsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgentIpcResponse> ExecuteAsync(AgentIpcRequest request)
    {
        if (request.Version != AgentIpcProtocol.CurrentVersion)
        {
            return Failure("UnsupportedProtocolVersion");
        }

        if (string.Equals(request.Command, "health", StringComparison.OrdinalIgnoreCase))
        {
            return Success(_agent.Health);
        }

        if (string.Equals(request.Command, "restart", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _agent.RestartAsync().ConfigureAwait(false);
                return Success(_agent.Health);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Failure(ex.GetType().Name);
            }
        }

        return Failure("UnknownCommand");
    }

    private static AgentIpcResponse Success(AgentHealthSnapshot health) =>
        new(AgentIpcProtocol.CurrentVersion, true, health, null);

    private static AgentIpcResponse Failure(string error) =>
        new(AgentIpcProtocol.CurrentVersion, false, null, error);
}

public sealed class AgentIpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pipeName;
    private readonly TimeSpan _timeout;

    public AgentIpcClient(string pipeName, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _pipeName = pipeName.Trim();
        _timeout = timeout;
    }

    public async Task<AgentHealthSnapshot> GetHealthAsync()
    {
        var response = await SendAsync("health").ConfigureAwait(false);
        if (!response.Success || response.Health is null)
        {
            throw new InvalidOperationException(response.Error ?? "AgentHealthUnavailable");
        }

        return response.Health;
    }

    public async Task RestartAsync()
    {
        var response = await SendAsync("restart").ConfigureAwait(false);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "AgentRestartFailed");
        }
    }

    private async Task<AgentIpcResponse> SendAsync(string command)
    {
        using var timeout = new CancellationTokenSource(_timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };

            var request = new AgentIpcRequest(AgentIpcProtocol.CurrentVersion, command);
            var json = JsonSerializer.Serialize(request, JsonOptions);
            await writer.WriteLineAsync(json.AsMemory(), timeout.Token).ConfigureAwait(false);

            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false)
                ?? throw new IOException("Agent IPC closed before returning a response.");
            var response = JsonSerializer.Deserialize<AgentIpcResponse>(line, JsonOptions)
                ?? throw new IOException("Agent IPC returned an empty response.");

            if (response.Version != AgentIpcProtocol.CurrentVersion)
            {
                throw new NotSupportedException("Agent IPC protocol version mismatch.");
            }

            return response;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for the Hardware Monitor background agent.");
        }
    }
}
