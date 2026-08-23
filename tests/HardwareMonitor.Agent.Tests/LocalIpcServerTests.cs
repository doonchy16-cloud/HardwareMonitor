using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TheSpark.HardwareMonitor.Agent;
using TheSpark.HardwareMonitor.Core.Ipc;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Sensors;
using Xunit;

namespace TheSpark.HardwareMonitor.Agent.Tests;

public sealed class LocalIpcServerTests
{
    [Fact]
    public async Task CurrentStatusRequestReturnsAgentHealthAndLatestSnapshot()
    {
        await using var host = new AgentHost(new IdleProvider(), TimeSpan.FromSeconds(1));
        var pipeName = $"HardwareMonitor.Tests.{Guid.NewGuid():N}";
        await using var server = new LocalIpcServer(host, pipeName);
        using var serverCts = new CancellationTokenSource();
        var serverTask = server.RunAsync(serverCts.Token);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        var request = new AgentMessage(AgentProtocol.Version, AgentProtocol.GetStatus, "request-1");
        await WriteLineAsync(client, JsonSerializer.Serialize(request));
        var response = JsonSerializer.Deserialize<AgentMessage>(await ReadLineAsync(client));

        Assert.NotNull(response);
        Assert.Equal(AgentProtocol.Status, response!.Type);
        Assert.Equal("request-1", response.RequestId);
        Assert.NotNull(response.Status);
        Assert.Equal("Stopped", response.Status!.HealthState);

        serverCts.Cancel();
        await serverTask;
    }

    [Fact]
    public async Task OversizedMessageIsRejectedWithoutCrashingServer()
    {
        await using var host = new AgentHost(new IdleProvider(), TimeSpan.FromSeconds(1));
        var pipeName = $"HardwareMonitor.Tests.{Guid.NewGuid():N}";
        await using var server = new LocalIpcServer(host, pipeName);
        using var serverCts = new CancellationTokenSource();
        var serverTask = server.RunAsync(serverCts.Token);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        await WriteLineAsync(client, new string('x', AgentProtocol.MaxMessageBytes + 1));
        var response = JsonSerializer.Deserialize<AgentMessage>(await ReadLineAsync(client));

        Assert.NotNull(response);
        Assert.Equal(AgentProtocol.Error, response!.Type);
        Assert.Contains("too large", response.Error!, StringComparison.OrdinalIgnoreCase);

        serverCts.Cancel();
        await serverTask;
    }

    [Fact]
    public async Task DesktopDisconnectDoesNotStopMonitoringAgent()
    {
        var provider = new CountingProvider();
        await using var host = new AgentHost(provider, TimeSpan.FromMilliseconds(2));
        var pipeName = $"HardwareMonitor.Tests.{Guid.NewGuid():N}";
        await using var server = new LocalIpcServer(host, pipeName);
        using var hostCts = new CancellationTokenSource();
        using var serverCts = new CancellationTokenSource();
        var hostTask = host.RunAsync(hostCts.Token);
        var serverTask = server.RunAsync(serverCts.Token);

        await WaitUntilAsync(() => provider.ReadCount >= 2, TimeSpan.FromSeconds(3));
        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(5000);
        }

        var before = provider.ReadCount;
        await WaitUntilAsync(() => provider.ReadCount > before, TimeSpan.FromSeconds(3));
        Assert.True(provider.ReadCount > before);

        hostCts.Cancel();
        serverCts.Cancel();
        await hostTask;
        await serverTask;
    }

    private static async Task WriteLineAsync(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<string> ReadLineAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        return await reader.ReadLineAsync() ?? throw new EndOfStreamException();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopAt = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= stopAt)
            {
                throw new TimeoutException("Condition was not reached.");
            }
            await Task.Delay(10);
        }
    }

    private sealed class IdleProvider : ISensorProvider
    {
        public Task<HardwareSnapshot> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(HardwareSnapshot.Empty(DateTimeOffset.UtcNow));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingProvider : ISensorProvider
    {
        private int _readCount;
        public int ReadCount => Volatile.Read(ref _readCount);

        public Task<HardwareSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readCount);
            return Task.FromResult(new HardwareSnapshot(
                DateTimeOffset.UtcNow,
                Array.Empty<HardwareDeviceSnapshot>(),
                "Healthy"));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
