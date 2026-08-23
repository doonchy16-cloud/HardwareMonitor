using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;
using TheSpark.HardwareMonitor.Sensors.Agent;

namespace TheSpark.HardwareMonitor.Sensors.Tests;

public sealed class BackgroundAgentBehaviorTests
{
    [Fact]
    public async Task Missing_profile_registry_starts_empty_and_collects_snapshots()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var profilePath = Path.Combine(tempDirectory, "profiles.json");
            var provider = new SnapshotProvider();
            var service = new HardwareMonitorService(provider, TimeSpan.FromMilliseconds(10));
            await using var agent = new BackgroundHardwareAgent(service, new ProfileRegistryFileStore(profilePath));

            await agent.StartAsync();
            await WaitUntilAsync(() => agent.LatestSnapshot is not null, TimeSpan.FromSeconds(2));

            Assert.Equal(AgentLifecycleState.Running, agent.Health.State);
            Assert.True(agent.Health.SensorEngineRunning);
            Assert.True(agent.Health.ProfileRegistryLoaded);
            Assert.Equal(0, agent.Health.ProfileCount);
            Assert.NotNull(agent.Health.StartedAt);
            Assert.NotNull(agent.Health.LastSnapshotAt);
            Assert.NotNull(agent.LatestSnapshot);
            Assert.False(File.Exists(profilePath));

            await agent.StopAsync();
            Assert.Equal(AgentLifecycleState.Stopped, agent.Health.State);
            Assert.False(agent.Health.SensorEngineRunning);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task Corrupt_profile_registry_faults_without_replacing_or_monitoring()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var profilePath = Path.Combine(tempDirectory, "profiles.json");
            const string corruptJson = "{ this is not valid profile json";
            await File.WriteAllTextAsync(profilePath, corruptJson);
            var provider = new SnapshotProvider();
            var service = new HardwareMonitorService(provider, TimeSpan.FromMilliseconds(10));
            await using var agent = new BackgroundHardwareAgent(service, new ProfileRegistryFileStore(profilePath));

            await agent.StartAsync();

            Assert.Equal(AgentLifecycleState.Faulted, agent.Health.State);
            Assert.False(agent.Health.SensorEngineRunning);
            Assert.False(agent.Health.ProfileRegistryLoaded);
            Assert.Equal(0, provider.ReadCount);
            Assert.Equal(corruptJson, await File.ReadAllTextAsync(profilePath));
            Assert.False(string.IsNullOrWhiteSpace(agent.Health.LastError));
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task Sensor_engine_fault_is_reported_and_sensitive_exception_message_is_not_exposed()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var profilePath = Path.Combine(tempDirectory, "profiles.json");
            var service = new HardwareMonitorService(new ThrowingProvider(), TimeSpan.FromMilliseconds(10));
            await using var agent = new BackgroundHardwareAgent(service, new ProfileRegistryFileStore(profilePath));

            await agent.StartAsync();
            await WaitUntilAsync(() => agent.Health.State == AgentLifecycleState.Faulted, TimeSpan.FromSeconds(2));

            Assert.False(agent.Health.SensorEngineRunning);
            Assert.Equal(nameof(InvalidOperationException), agent.Health.LastError);
            Assert.DoesNotContain("DO_NOT_LEAK_THIS", agent.Health.LastError ?? string.Empty);

            await agent.StopAsync();
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task Sensor_service_reports_provider_failure_and_can_stop_cleanly()
    {
        var service = new HardwareMonitorService(new ThrowingProvider(), TimeSpan.FromMilliseconds(10));
        var faulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Faulted += exception => faulted.TrySetResult(exception);

        await service.StartAsync();
        var exception = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<InvalidOperationException>(exception);
        await WaitUntilAsync(() => !service.IsRunning, TimeSpan.FromSeconds(2));
        await service.StopAsync();
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Named_pipe_client_reads_health_and_restarts_agent()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var profilePath = Path.Combine(tempDirectory, "profiles.json");
            var service = new HardwareMonitorService(new SnapshotProvider(), TimeSpan.FromMilliseconds(10));
            await using var agent = new BackgroundHardwareAgent(service, new ProfileRegistryFileStore(profilePath));
            var pipeName = $"HardwareMonitor.Tests.{Guid.NewGuid():N}";
            await using var server = new AgentIpcServer(agent, pipeName);
            var client = new AgentIpcClient(pipeName, TimeSpan.FromSeconds(2));

            await agent.StartAsync();
            await WaitUntilAsync(() => agent.LatestSnapshot is not null, TimeSpan.FromSeconds(2));
            await server.StartAsync();

            var before = await client.GetHealthAsync();
            Assert.Equal(AgentLifecycleState.Running, before.State);
            Assert.NotNull(before.StartedAt);

            await Task.Delay(20);
            await client.RestartAsync();
            var after = await client.GetHealthAsync();

            Assert.Equal(AgentLifecycleState.Running, after.State);
            Assert.NotNull(after.StartedAt);
            Assert.True(after.StartedAt >= before.StartedAt);

            await server.StopAsync();
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HardwareMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(predicate(), "Condition did not become true before the timeout.");
    }

    private sealed class SnapshotProvider : ISensorProvider
    {
        public int ReadCount { get; private set; }

        public Task<HardwareSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult(HardwareSnapshot.Empty(DateTimeOffset.UtcNow));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingProvider : ISensorProvider
    {
        public Task<HardwareSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<HardwareSnapshot>(new InvalidOperationException("DO_NOT_LEAK_THIS"));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
