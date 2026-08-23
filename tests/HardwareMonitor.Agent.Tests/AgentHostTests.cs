using TheSpark.HardwareMonitor.Agent;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Sensors;
using Xunit;

namespace TheSpark.HardwareMonitor.Agent.Tests;

public sealed class AgentHostTests
{
    [Fact]
    public async Task RunAsyncCollectsSnapshotsWithoutWpfAndStopsCleanly()
    {
        using var cts = new CancellationTokenSource();
        var provider = new DelegateSensorProvider((count, _) =>
        {
            var snapshot = new HardwareSnapshot(
                DateTimeOffset.UtcNow,
                Array.Empty<HardwareDeviceSnapshot>(),
                "Healthy");
            if (count >= 2)
            {
                cts.Cancel();
            }
            return Task.FromResult(snapshot);
        });
        await using var host = new AgentHost(provider, TimeSpan.FromMilliseconds(1));

        await host.RunAsync(cts.Token);

        Assert.True(provider.ReadCount >= 2);
        Assert.NotNull(host.LatestSnapshot);
        Assert.Equal("Healthy", host.LatestSnapshot!.EngineStatus);
        Assert.Equal(AgentHealthState.Stopped, host.Health.State);
        Assert.NotNull(host.Health.LastSuccessfulReadAt);
    }

    [Fact]
    public async Task ProviderFailurePublishesErrorHealthThenRecoversWithoutTerminatingLoop()
    {
        using var cts = new CancellationTokenSource();
        var observed = new List<AgentHealthState>();
        var provider = new DelegateSensorProvider((count, _) =>
        {
            if (count == 1)
            {
                throw new InvalidOperationException("sensor provider boom");
            }

            var snapshot = new HardwareSnapshot(
                DateTimeOffset.UtcNow,
                Array.Empty<HardwareDeviceSnapshot>(),
                "Healthy");
            cts.Cancel();
            return Task.FromResult(snapshot);
        });
        await using var host = new AgentHost(provider, TimeSpan.FromMilliseconds(1));
        host.HealthChanged += value => observed.Add(value.State);

        await host.RunAsync(cts.Token);

        Assert.True(provider.ReadCount >= 2);
        Assert.Contains(AgentHealthState.Error, observed);
        Assert.Contains(AgentHealthState.Healthy, observed);
        Assert.NotNull(host.LatestSnapshot);
        Assert.Equal(0, host.Health.ConsecutiveFailures);
        Assert.Equal(AgentHealthState.Stopped, host.Health.State);
    }

    private sealed class DelegateSensorProvider : ISensorProvider
    {
        private readonly Func<int, CancellationToken, Task<HardwareSnapshot>> _reader;
        private int _readCount;

        public DelegateSensorProvider(Func<int, CancellationToken, Task<HardwareSnapshot>> reader)
        {
            _reader = reader;
        }

        public int ReadCount => Volatile.Read(ref _readCount);

        public Task<HardwareSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _readCount);
            return _reader(count, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
