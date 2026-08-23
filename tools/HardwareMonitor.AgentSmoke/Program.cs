using TheSpark.HardwareMonitor.Sensors.Agent;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: HardwareMonitor.AgentSmoke <pipe-name>");
    return 2;
}

var pipeName = args[0].Trim();
var client = new AgentIpcClient(pipeName, TimeSpan.FromSeconds(2));

try
{
    var before = await WaitForHealthyAsync(client, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    if (!before.ProfileRegistryLoaded || before.ProfileCount != 0)
    {
        Console.Error.WriteLine($"Unexpected profile state: loaded={before.ProfileRegistryLoaded}, count={before.ProfileCount}.");
        return 3;
    }

    var startedBefore = before.StartedAt;
    await client.RestartAsync().ConfigureAwait(false);
    var after = await WaitForHealthyAsync(client, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

    if (startedBefore.HasValue && after.StartedAt.HasValue && after.StartedAt < startedBefore)
    {
        Console.Error.WriteLine("Agent restart moved StartedAt backwards.");
        return 4;
    }

    Console.WriteLine($"AGENT_STATE={after.State}");
    Console.WriteLine($"AGENT_SENSOR_ENGINE_RUNNING={after.SensorEngineRunning}");
    Console.WriteLine($"AGENT_PROFILE_REGISTRY_LOADED={after.ProfileRegistryLoaded}");
    Console.WriteLine($"AGENT_PROFILE_COUNT={after.ProfileCount}");
    Console.WriteLine($"AGENT_LAST_SNAPSHOT_AT={after.LastSnapshotAt:O}");
    Console.WriteLine("AGENT_RESTARTED=1");
    return 0;
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    Console.Error.WriteLine($"AGENT_SMOKE_ERROR={ex.GetType().Name}");
    return 5;
}

static async Task<AgentHealthSnapshot> WaitForHealthyAsync(AgentIpcClient client, TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    Exception? lastException = null;

    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            var health = await client.GetHealthAsync().ConfigureAwait(false);
            if (health.State == AgentLifecycleState.Faulted)
            {
                throw new InvalidOperationException(health.LastError ?? "AgentFaulted");
            }

            if (health.State == AgentLifecycleState.Running
                && health.SensorEngineRunning
                && health.ProfileRegistryLoaded
                && health.LastSnapshotAt.HasValue)
            {
                return health;
            }
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
        {
            lastException = ex;
        }

        await Task.Delay(100).ConfigureAwait(false);
    }

    throw new TimeoutException("Standalone Hardware Monitor agent did not become healthy before the deadline.", lastException);
}
