using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TheSpark.HardwareMonitor.App.Services;
using TheSpark.HardwareMonitor.Core.Ipc;
using Xunit;

namespace TheSpark.HardwareMonitor.App.Tests;

public sealed class AgentIpcClientTests
{
    [Fact]
    public async Task ClientRequestsCurrentStatusOverLocalPipe()
    {
        var testToken = TestContext.Current.CancellationToken;
        var pipeName = $"HardwareMonitor.App.Tests.{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(testToken);
            using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
            var request = JsonSerializer.Deserialize<AgentMessage>(
                await reader.ReadLineAsync(testToken) ?? throw new EndOfStreamException());
            Assert.NotNull(request);
            Assert.Equal(AgentProtocol.GetStatus, request!.Type);

            var status = new AgentStatusPayload(
                "Healthy",
                DateTimeOffset.Parse("2026-08-23T07:20:00Z"),
                DateTimeOffset.Parse("2026-08-23T07:19:59Z"),
                0,
                null,
                null);
            var response = new AgentMessage(
                AgentProtocol.Version,
                AgentProtocol.Status,
                request.RequestId,
                status);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response) + "\n");
            await server.WriteAsync(bytes, testToken);
            await server.FlushAsync(testToken);
        }, testToken);

        var client = new AgentIpcClient(pipeName, TimeSpan.FromSeconds(2));
        var result = await client.GetStatusAsync(testToken);

        Assert.Equal("Healthy", result.HealthState);
        Assert.Equal(0, result.ConsecutiveFailures);
        await serverTask;
    }
}
