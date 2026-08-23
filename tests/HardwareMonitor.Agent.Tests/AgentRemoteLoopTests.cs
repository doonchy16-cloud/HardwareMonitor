using TheSpark.HardwareMonitor.Agent;
using TheSpark.HardwareMonitor.Core.Models;
using Xunit;

namespace TheSpark.HardwareMonitor.Agent.Tests;

public sealed class AgentRemoteLoopTests
{
    [Fact]
    public async Task ProcessesEachNewSnapshotOnceAndKeepsRunningWhenSyncFails()
    {
        HardwareSnapshot? current = null;
        var processed = new List<HardwareSnapshot>();
        var syncCalls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var loop = new AgentRemoteLoop(
            () => current,
            _ => { syncCalls++; return Task.FromResult(false); },
            (snapshot, _) =>
            {
                processed.Add(snapshot);
                return Task.FromResult(new AgentRuntimeCycleResult(false, Array.Empty<TheSpark.HardwareMonitor.Core.Alerts.AlertEvent>()));
            },
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(30));

        var task = loop.RunAsync(cts.Token);
        current = new HardwareSnapshot(DateTimeOffset.UtcNow, Array.Empty<HardwareDeviceSnapshot>(), "Healthy");
        await Task.Delay(70, TestContext.Current.CancellationToken);
        var first = current;
        await Task.Delay(50, TestContext.Current.CancellationToken);
        current = new HardwareSnapshot(DateTimeOffset.UtcNow.AddSeconds(1), Array.Empty<HardwareDeviceSnapshot>(), "Healthy");
        await Task.Delay(70, TestContext.Current.CancellationToken);
        cts.Cancel();
        await task;

        Assert.Equal(2, processed.Count);
        Assert.Same(first, processed[0]);
        Assert.True(syncCalls >= 2);
    }

    [Fact]
    public async Task ProcessingExceptionIsContainedAndLaterSnapshotStillProcesses()
    {
        HardwareSnapshot? current = new HardwareSnapshot(DateTimeOffset.UtcNow, Array.Empty<HardwareDeviceSnapshot>(), "Healthy");
        var calls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var loop = new AgentRemoteLoop(
            () => current,
            _ => Task.FromResult(true),
            (snapshot, _) =>
            {
                calls++;
                if (calls == 1) throw new HttpRequestException("simulated Gateway failure");
                return Task.FromResult(new AgentRuntimeCycleResult(true, Array.Empty<TheSpark.HardwareMonitor.Core.Alerts.AlertEvent>()));
            },
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(1));

        var task = loop.RunAsync(cts.Token);
        await Task.Delay(60, TestContext.Current.CancellationToken);
        current = new HardwareSnapshot(DateTimeOffset.UtcNow.AddSeconds(1), Array.Empty<HardwareDeviceSnapshot>(), "Healthy");
        await Task.Delay(80, TestContext.Current.CancellationToken);
        cts.Cancel();
        await task;

        Assert.Equal(2, calls);
    }
}
