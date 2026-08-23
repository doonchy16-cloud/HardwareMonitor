using System.Diagnostics;
using System.IO;
using TheSpark.HardwareMonitor.Sensors.Agent;

namespace TheSpark.HardwareMonitor.App.Services;

public sealed class BackgroundAgentController
{
    private readonly string _executablePath;
    private readonly string _pipeName;
    private readonly TimeSpan _ipcTimeout;
    private readonly TimeSpan _startupTimeout;

    public BackgroundAgentController(
        string executablePath,
        string pipeName,
        TimeSpan? ipcTimeout = null,
        TimeSpan? startupTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Background agent executable path cannot be blank.", nameof(executablePath));
        }

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Background agent pipe name cannot be blank.", nameof(pipeName));
        }

        _executablePath = Path.GetFullPath(executablePath.Trim());
        _pipeName = pipeName.Trim();
        _ipcTimeout = ipcTimeout ?? TimeSpan.FromMilliseconds(750);
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(8);

        if (_ipcTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ipcTimeout));
        }

        if (_startupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startupTimeout));
        }
    }

    public async Task<AgentHealthSnapshot?> GetHealthAsync()
    {
        try
        {
            return await CreateClient().GetHealthAsync().ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task<AgentHealthSnapshot> EnsureRunningAsync(TimeSpan pollInterval)
    {
        var health = await GetHealthAsync().ConfigureAwait(false);
        if (health is not null)
        {
            if (health.State is AgentLifecycleState.Running or AgentLifecycleState.Faulted)
            {
                return health;
            }

            if (health.State == AgentLifecycleState.Starting)
            {
                return await WaitForTerminalStartupStateAsync().ConfigureAwait(false);
            }

            await CreateClient().RestartAsync().ConfigureAwait(false);
            return await WaitForTerminalStartupStateAsync().ConfigureAwait(false);
        }

        if (!File.Exists(_executablePath))
        {
            throw new FileNotFoundException("The packaged Hardware Monitor background agent is unavailable.");
        }

        var launchPlan = AgentProcessLaunchPlan.Create(_executablePath, _pipeName, pollInterval);
        using var process = Process.Start(launchPlan.CreateStartInfo())
            ?? throw new InvalidOperationException("Windows did not start the Hardware Monitor background agent.");

        return await WaitForTerminalStartupStateAsync().ConfigureAwait(false);
    }

    public async Task<AgentHealthSnapshot> RestartOrStartAsync(TimeSpan pollInterval)
    {
        var health = await GetHealthAsync().ConfigureAwait(false);
        if (health is null)
        {
            return await EnsureRunningAsync(pollInterval).ConfigureAwait(false);
        }

        await CreateClient().RestartAsync().ConfigureAwait(false);
        return await WaitForTerminalStartupStateAsync().ConfigureAwait(false);
    }

    private AgentIpcClient CreateClient() => new(_pipeName, _ipcTimeout);

    private async Task<AgentHealthSnapshot> WaitForTerminalStartupStateAsync()
    {
        var deadline = DateTimeOffset.UtcNow + _startupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var health = await GetHealthAsync().ConfigureAwait(false);
            if (health is not null && health.State is AgentLifecycleState.Running or AgentLifecycleState.Faulted)
            {
                return health;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new TimeoutException("The Hardware Monitor background agent did not become reachable before the startup deadline.");
    }
}